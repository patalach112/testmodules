#include "protocol.h"
#include "asm_crypto.h"

#include <stdio.h>
#include <string.h>

int protocol_format_json(const hw_stats_t *stats, char *buf, size_t buflen)
{
    if (!stats || !buf || buflen == 0) {
        return -1;
    }

    int written = snprintf(
        buf, buflen,
        "{"
        "\"hostname\":\"%s\","
        "\"os\":\"Windows\","
        "\"timestamp_ms\":%llu,"
        "\"uptime_ms\":%llu,"
        "\"cpu\":{"
        "\"usage_pct\":%.2f,"
        "\"tsc_cycles\":%llu,"
        "\"tsc_delta\":%llu,"
        "\"tsc_overhead_cycles\":%llu"
        "},"
        "\"memory\":{"
        "\"total_bytes\":%llu,"
        "\"avail_bytes\":%llu,"
        "\"used_bytes\":%llu,"
        "\"usage_pct\":%.2f"
        "},"
        "\"process\":{"
        "\"pid\":%lu,"
        "\"ws_bytes\":%llu"
        "}"
        "}",
        stats->hostname,
        (unsigned long long)stats->timestamp_unix_ms,
        (unsigned long long)stats->uptime_ms,
        stats->cpu_usage_pct,
        (unsigned long long)stats->tsc_cycles,
        (unsigned long long)stats->tsc_delta,
        (unsigned long long)stats->tsc_overhead,
        (unsigned long long)stats->ram_total_bytes,
        (unsigned long long)stats->ram_avail_bytes,
        (unsigned long long)stats->ram_used_bytes,
        stats->ram_usage_pct,
        (unsigned long)stats->process_pid,
        (unsigned long long)stats->process_ws_bytes
    );

    if (written < 0 || (size_t)written >= buflen) {
        return -1;
    }

    return written;
}

int protocol_format_binary(const hw_stats_t *stats, telemetry_binary_payload_t *out)
{
    if (!stats || !out) {
        return -1;
    }

    memset(out, 0, sizeof(*out));
    out->magic = TELEMETRY_BINARY_MAGIC;
    out->version = TELEMETRY_VERSION;
    out->flags = 0x01; /* Encrypted flag */
    out->timestamp_ms = stats->timestamp_unix_ms;
    out->uptime_ms = stats->uptime_ms;
    out->tsc_cycles = stats->tsc_cycles;
    out->tsc_delta = stats->tsc_delta;
    out->tsc_overhead = (uint32_t)stats->tsc_overhead;
    out->cpu_usage_pct = (float)stats->cpu_usage_pct;
    out->ram_total_bytes = stats->ram_total_bytes;
    out->ram_avail_bytes = stats->ram_avail_bytes;
    out->ram_used_bytes = stats->ram_used_bytes;
    out->ram_usage_pct = (float)stats->ram_usage_pct;
    out->process_pid = stats->process_pid;
    out->process_ws_bytes = stats->process_ws_bytes;
    snprintf(out->hostname, sizeof(out->hostname), "%s", stats->hostname);

    return (int)sizeof(telemetry_binary_payload_t);
}

void protocol_encrypt(void *data, size_t len, uint64_t key)
{
    asm_crypto_xor_in_place(data, len, key);
}

void protocol_decrypt(void *data, size_t len, uint64_t key)
{
    asm_crypto_xor_in_place(data, len, key);
}
