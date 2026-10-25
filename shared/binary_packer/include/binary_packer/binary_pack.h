#ifndef BINARY_PACKER_BINARY_PACK_H
#define BINARY_PACKER_BINARY_PACK_H

/*
 * Binary Packet Protocol (BPP), version 1.
 *
 * Wire layout, all multi-byte integers in network byte order (big endian):
 *
 *   offset  size  field
 *   0       2     magic bytes 0x50 0x4B ('P', 'K')
 *   2       1     packet type / opcode
 *   3       1     flags
 *   4       4     sequence number (uint32, big endian)
 *   8       4     payload length  (uint32, big endian)
 *   12      N     payload (N = payload length, may be zero)
 *   12+N    4     CRC32-C checksum over bytes [0, 12+N), big endian
 *
 * Total packet size is therefore BP_HEADER_SIZE + N + BP_CRC_SIZE.
 *
 * The checksum is CRC-32C (Castagnoli), the variant implemented by the
 * SSE4.2 crc32 instruction, not the CRC-32 used by zlib/Ethernet.
 */

#include <stddef.h>
#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

#define BP_MAGIC_0 0x50u /* 'P' */
#define BP_MAGIC_1 0x4Bu /* 'K' */

#define BP_HEADER_SIZE 12u
#define BP_CRC_SIZE 4u
#define BP_OVERHEAD (BP_HEADER_SIZE + BP_CRC_SIZE) /* framing cost, 16 bytes */

/* Validation results returned by bp_parse(). */
typedef enum bp_status {
    BP_OK = 0,
    BP_ERR_TOO_SHORT, /* buffer cannot hold even the header + crc        */
    BP_ERR_MAGIC,     /* magic bytes do not match                         */
    BP_ERR_LENGTH,    /* declared payload length exceeds the buffer       */
    BP_ERR_CRC        /* checksum does not match the received bytes       */
} bp_status_t;

/* Host-order view of a parsed header. The wire bytes are never cast to
 * this struct; it is filled field by field to stay alignment-safe. */
typedef struct bp_header {
    uint8_t opcode;
    uint8_t flags;
    uint32_t sequence;
    uint32_t payload_length;
} bp_header_t;

/*
 * CRC-32C (Castagnoli) of len bytes. Returns 0 for an empty buffer.
 * Uses the SSE4.2 crc32 instruction after a runtime CPUID check; on
 * other architectures the portable bitwise implementation is used.
 */
uint32_t bp_crc32c(const void *data, size_t len);

/* Total wire size for a payload, or SIZE_MAX when it would overflow. */
size_t bp_packed_size(size_t payload_length);

/*
 * Writes a complete packet into dst. Never allocates.
 *
 * Returns the number of bytes written, or 0 when dst_capacity is too
 * small or payload_length exceeds the uint32 wire field. payload may be
 * NULL only when payload_length is zero.
 */
size_t bp_pack(void *dst, size_t dst_capacity,
               uint8_t opcode, uint8_t flags,
               uint32_t sequence,
               const void *payload, uint32_t payload_length);

/*
 * Writes just the 12-byte header and reserves the payload/crc area.
 * Returns BP_HEADER_SIZE on success, 0 when the capacity cannot hold
 * the worst-case framing. Pair with bp_finalize() after filling the
 * payload region in place (zero-copy path).
 */
size_t bp_write_header(void *dst, size_t dst_capacity,
                       uint8_t opcode, uint8_t flags,
                       uint32_t sequence, uint32_t payload_length);

/*
 * Computes the CRC over header plus the payload that now sits in dst and
 * appends it. payload_length must match the value given to
 * bp_write_header(). Returns the total packet size, or 0 on overflow.
 */
size_t bp_finalize(void *dst, uint32_t payload_length);

/*
 * Validates a received buffer. On BP_OK, out_header holds the decoded
 * fields and *out_payload points into the caller's buffer (zero copy)
 * with *out_payload_length bytes. Output pointers may be NULL.
 */
bp_status_t bp_parse(const void *buffer, size_t buffer_length,
                     bp_header_t *out_header,
                     const uint8_t **out_payload,
                     size_t *out_payload_length);

#ifdef __cplusplus
} /* extern "C" */
#endif

#endif /* BINARY_PACKER_BINARY_PACK_H */
