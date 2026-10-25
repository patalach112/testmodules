#include "wsclient/ws_client.hpp"

#include "asm_crypto.h"
#include "binary_packer/opcodes.h"
#include "binary_packer/packet.hpp"

#include <algorithm>
#include <cctype>
#include <charconv>
#include <chrono>
#include <cstdint>
#include <cstdlib>
#include <cstring>
#include <exception>
#include <iostream>
#include <iterator>
#include <span>
#include <string>
#include <string_view>
#include <vector>

namespace {

struct CliArgs {
    std::string url;
    std::string message = "hello from ws_client";
    std::uint64_t key = 0x0123456789ABCDEFull;
    int count = 1;
    // Binary by default: the gateway transforms frames into arbitrary bytes
    // that are not legal UTF-8 text. --text forces the text opcode.
    bool binary = true;
    bool roundtrip = false;
    // --packet wraps the message in the Binary Packet Protocol instead of
    // sending it as a raw WebSocket frame.
    bool use_packet = false;
    std::chrono::seconds timeout{10};
};

void usage()
{
    std::cerr
        << "usage: ws_client [options] ws://HOST[:PORT][/PATH] [MESSAGE]\n"
        << "options:\n"
        << "  --binary            send a binary frame (the default)\n"
        << "  --text              force a text frame instead\n"
        << "  --packet            use the Binary Packet Protocol framing\n"
        << "                      (12-byte header, CRC32-C, typed opcode)\n"
        << "  --count N           send N frames, verifying every reply\n"
        << "  --key DEC|0xHEX     asm_crypto key (default: 0x0123456789ABCDEF)\n"
        << "  --roundtrip         reverse the XOR on each reply and check it\n"
        << "                      restores the original payload\n"
        << "  --timeout SECONDS   per-operation timeout (default: 10)\n"
        << "  MESSAGE '-' reads the payload from standard input\n";
}

bool parse_u64(std::string_view raw, std::uint64_t &out)
{
    int base = 10;
    if (raw.size() > 2 && raw[0] == '0' &&
        (raw[1] == 'x' || raw[1] == 'X')) {
        raw.remove_prefix(2);
        base = 16;
    }
    const auto [ptr, ec] =
        std::from_chars(raw.data(), raw.data() + raw.size(), out, base);
    return ec == std::errc{} && ptr == raw.data() + raw.size();
}

/*
 * Splits ws://host:port/path into the client options. Kept hand-rolled on
 * purpose: pulling in a URL library for three delimiters is not worth a
 * dependency in a module meant to be copied between projects.
 */
bool parse_url(const std::string &url, wsclient::ClientOptions &out)
{
    constexpr std::string_view scheme = "ws://";
    if (url.rfind(scheme, 0) != 0) {
        std::cerr << "error: only plain ws:// URLs are supported (got: "
                  << url << ")\n";
        return false;
    }

    const std::string rest = url.substr(scheme.size());
    const auto slash = rest.find('/');
    const std::string host_port = rest.substr(0, slash);
    out.target = (slash == std::string::npos) ? "/" : rest.substr(slash);

    const auto colon = host_port.find(':');
    if (colon == std::string::npos) {
        out.host = host_port;
        out.port = "80";
    } else {
        out.host = host_port.substr(0, colon);
        out.port = host_port.substr(colon + 1);
    }
    return !out.host.empty();
}

std::string hex_dump(std::span<const std::uint8_t> bytes)
{
    static const char digits[] = "0123456789abcdef";
    std::string out;
    out.reserve(bytes.size() * 2);
    for (std::uint8_t b : bytes) {
        out.push_back(digits[b >> 4]);
        out.push_back(digits[b & 0x0f]);
    }
    return out;
}

/*
 * One BPP request/response: frame the payload, send it, validate the
 * reply's checksum and sequence, and optionally prove the asm_crypto
 * round trip. Returns false (after printing the reason) on any failure.
 */
bool exchange_packet(wsclient::Client &client, std::uint32_t sequence,
                     const std::vector<std::uint8_t> &payload,
                     std::uint64_t key, bool roundtrip, int frame_index)
{
    std::vector<std::uint8_t> frame(
        binary_packer::packed_size(payload.size()));
    binary_packer::PacketBuilder builder(frame);
    if (builder.build(BP_OP_ECHO, 0, sequence, payload) != frame.size()) {
        std::cerr << "error: packet builder rejected the payload\n";
        return false;
    }

    const std::vector<std::uint8_t> reply = client.send(frame, true);
    binary_packer::PacketParser parser(reply);
    const bp_status_t status = parser.parse();
    if (status != BP_OK) {
        std::cerr << "FAIL frame " << frame_index
                  << ": reply is not a valid packet (status " << status
                  << ")\n";
        return false;
    }
    if (parser.opcode() == BP_OP_ERROR) {
        const auto body = parser.payload();
        const unsigned code = body.empty() ? 0u : body[0];
        std::cerr << "FAIL frame " << frame_index
                  << ": gateway returned BP_OP_ERROR, status " << code
                  << '\n';
        return false;
    }
    if (parser.sequence() != sequence) {
        std::cerr << "FAIL frame " << frame_index << ": sequence "
                  << parser.sequence() << " != " << sequence << '\n';
        return false;
    }

    const auto body = parser.payload();
    if (roundtrip) {
        std::vector<std::uint8_t> restored(body.size());
        if (!body.empty())
            asm_crypto_xor(body.data(), restored.data(), body.size(), key);
        if (restored != payload) {
            std::cerr << "FAIL frame " << frame_index
                      << ": payload round trip mismatch\n";
            return false;
        }
        std::cout << "frame " << frame_index << ": PASS (seq " << sequence
                  << ", " << body.size() << " bytes transformed and "
                     "restored, CRC32-C valid)\n";
    } else {
        std::cout << "frame " << frame_index << ": opcode 0x" << std::hex
                  << static_cast<unsigned>(parser.opcode()) << std::dec
                  << " seq " << sequence << " payload " << body.size()
                  << " bytes 0x" << hex_dump(body) << '\n';
    }
    return true;
}

} // namespace

