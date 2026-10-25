#include "binary_packer/binary_pack.h"

#include <stdint.h>
#include <string.h>

#include "bp_endian.h"

size_t bp_packed_size(size_t payload_length)
{
    /* Header + payload + CRC, guarded against size_t overflow. */
    if (payload_length > SIZE_MAX - BP_OVERHEAD)
        return SIZE_MAX;
    return BP_OVERHEAD + payload_length;
}

size_t bp_write_header(void *dst, size_t dst_capacity,
                       uint8_t opcode, uint8_t flags,
                       uint32_t sequence, uint32_t payload_length)
{
    uint8_t *out = (uint8_t *)dst;

    if (dst_capacity < bp_packed_size(payload_length))
        return 0;

    out[0] = BP_MAGIC_0;
    out[1] = BP_MAGIC_1;
    out[2] = opcode;
    out[3] = flags;
    bp_put_be32(out + 4, sequence);
    bp_put_be32(out + 8, payload_length);
    return BP_HEADER_SIZE;
}

size_t bp_finalize(void *dst, uint32_t payload_length)
{
    uint8_t *out = (uint8_t *)dst;
    const size_t covered = BP_HEADER_SIZE + (size_t)payload_length;
    const uint32_t checksum = bp_crc32c(out, covered);

    bp_put_be32(out + covered, checksum);
    return covered + BP_CRC_SIZE;
}

size_t bp_pack(void *dst, size_t dst_capacity,
               uint8_t opcode, uint8_t flags,
               uint32_t sequence,
               const void *payload, uint32_t payload_length)
{
    uint8_t *out = (uint8_t *)dst;

    if (bp_write_header(dst, dst_capacity, opcode, flags, sequence,
                        payload_length) != BP_HEADER_SIZE)
        return 0;

    if (payload_length > 0u) {
        if (payload == NULL)
            return 0;
        memcpy(out + BP_HEADER_SIZE, payload, payload_length);
    }

    return bp_finalize(dst, payload_length);
}

bp_status_t bp_parse(const void *buffer, size_t buffer_length,
                     bp_header_t *out_header,
                     const uint8_t **out_payload,
                     size_t *out_payload_length)
{
    const uint8_t *in = (const uint8_t *)buffer;

    /* A packet needs at least header + crc, even with an empty payload. */
    if (buffer_length < BP_OVERHEAD)
        return BP_ERR_TOO_SHORT;

    if (in[0] != BP_MAGIC_0 || in[1] != BP_MAGIC_1)
        return BP_ERR_MAGIC;

    const uint32_t payload_length = bp_get_be32(in + 8);
    const size_t total = bp_packed_size(payload_length);
    if (total == SIZE_MAX || total > buffer_length)
        return BP_ERR_LENGTH;

    /* Checksum covers header and payload, excluding the CRC field. */
    const size_t covered = BP_HEADER_SIZE + (size_t)payload_length;
    const uint32_t expected = bp_crc32c(in, covered);
    const uint32_t received = bp_get_be32(in + covered);
    if (expected != received)
        return BP_ERR_CRC;

    if (out_header != NULL) {
        out_header->opcode = in[2];
        out_header->flags = in[3];
        out_header->sequence = bp_get_be32(in + 4);
        out_header->payload_length = payload_length;
    }
    if (out_payload != NULL)
        *out_payload = in + BP_HEADER_SIZE;
    if (out_payload_length != NULL)
        *out_payload_length = (size_t)payload_length;

    return BP_OK;
}
