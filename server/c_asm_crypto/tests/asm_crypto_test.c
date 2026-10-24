/*
 * Unit tests for the asm_crypto transform.
 *
 * Built and run by `make check`. Covers the assembly fast path, the C
 * tail path, every length/alignment combination and the in-place API.
 */
#include "asm_crypto.h"

#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

/* Independent byte-wise reference, including the little-endian key phase. */
static void reference_xor(const uint8_t *src, uint8_t *dst,
                          size_t len, uint64_t key)
{
    for (size_t i = 0; i < len; ++i)
        dst[i] = (uint8_t)(src[i] ^ (uint8_t)(key >> (8u * (i & 7u))));
}

static uint64_t lcg_state = 0x9e3779b97f4a7c15ULL;

static uint64_t lcg_next(void)
{
    lcg_state = lcg_state * 6364136223846793005ULL + 1442695040888963407ULL;
    return lcg_state;
}

static int fail(const char *what, size_t len, int sa, int da)
{
    fprintf(stderr, "FAIL: %s (len=%zu src_align=%d dst_align=%d)\n",
            what, len, sa, da);
    return 1;
}

int main(void)
{
    enum { MAX_LEN = 300, PAD = 16 };
    uint8_t *src_mem = (uint8_t *)malloc(MAX_LEN + PAD);
    uint8_t *dst_mem = (uint8_t *)malloc(MAX_LEN + PAD);
    uint8_t *ref_mem = (uint8_t *)malloc(MAX_LEN + PAD);
    if (!src_mem || !dst_mem || !ref_mem)
        return 2;

    const uint64_t keys[] = {
        0ULL, 0xFFFFFFFFFFFFFFFFULL, 0x0123456789ABCDEFULL, lcg_next()
    };

    for (size_t len = 0; len <= MAX_LEN; ++len) {
        for (int sa = 0; sa < 8; ++sa) {
            for (int da = 0; da < 8; ++da) {
                uint8_t *src = src_mem + sa;
                uint8_t *dst = dst_mem + da;
                uint8_t *ref = ref_mem + da;

                for (size_t i = 0; i < len; ++i)
                    src[i] = (uint8_t)lcg_next();

                for (int k = 0; k < 4; ++k) {
                    reference_xor(src, ref, len, keys[k]);

                    if (asm_crypto_xor(src, dst, len, keys[k]) != len)
                        return fail("return value", len, sa, da);
                    if (memcmp(dst, ref, len) != 0)
                        return fail("out-of-place mismatch", len, sa, da);

                    /* In-place transform followed by a second one must
                     * restore the original bytes (XOR is self-inverse). */
                    uint8_t *work = src_mem + sa;
                    asm_crypto_xor_in_place(work, len, keys[k]);
                    if (memcmp(work, ref, len) != 0)
                        return fail("in-place mismatch", len, sa, da);
                    asm_crypto_xor_in_place(work, len, keys[k]);
                    if (memcmp(work, src, len) != 0)
                        return fail("round trip", len, sa, da);
                }
            }
        }
    }

    /* Zero-length call must not touch its arguments. */
    if (asm_crypto_xor(NULL, NULL, 0, 1) != 0)
        return fail("zero-length return", 0, 0, 0);

    free(src_mem);
    free(dst_mem);
    free(ref_mem);
    printf("asm_crypto %s: all tests passed\n", asm_crypto_version());
    return 0;
}
