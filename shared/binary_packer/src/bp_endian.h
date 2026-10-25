#ifndef BINARY_PACKER_BP_ENDIAN_H
#define BINARY_PACKER_BP_ENDIAN_H

/*
 * Big-endian (network order) load/store helpers.
 *
 * The byte-shift formulations are strictly portable: they contain no
 * alignment requirement and no dependence on the host's endianness.
 * On little-endian x86 every supported compiler reduces them to a native
 * bwap + store; the builtin is selected explicitly where available so the
 * intent is visible in debug builds too.
 */

#include <stdint.h>

static inline uint32_t bp_bswap32(uint32_t value)
{
#if defined(__GNUC__) || defined(__clang__)
    return __builtin_bswap32(value);
#elif defined(_MSC_VER)
    return _byteswap_ulong(value);
#else
    return ((value & 0x000000FFu) << 24) |
           ((value & 0x0000FF00u) << 8)  |
           ((value & 0x00FF0000u) >> 8)  |
           ((value & 0xFF000000u) >> 24);
#endif
}

static inline void bp_put_be32(uint8_t *dst, uint32_t value)
{
#if defined(__BYTE_ORDER__) && __BYTE_ORDER__ == __ORDER_BIG_ENDIAN__
    const uint32_t ordered = value;
#elif defined(__BYTE_ORDER__) && __BYTE_ORDER__ == __ORDER_LITTLE_ENDIAN__
    const uint32_t ordered = bp_bswap32(value);
#else
    /* Unknown endianness: emit the bytes explicitly. */
    dst[0] = (uint8_t)(value >> 24);
    dst[1] = (uint8_t)(value >> 16);
    dst[2] = (uint8_t)(value >> 8);
    dst[3] = (uint8_t)(value);
    return;
#endif
    dst[0] = (uint8_t)(ordered);
    dst[1] = (uint8_t)(ordered >> 8);
    dst[2] = (uint8_t)(ordered >> 16);
    dst[3] = (uint8_t)(ordered >> 24);
}

static inline uint32_t bp_get_be32(const uint8_t *src)
{
#if defined(__BYTE_ORDER__) && defined(__ORDER_LITTLE_ENDIAN__) && \
    __BYTE_ORDER__ == __ORDER_LITTLE_ENDIAN__
    uint32_t raw;
    __builtin_memcpy(&raw, src, sizeof(raw)); /* alignment-safe load */
    return bp_bswap32(raw);
#else
    return ((uint32_t)src[0] << 24) | ((uint32_t)src[1] << 16) |
           ((uint32_t)src[2] << 8)  | (uint32_t)src[3];
#endif
}

#endif /* BINARY_PACKER_BP_ENDIAN_H */
