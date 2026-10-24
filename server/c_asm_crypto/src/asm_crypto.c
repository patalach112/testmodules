#include "asm_crypto.h"

#include <stdint.h>
#include <string.h>

const char *asm_crypto_version(void)
{
    return "1.0.0";
}

#if defined(__x86_64__)

/*
 * Transform a run of complete 64-bit blocks.
 *
 * Register allocation is expressed through operand constraints instead of
 * hardcoded registers, so the compiler remains free to schedule around the
 * block. Under the System V AMD64 ABI (Linux x86-64) it typically picks:
 *
 *   dst    - "r"  destination pointer  -> %rdi
 *   src    - "r"  source pointer       -> %rsi
 *   blocks - "r"  block count          -> %rdx
 *   key    - "r"  repeating XOR mask   -> %rcx
 *   i      - "+r" loop index           -> %r8
 *   tmp    - "=&r" scratch block       -> %r9
 *
 * movq handles unaligned addresses on x86-64, so callers need no alignment
 * guarantees. "memory" is listed as a clobber so the compiler cannot cache
 * or reorder the touched memory across the loop; "=&r" marks tmp as an
 * early-clobber output that must not alias any input register.
 */
static void xor_blocks(uint64_t *dst, const uint64_t *src,
                       size_t blocks, uint64_t key)
{
    uint64_t tmp;
    size_t i = 0;

    __asm__ volatile (
        ".p2align 4\n"                   /* align loop entry to 16 bytes */
        "1:\n"
        "  movq  (%[src], %[i], 8), %[tmp]\n"  /* tmp = src[i]            */
        "  xorq  %[key], %[tmp]\n"             /* tmp ^= key              */
        "  movq  %[tmp], (%[dst], %[i], 8)\n"  /* dst[i] = tmp            */
        "  incq  %[i]\n"                       /* ++i                     */
        "  cmpq  %[blocks], %[i]\n"            /* compare i with blocks   */
        "  jb    1b\n"                          /* loop while i < blocks   */
        : [tmp] "=&r" (tmp), [i] "+r" (i)
        : [dst] "r" (dst), [src] "r" (src),
          [blocks] "r" (blocks), [key] "r" (key)
        : "memory"
    );
}

#else

/*
 * Portable fallback for non-x86-64 toolchains. Kept in the same translation
 * unit so the ABI is identical on every platform.
 */
static void xor_blocks(uint64_t *dst, const uint64_t *src,
                       size_t blocks, uint64_t key)
{
    for (size_t i = 0; i < blocks; ++i)
        dst[i] = src[i] ^ key;
}

#endif

size_t asm_crypto_xor(const void *src, void *dst, size_t len, uint64_t key)
{
    if (len == 0)
        return 0;

    const uint8_t *s = (const uint8_t *)src;
    uint8_t *d = (uint8_t *)dst;

    /* Fast path: whole 8-byte blocks go through the assembly routine. */
    const size_t blocks = len / 8;
    if (blocks > 0) {
        xor_blocks((uint64_t *)(void *)d,
                   (const uint64_t *)(const void *)s,
                   blocks, key);
    }

    /*
     * Slow path: any trailing 1..7 bytes. The 64-bit key repeats in
     * little-endian byte order, so byte i uses key_byte[i % 8].
     */
    const uint8_t *key_bytes = (const uint8_t *)&key;
    for (size_t i = blocks * 8; i < len; ++i)
        d[i] = s[i] ^ key_bytes[i & 7u];

    return len;
}

void asm_crypto_xor_in_place(void *buffer, size_t len, uint64_t key)
{
    (void)asm_crypto_xor(buffer, buffer, len, key);
}
