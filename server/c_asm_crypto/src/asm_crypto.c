#include "asm_crypto.h"

#include <stdint.h>

const char *asm_crypto_version(void)
{
    return "1.0.0";
}

/*
 * Key byte at phase i (i modulo 8).
 *
 * Derived by shifting instead of reading the key's memory representation,
 * so the result is identical on little- and big-endian machines: byte zero
 * is always the least significant byte. The x86-64 assembly path produces
 * the same sequence because x86-64 is little-endian.
 */
static inline uint8_t key_byte_at(uint64_t key, size_t i)
{
    return (uint8_t)(key >> (8u * (i & 7u)));
}

/*
 * The inline-assembly path requires both the x86-64 ISA and a compiler
 * that understands GNU extended asm (GCC and Clang do; MSVC x64 has no
 * inline assembler and falls through to the portable C path below).
 */
#if defined(__x86_64__) && defined(__GNUC__)
#define ASM_CRYPTO_HAVE_X86_64_ASM 1
#else
#define ASM_CRYPTO_HAVE_X86_64_ASM 0
#endif

#if ASM_CRYPTO_HAVE_X86_64_ASM

/*
 * Transform a run of complete 64-bit blocks.
 *
 * The pointers are plain byte pointers (uint8_t *). They are fed straight
 * into the assembler addressing modes without ever forming a uint64_t
 * lvalue in C, which keeps the code free of two classes of undefined
 * behavior when callers pass arbitrary byte buffers:
 *
 *   - strict-aliasing violations (a char object read through uint64_t *);
 *   - misaligned-pointer UB (callers need no alignment guarantee).
 *
 * Hardware side: movq accepts unaligned addresses on x86-64, so no
 * alignment guarantee is needed.
 *
 * Register allocation is expressed through operand constraints rather than
 * hardcoded registers, leaving the scheduler free to pick the actual
 * registers. Under the System V AMD64 ABI (Linux) it typically resolves to:
 *
 *   dst    - "r"  destination pointer  -> %rdi
 *   src    - "r"  source pointer       -> %rsi
 *   blocks - "r"  block count          -> %rdx
 *   key    - "r"  repeating XOR mask   -> %rcx
 *   i      - "+r" loop index           -> %r8
 *   tmp   - "=&r" scratch block       -> %r9
 *
 * The Windows x64 ABI uses different argument registers, but the same
 * constraint-driven allocation handles it unchanged.
 *
 * "memory" is a clobber so the compiler cannot cache or reorder the touched
 * memory across the loop; "=&r" marks tmp as an early-clobber output that
 * must not alias any input register.
 */
static void xor_blocks_asm(uint8_t *dst, const uint8_t *src,
                           size_t blocks, uint64_t key)
{
    uint64_t tmp;
    size_t i = 0;

    __asm__ volatile (
        ".p2align 4\n"                        /* align loop entry to 16 B   */
        "1:\n"
        "  movq  (%[src], %[i], 8), %[tmp]\n" /* tmp = src block i          */
        "  xorq  %[key], %[tmp]\n"            /* tmp ^= key                 */
        "  movq  %[tmp], (%[dst], %[i], 8)\n" /* dst block i = tmp          */
        "  incq  %[i]\n"                      /* ++i                        */
        "  cmpq  %[blocks], %[i]\n"           /* compare i with blocks      */
        "  jb    1b\n"                        /* loop while i < blocks      */
        : [tmp] "=&r" (tmp), [i] "+r" (i)
        : [dst] "r" (dst), [src] "r" (src),
          [blocks] "r" (blocks), [key] "r" (key)
        : "memory"
    );
}

#endif /* ASM_CRYPTO_HAVE_X86_64_ASM */

/*
 * Portable byte-wise transform used:
 *   - on non-x86-64 or non-GNU toolchains (including MSVC);
 *   - for the trailing 1..7 bytes on every platform.
 *
 * Only char-typed accesses are performed, so arbitrary source and
 * destination buffers are legal regardless of alignment or effective type.
 * GCC/Clang auto-vectorize this loop into word-sized operations when the
 * alias analysis allows it.
 */
static void xor_bytes(uint8_t *dst, const uint8_t *src,
                      size_t from, size_t len, uint64_t key)
{
    for (size_t i = from; i < len; ++i)
        dst[i] = (uint8_t)(src[i] ^ key_byte_at(key, i));
}

size_t asm_crypto_xor(const void *src, void *dst, size_t len, uint64_t key)
{
    if (len == 0)
        return 0;

    const uint8_t *s = (const uint8_t *)src;
    uint8_t *d = (uint8_t *)dst;

    /* Fast path: whole 8-byte blocks. */
    const size_t blocks = len / 8;
    if (blocks > 0) {
#if ASM_CRYPTO_HAVE_X86_64_ASM
        xor_blocks_asm(d, s, blocks, key);
#else
        xor_bytes(d, s, 0, blocks * 8, key);
#endif
    }

    /* Slow path: the trailing 1..7 bytes (empty when len is a multiple of 8). */
    xor_bytes(d, s, blocks * 8, len, key);

    return len;
}

void asm_crypto_xor_in_place(void *buffer, size_t len, uint64_t key)
{
    (void)asm_crypto_xor(buffer, buffer, len, key);
}
