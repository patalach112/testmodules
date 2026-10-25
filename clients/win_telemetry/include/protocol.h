#ifndef WIN_TELEMETRY_PROTOCOL_H
#define WIN_TELEMETRY_PROTOCOL_H

#include "hw_stats.h"
#include <stddef.h>
#include <stdint.h>

#define TELEMETRY_BINARY_MAGIC 0x54454C4Du /* 'TELM' */
#define TELEMETRY_VERSION      1

#pragma pack(push, 1)
typedef struct {
    uint32_t magic;            /* 0x54454C4D ('TELM') */
    uint16_t version;          /* Protocol version: 1 */
    uint16_t flags;            /* Bit 0: Encrypted */
    uint64_t timestamp_ms;     /* Milliseconds since Unix epoch */
    uint64_t uptime_ms;        /* System uptime in ms */
    uint64_t tsc_cycles;       /* Serialized RDTSC raw cycles */
    uint64_t tsc_delta;        /* Cycles delta since last sample */
    uint32_t tsc_overhead;     /* Cycles for RDTSC measurement overhead */
    float    cpu_usage_pct;    /* System CPU usage percentage */
    uint64_t ram_total_bytes;  /* Total physical memory */
    uint64_t ram_avail_bytes;  /* Available physical memory */
    uint64_t ram_used_bytes;   /* Used physical memory */
    float    ram_usage_pct;    /* Memory load percentage */
    uint32_t process_pid;      /* Daemon PID */
    uint64_t process_ws_bytes; /* Daemon resident Working Set bytes */
    char     hostname[64];     /* Machine hostname (null terminated) */
} telemetry_binary_payload_t;
#pragma pack(pop)

/* Format hardware stats into a JSON string buffer. Returns length written, or -1 on error. */
int protocol_format_json(const hw_stats_t *stats, char *buf, size_t buflen);

/* Format hardware stats into packed binary payload. Returns sizeof(telemetry_binary_payload_t). */
int protocol_format_binary(const hw_stats_t *stats, telemetry_binary_payload_t *out);

/* Encrypt / Decrypt payload using asm_crypto_xor_in_place */
void protocol_encrypt(void *data, size_t len, uint64_t key);
void protocol_decrypt(void *data, size_t len, uint64_t key);

#endif /* WIN_TELEMETRY_PROTOCOL_H */