int main(int argc, char **argv)
{
    CliArgs args;
    std::vector<std::string> positional;

    for (int i = 1; i < argc; ++i) {
        const std::string_view flag = argv[i];
        auto need_value = [&](std::string_view name) -> std::string_view {
            if (i + 1 >= argc) {
                std::cerr << "error: " << name << " requires a value\n";
                std::exit(2);
            }
            return argv[++i];
        };

        // A lone dash is the conventional "read from stdin" positional,
        // not an option flag.
        if (flag == "-") {
            positional.emplace_back("-");
        } else if (flag == "--binary") {
            args.binary = true;
        } else if (flag == "--text") {
            args.binary = false;
        } else if (flag == "--packet") {
            args.use_packet = true;
        } else if (flag == "--roundtrip") {
            args.roundtrip = true;
        } else if (flag == "--count") {
            std::uint64_t count = 0;
            if (!parse_u64(need_value("--count"), count) || count == 0) {
                std::cerr << "error: --count must be a positive integer\n";
                return 2;
            }
            args.count = static_cast<int>(count);
        } else if (flag == "--key") {
            if (!parse_u64(need_value("--key"), args.key)) {
                std::cerr << "error: --key must be decimal or 0x-hex uint64\n";
                return 2;
            }
        } else if (flag == "--timeout") {
            std::uint64_t seconds = 0;
            if (!parse_u64(need_value("--timeout"), seconds) || seconds == 0) {
                std::cerr << "error: --timeout must be a positive integer\n";
                return 2;
            }
            args.timeout = std::chrono::seconds(seconds);
        } else if (flag == "-h" || flag == "--help") {
            usage();
            return 0;
        } else if (!flag.empty() && flag[0] == '-') {
            std::cerr << "error: unknown option " << flag << '\n';
            usage();
            return 2;
        } else {
            positional.emplace_back(flag);
        }
    }

    if (positional.empty()) {
        usage();
        return 2;
    }
    args.url = positional[0];
    if (positional.size() >= 2)
        args.message = positional[1];
    if (args.message == "-")
        args.message.assign(std::istreambuf_iterator<char>(std::cin),
                            std::istreambuf_iterator<char>());

    wsclient::ClientOptions options;
    if (!parse_url(args.url, options))
        return 2;
    options.timeout = args.timeout;

    const std::vector<std::uint8_t> payload(args.message.begin(),
                                            args.message.end());

    try {
        wsclient::Client client(options);
        client.connect();
        std::cout << "connected to " << args.url << '\n';

        for (int frame = 1; frame <= args.count; ++frame) {
            if (args.use_packet) {
                if (!exchange_packet(client,
                                     static_cast<std::uint32_t>(frame), payload,
                                     args.key, args.roundtrip, frame))
                    return 1;
                continue;
            }

            const std::vector<std::uint8_t> reply =
                client.send(payload, args.binary);

            if (args.roundtrip) {
                // XOR is self-inverse: applying the same key again must
                // restore the exact bytes we sent.
                std::vector<std::uint8_t> restored(reply.size());
                asm_crypto_xor(reply.data(), restored.data(),
                               reply.size(), args.key);

                if (restored != payload) {
                    std::cerr << "FAIL frame " << frame
                              << ": round trip mismatch (" << reply.size()
                              << " bytes)\n";
                    return 1;
                }
                std::cout << "frame " << frame << ": PASS ("
                          << reply.size() << " bytes transformed and "
                             "restored)\n";
            } else {
                // The gateway returns XOR-transformed bytes, which are not
                // guaranteed printable; show hex and add text only when safe.
                const bool printable =
                    std::all_of(reply.begin(), reply.end(),
                                [](unsigned char c) {
                                    return std::isprint(c) != 0;
                                });
                std::cout << "frame " << frame << ": reply "
                          << reply.size() << " bytes 0x" << hex_dump(reply);
                if (printable)
                    std::cout << " \""
                              << std::string(reply.begin(), reply.end())
                              << '"';
                std::cout << '\n';
            }
        }

        client.close();
        return 0;
    } catch (const std::exception &e) {
        std::cerr << "error: " << e.what() << '\n';
        return 1;
    }
}
