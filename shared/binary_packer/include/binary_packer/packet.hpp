#ifndef BINARY_PACKER_PACKET_HPP
#define BINARY_PACKER_PACKET_HPP

#include "binary_packer/binary_pack.h"

#include <cstddef>
#include <cstdint>
#include <span>

namespace binary_packer {

/* Total wire size for a payload, or SIZE_MAX on overflow. */
inline std::size_t packed_size(std::size_t payload_length)
{
    return bp_packed_size(payload_length);
}

/*
 * Writes BPP packets into caller-owned storage. The class never allocates;
 * the destination buffer and its capacity are bound at construction.
 *
 * Two write styles share the same wire format:
 *
 *   1. one-shot build(), which copies an existing payload;
 *   2. the zero-copy pair begin_payload()/finalize(): the caller writes
 *      directly into the reserved payload region (for example via the
 *      asm_crypto transform), then the checksum is computed once.
 */
class PacketBuilder {
public:
    PacketBuilder(std::uint8_t *buffer, std::size_t capacity)
        : buffer_(buffer), capacity_(capacity)
    {
    }

    explicit PacketBuilder(std::span<std::uint8_t> storage)
        : PacketBuilder(storage.data(), storage.size())
    {
    }

    /* Header + reserved region. Returns the payload pointer, or nullptr
     * when the storage cannot hold the framed packet. */
    std::uint8_t *begin_payload(std::uint8_t opcode, std::uint8_t flags,
                                std::uint32_t sequence,
                                std::uint32_t payload_length)
    {
        if (bp_write_header(buffer_, capacity_, opcode, flags, sequence,
                            payload_length) != BP_HEADER_SIZE)
            return nullptr;
        return buffer_ + BP_HEADER_SIZE;
    }

    /* Appends the CRC. Returns the total packet size, or 0 on overflow. */
    std::size_t finalize(std::uint32_t payload_length)
    {
        return bp_finalize(buffer_, payload_length);
    }

    /* One-shot path: header, payload copy, CRC. Returns 0 on error. */
    std::size_t build(std::uint8_t opcode, std::uint8_t flags,
                      std::uint32_t sequence,
                      std::span<const std::uint8_t> payload)
    {
        return bp_pack(buffer_, capacity_, opcode, flags, sequence,
                       payload.data(), static_cast<std::uint32_t>(payload.size()));
    }

private:
    std::uint8_t *buffer_;
    std::size_t capacity_;
};

/*
 * Validates a received frame and exposes its fields. The parser holds a
 * view of the caller's buffer only; no bytes are copied. It must not
 * outlive the buffer it was constructed from.
 */
class PacketParser {
public:
    PacketParser(const std::uint8_t *data, std::size_t size)
        : data_(data), size_(size)
    {
    }

    explicit PacketParser(std::span<const std::uint8_t> frame)
        : PacketParser(frame.data(), frame.size())
    {
    }

    bp_status_t parse()
    {
        const std::uint8_t *payload = nullptr;
        std::size_t payload_length = 0;
        status_ = bp_parse(data_, size_, &header_, &payload, &payload_length);
        payload_ = (status_ == BP_OK) ? payload : nullptr;
        payload_length_ = (status_ == BP_OK) ? payload_length : 0;
        return status_;
    }

    bool valid() const { return status_ == BP_OK; }
    bp_status_t status() const { return status_; }

    std::uint8_t opcode() const { return header_.opcode; }
    std::uint8_t flags() const { return header_.flags; }
    std::uint32_t sequence() const { return header_.sequence; }
    std::uint32_t payload_length() const { return header_.payload_length; }

    /* View into the original receive buffer; empty when invalid. */
    std::span<const std::uint8_t> payload() const
    {
        return valid() ? std::span<const std::uint8_t>(payload_, payload_length_)
                       : std::span<const std::uint8_t>();
    }

private:
    const std::uint8_t *data_;
    std::size_t size_;
    bp_header_t header_{};
    const std::uint8_t *payload_ = nullptr;
    std::size_t payload_length_ = 0;
    bp_status_t status_ = BP_ERR_TOO_SHORT;
};

} // namespace binary_packer

#endif // BINARY_PACKER_PACKET_HPP
