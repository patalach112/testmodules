#include "gateway/session.hpp"

#include "asm_crypto.h"
#include "binary_packer/opcodes.h"
#include "binary_packer/packet.hpp"
#include "gateway/log.hpp"

#include <boost/asio/buffer.hpp>
#include <boost/asio/dispatch.hpp>
#include <chrono>
#include <functional>
#include <span>
#include <string>
#include <utility>

namespace gateway {

namespace beast = boost::beast;
namespace net = boost::asio;
namespace websocket = beast::websocket;

Session::Session(net::ip::tcp::socket &&socket, std::uint64_t crypto_key)
    : ws_(std::move(socket)), crypto_key_(crypto_key)
{
}

void Session::run()
{
    // Hop onto the socket's strand so the whole session is serialized.
    net::dispatch(ws_.get_executor(),
                  std::bind_front(&Session::accept, shared_from_this()));
}

void Session::accept()
{
    // Bound the upgrade handshake; idle TCP connections never linger.
    ws_.next_layer().expires_after(std::chrono::seconds(30));
    ws_.read_message_max(kMaxFrameSize);

    // Reads the buffered HTTP Upgrade request and replies with HTTP 101.
    // Cloudflare already validated the edge TLS certificate; here we only
    // speak the WebSocket subprotocol (RFC 6455) over plain TCP.
    ws_.async_accept(
        std::bind_front(&Session::on_accept, shared_from_this()));
}

void Session::on_accept(beast::error_code ec)
{
    if (ec) {
        Logger::warn("websocket upgrade failed: " + ec.message());
        return;
    }

    // After the upgrade, liveness is driven by RFC 6455 ping/pong frames;
    // Beast answers pings automatically, so no transport timeout is wanted.
    ws_.next_layer().expires_never();
    read();
}

void Session::read()
{
    // Client frames arrive masked; Beast unmasks them into rx_ transparently.
    ws_.async_read(rx_,
                   std::bind_front(&Session::on_read, shared_from_this()));
}

void Session::on_read(beast::error_code ec, std::size_t /*bytes*/)
{
    // A normal close handshake is an expected end of lifecycle, not an error.
    if (ec == websocket::error::closed)
        return;
    if (ec) {
        Logger::warn("websocket read failed: " + ec.message());
        return;
    }

    const std::size_t size = net::buffer_size(rx_.data());
    const auto *wire = static_cast<const std::uint8_t *>(rx_.data().data());

    // The parser only views the receive buffer; no bytes are copied.
    binary_packer::PacketParser parser(wire, size);
    if (parser.parse() != BP_OK) {
        send_protocol_error(parser.status());
        return;
    }

    const auto incoming = parser.payload();
    tx_.resize(binary_packer::packed_size(incoming.size()));

    // Reserve the full frame, then run the transform straight into the
    // payload region: the only copy is the unavoidable transform write.
    binary_packer::PacketBuilder builder(tx_);
    std::uint8_t *outgoing = builder.begin_payload(
        BP_OP_ECHO, parser.flags(), parser.sequence(),
        static_cast<std::uint32_t>(incoming.size()));
    if (outgoing == nullptr) {
        send_protocol_error(BP_ERR_LENGTH);
        return;
    }
    if (!incoming.empty())
        asm_crypto_xor(incoming.data(), outgoing, incoming.size(),
                       crypto_key_);
    builder.finalize(static_cast<std::uint32_t>(incoming.size()));

    // Framed binary on the wire; BPP payloads are arbitrary bytes.
    ws_.binary(true);
    ws_.async_write(net::buffer(tx_),
                    std::bind_front(&Session::on_write, shared_from_this()));
}

void Session::send_protocol_error(bp_status_t reason)
{
    Logger::warn("malformed packet, status " + std::to_string(reason));

    const std::uint8_t status = static_cast<std::uint8_t>(reason);
    tx_.resize(binary_packer::packed_size(1));
    binary_packer::PacketBuilder builder(tx_);
    builder.build(BP_OP_ERROR, 0, 0, std::span<const std::uint8_t>(&status, 1));

    ws_.binary(true);
    ws_.async_write(net::buffer(tx_),
                    std::bind_front(&Session::on_write, shared_from_this()));
}

void Session::on_write(beast::error_code ec, std::size_t bytes)
{
    if (ec) {
        Logger::warn("websocket write failed: " + ec.message());
        return;
    }

    // Frame fully sent: release the receive buffer and the tx staging area.
    rx_.consume(bytes);
    tx_.clear();
    read();
}

} // namespace gateway
