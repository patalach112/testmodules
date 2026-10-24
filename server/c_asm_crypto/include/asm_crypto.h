#ifndef ASM_CRYPTO_H
#define ASM_CRYPTO_H

/*
 * Public C ABI of the asm_crypto core library.
 *
 * The library performs in-memory buffer transformations backed by an
 * x86-64 inline assembly implementation. It is intentionally kept free of
 * C++ symbols so it can be linked from any language with a C FFI.
 */

#include <stddef.h>
#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

#define ASM_CRYPTO_VERSION_MAJOR 1
#define ASM_CRYPTO_VERSION_MINOR 0
#define ASM_CRYPTO_VERSION_PATCH 0

/* Returns the library version as a NUL-terminated semantic version string. */
const char *asm_crypto_version(void);

/*
 * Reversible XOR transform.
 *
 * XORs len bytes from src into dst using a 64-bit key that repeats every
 * eight bytes. src and dst may point to the same buffer (in-place use);
 * otherwise the regions must not partially overlap.
 *
 * Security note: a fixed repeating-key XOR is obfuscation, not encryption.
 * It is a fast in-memory primitive and must never be relied on for
 * confidentiality. Transport confidentiality is provided by TLS terminated
 * at the Cloudflare edge.
 *
 * Returns len, or 0 when len is zero.
 */
size_t asm_crypto_xor(const void *src, void *dst, size_t len, uint64_t key);

/* In-place convenience wrapper around asm_crypto_xor. */
void asm_crypto_xor_in_place(void *buffer, size_t len, uint64_t key);

#ifdef __cplusplus
} /* extern "C" */
#endif

#endif /* ASM_CRYPTO_H */
