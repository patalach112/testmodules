#include "binary_packer/binary_pack.h"
#include "bp_crc_internal.h"

#include <stdatomic.h>
#include <stdint.h>
#include <string.h>

/*
 * CRC-32C (Castagnoli), reflected form.
 *
 * The SSE4.2 crc32 instruction and this bitwise loop implement the same
 * polynomial, so the two paths are bit-for-bit interchangeable. The
 * software version is streaming: callers may feed it an initial value
 * (0xFFFFFFFFu for a fresh checksum) and continue across chunks.
 */
uint32_t bp_crc32c_soft(uint32_t crc, const uint8_t *data, size_t len)
{
    while (len-- > 0) {
        crc ^= *data++;
        for (int bit = 0; bit < 8; ++bit) {
            const uint32_t mask = (uint32_t)(-(int32_t)(crc & 1u));
            crc = (crc >> 1) ^ (BP_CRC32C_POLY & mask);
        }
    }
    return crc;
}

#if BP_CRC_HAVE_X86

/*
 * CPUID.1:ECX bit 20 reports SSE4.2. The probe runs once and its result
 * is cached atomically; after the first packet every call is a plain
 * atomic load.
 *
 * State encoding: 0 = unprobed, 1 = unsupported, 2 = supported.
 */
static atomic_uint hw_state;

#if defined(__x86_64__)
static int cpu_has_sse42(void)
{
    unsigned int eax, ebx, ecx, edx;
    __asm__ volatile ("cpuid"
                      : "=a"(eax), "=b"(ebx), "=c"(ecx), "=d"(edx)
                      : "a"(1u)
                      : "cc");
    return (ecx & (1u << 20)) != 0;
}
#else
/* 32-bit PIC keeps %ebx reserved for the GOT, so save it explicitly. */
static int cpu_has_sse42(void)
{
    unsigned int eax, ebx, ecx, edx;
    __asm__ volatile (
        "pushl %%ebx\n\t"
        "cpuid\n\t"
        "movl %%ebx, %[ebx_out]\n\t"
        "popl %%ebx"
        : "=a"(eax), [ebx_out] "=r"(ebx), "=c"(ecx), "=d"(edx)
        : "a"(1u)
        : "cc");
    return (ecx & (1u << 20)) != 0;
}
#endif

int bp_crc32c_hw_available(void)
{
    unsigned int state = atomic_load_explicit(&hw_state, memory_order_relaxed);
    if (state == 0u) {
        state = cpu_has_sse42() ? 2u : 1u;
        atomic_store_explicit(&hw_state, state, memory_order_relaxed);
    }
    return state == 2u;
}

/*
 * The target attribute emits an .arch directive for this function only,
 * so the crc32 instructions assemble without a global -msse4.2 flag and
 * the rest of the object still runs on baseline x86-64.
 *
 * Loads go through memcpy: the wire buffer has no alignment guarantee,
 * and GCC turns these into ordinary mov instructions at -O1 and above.
 */
__attribute__((target("sse4.2")))
uint32_t bp_crc32c_hw(uint32_t crc, const uint8_t *data, size_t len)
{
#if defined(__x86_64__)
    /* 64-bit mode: drain eight bytes per iteration with crc32q. */
    uint64_t crc64 = crc;
    while (len >= 8u) {
        uint64_t word;
        memcpy(&word, data, sizeof(word));
        __asm__ volatile ("crc32q %[word], %[crc]"
                          : [crc] "+r"(crc64)
                          : [word] "r"(word)
                          : "cc");
        data += 8;
        len -= 8;
    }
    crc = (uint32_t)crc64;
#endif

    while (len >= 4u) {
        uint32_t dword;
        memcpy(&dword, data, sizeof(dword));
        __asm__ volatile ("crc32l %[word], %[crc]"
                          : [crc] "+r"(crc)
                          : [word] "r"(dword)
                          : "cc");
        data += 4;
        len -= 4;
    }

    while (len-- > 0) {
        __asm__ volatile ("crc32b %[byte], %[crc]"
                          : [crc] "+r"(crc)
                          : [byte] "rm"(*data)
                          : "cc");
        ++data;
    }
    return crc;
}

uint32_t bp_crc32c(const void *data, size_t len)
{
    const uint8_t *bytes = (const uint8_t *)data;
    uint32_t crc = 0xFFFFFFFFu;

    if (bp_crc32c_hw_available())
        crc = bp_crc32c_hw(crc, bytes, len);
    else
        crc = bp_crc32c_soft(crc, bytes, len);

    return crc ^ 0xFFFFFFFFu;
}

#else /* non-x86: portable implementation only */

uint32_t bp_crc32c(const void *data, size_t len)
{
    const uint8_t *bytes = (const uint8_t *)data;
    return bp_crc32c_soft(0xFFFFFFFFu, bytes, len) ^ 0xFFFFFFFFu;
}

#endif
