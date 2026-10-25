// Compile-time and basic runtime check of the zero-allocation C++ wrapper.
// Uses always-evaluated checks rather than assert() so the test stays
// meaningful under -DNDEBUG.
#include "binary_packer/packet.hpp"
#include "binary_packer/opcodes.h"

#include <cstdint>
#include <cstring>
#include <span>
#include <vector>

namespace {

int failures = 0;

#define REQUIRE(cond)                                                  \
    do {                                                               \
        if (!(cond)) {                                                 \
            ++failures;                                                \
        }                                                              \
    } while (0)

// std::span equality is only provided by newer standard libraries, so
// compare contents explicitly instead of relying on operator==.
bool same_bytes(std::span<const std::uint8_t> a,
                std::span<const std::uint8_t> b)
{
    return a.size() == b.size() &&
           std::memcmp(a.data(), b.data(), a.size()) == 0;
}

} // namespace

int main()
{
    constexpr std::uint32_t sequence = 777;
    const std::vector<std::uint8_t> payload = {0xDE, 0xAD, 0xBE, 0xEF, 0x00, 0x11};

    std::vector<std::uint8_t> frame(binary_packer::packed_size(payload.size()));

    // One-shot API.
    binary_packer::PacketBuilder builder(frame);
    const std::size_t written =
        builder.build(BP_OP_ECHO, 0, sequence, payload);
    REQUIRE(written == frame.size());

    binary_packer::PacketParser parser(frame);
    REQUIRE(parser.parse() == BP_OK);
    REQUIRE(parser.opcode() == BP_OP_ECHO);
    REQUIRE(parser.sequence() == sequence);
    REQUIRE(same_bytes(parser.payload(), payload));

    // Zero-copy API: reserve the region and write through it directly.
    std::vector<std::uint8_t> direct(frame.size());
    binary_packer::PacketBuilder in_place(direct);
    std::uint8_t *region =
        in_place.begin_payload(BP_OP_ACK, 1, sequence,
                               static_cast<std::uint32_t>(payload.size()));
    REQUIRE(region != nullptr);
    for (std::size_t i = 0; i < payload.size(); ++i)
        region[i] = payload[i];
    REQUIRE(in_place.finalize(static_cast<std::uint32_t>(payload.size())) ==
            direct.size());

    binary_packer::PacketParser direct_parser(direct);
    REQUIRE(direct_parser.parse() == BP_OK);
    REQUIRE(direct_parser.opcode() == BP_OP_ACK);
    REQUIRE(direct_parser.flags() == 1);
    REQUIRE(same_bytes(direct_parser.payload(), payload));

    // Garbage input reports a failure instead of crashing.
    std::vector<std::uint8_t> garbage(32, 0xAB);
    binary_packer::PacketParser bad(garbage);
    REQUIRE(bad.parse() != BP_OK);
    REQUIRE(!bad.valid());
    REQUIRE(bad.payload().empty());

    if (failures != 0)
        return 1;
    return 0;
}
