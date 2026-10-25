#ifndef BINARY_PACKER_BP_CRC_INTERNAL_H
#define BINARY_PACKER_BP_CRC_INTERNAL_H

/*
 * Internal CRC entry points. Not part of the installed ABI: the unit
 * tests include this header so they can (1) verify the portable software
 * implementation against published CRC-32C vectors and (2) prove the
 * hardware and software paths produce identical results.
 */

#include <stddef.h>
#include <stdint.h>

/* Overridable on the compile line (e.g. -DBP_CRC_HAVE_X86=0) so the
 * portable path can be exercised even on x86 build machines. */
#ifndef BP_CRC_HAVE_X86
#if defined(__x86_64__) || defined(__i386__)
#define BP_CRC_HAVE_X86 1
#else
#define BP_CRC_HAVE_X86 0
#endif
#endif

/* Reflected Castagnoli polynomial (CRC-32C). */
#define BP_CRC32C_POLY 0x82F63B78u

/* One streaming step: update crc with len bytes, no init/final xor. */
uint32_t bp_crc32c_soft(uint32_t crc, const uint8_t *data, size_t len);

#if BP_CRC_HAVE_X86
/* Compiled with the sse4.2 target attribute; only call after the
 * runtime CPUID check in bp_crc32c_hw_available() succeeds. */
uint32_t bp_crc32c_hw(uint32_t crc, const uint8_t *data, size_t len);

/* Cached CPUID probe for the SSE4.2 feature bit (CPUID.1:ECX[20]). */
int bp_crc32c_hw_available(void);
#endif

#endif /* BINARY_PACKER_BP_CRC_INTERNAL_H */
