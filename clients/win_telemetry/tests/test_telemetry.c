#include "config.h"
#include "hw_stats.h"
#include "protocol.h"
#include "asm_crypto.h"
#include "ws_client.h"

#include <assert.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

static int g_tests_run = 0;
static int g_tests_passed = 0;

#define TEST_ASSERT(cond, msg) do { \
    g_tests_run++; \
    if (cond) { \
        printf("  [PASS] %s\n", msg); \
        g_tests_passed++; \
    } else { \
        printf("  [FAIL] %s (%s:%d)\n", msg, __FILE__, __LINE__); \
    } \
} while (0)

static void test_x86_64_asm_rdtsc(void)
{
    printf("\n[Test Suite 1: Low-Level Hardware Stats & x86_64 ASM]\n");

    uint64_t t0 = hw_rdtsc_serialized();
    uint64_t t1 = hw_rdtsc_serialized();
    TEST_ASSERT(t0 > 0, "hw_rdtsc_serialized returns non-zero cycle count");
    TEST_ASSERT(t1 >= t0, "hw_rdtsc_serialized monotonic advance (t1 >= t0)");

    uint64_t raw0 = hw_rdtsc_raw();
    uint64_t raw1 = hw_rdtsc_raw();
    TEST_ASSERT(raw0 > 0, "hw_rdtsc_raw returns non-zero cycle count");
    TEST_ASSERT(raw1 >= raw0, "hw_rdtsc_raw monotonic advance (raw1 >= raw0)");

    uint64_t overhead = hw_measure_timing_precision();
    TEST_ASSERT(overhead > 0 && overhead < 10000000ULL, "hw_measure_timing_precision returns valid cycle overhead");
    printf("     -> Measured CPUID+RDTSC timing overhead: %llu cycles\n", (unsigned long long)overhead);
}

static void test_windows_apis(void)
{
    printf("\n[Test Suite 2: Native Windows Metrics APIs]\n");

    hw_stats_init();
    Sleep(50); /* Sleep 50ms to allow CPU delta measurement */

    hw_stats_t stats;
    hw_stats_sample(&stats);

    TEST_ASSERT(stats.ram_total_bytes > 0, "GlobalMemoryStatusEx: Total RAM > 0");
    TEST_ASSERT(stats.ram_avail_bytes > 0, "GlobalMemoryStatusEx: Avail RAM > 0");
    TEST_ASSERT(stats.ram_total_bytes >= stats.ram_avail_bytes, "GlobalMemoryStatusEx: Total >= Avail");
    TEST_ASSERT(stats.uptime_ms > 0, "GetTickCount64: System uptime > 0");
    TEST_ASSERT(stats.timestamp_unix_ms > 1600000000000ULL, "Unix epoch timestamp is sane (> year 2020)");
    TEST_ASSERT(stats.process_pid > 0, "Process PID is non-zero");
    TEST_ASSERT(stats.process_ws_bytes > 0, "Process WorkingSetSize > 0");

    double ws_mb = (double)stats.process_ws_bytes / (1024.0 * 1024.0);
    printf("     -> Current resident working set: %.2f MB\n", ws_mb);
    TEST_ASSERT(ws_mb < 5.0, "Working set is < 5.0 MB (meets lightweight daemon requirement)");
}

static void test_crypto_integration(void)
{
    printf("\n[Test Suite 3: c_asm_crypto Core Integration]\n");

    const char *ver = asm_crypto_version();
    TEST_ASSERT(ver != NULL && strlen(ver) > 0, "asm_crypto_version() returned valid string");
    printf("     -> asm_crypto version: %s\n", ver);

    uint64_t key = 0x0123456789ABCDEFull;
    char sample_text[] = "{\"cpu\":{\"usage_pct\":12.5},\"memory\":{\"ram_used\":4096}}";
    size_t len = strlen(sample_text);

    char encrypted[128];
    memcpy(encrypted, sample_text, len + 1);

    protocol_encrypt(encrypted, len, key);
    TEST_ASSERT(memcmp(sample_text, encrypted, len) != 0, "Payload successfully transformed by XOR cipher");

    /* XOR cipher involution: applying key twice recovers original text */
    protocol_decrypt(encrypted, len, key);
    TEST_ASSERT(memcmp(sample_text, encrypted, len) == 0, "Involution check: Decrypting restored exact original payload");
}

static void test_protocol_framing(void)
{
    printf("\n[Test Suite 4: Protocol Framing (JSON & Binary)]\n");

    hw_stats_t stats;
    hw_stats_sample(&stats);

    /* Test JSON */
    char json_buf[1024];
    int json_len = protocol_format_json(&stats, json_buf, sizeof(json_buf));
    TEST_ASSERT(json_len > 0, "protocol_format_json produced non-empty output");
    TEST_ASSERT(json_buf[0] == '{' && json_buf[json_len - 1] == '}', "protocol_format_json produced valid JSON enclosure");
    TEST_ASSERT(strstr(json_buf, "\"hostname\":") != NULL, "JSON contains 'hostname' field");
    TEST_ASSERT(strstr(json_buf, "\"cpu\":") != NULL, "JSON contains 'cpu' field");
    TEST_ASSERT(strstr(json_buf, "\"memory\":") != NULL, "JSON contains 'memory' field");
    TEST_ASSERT(strstr(json_buf, "\"tsc_cycles\":") != NULL, "JSON contains 'tsc_cycles' field");

    /* Test Binary */
    telemetry_binary_payload_t bin_payload;
    int bin_len = protocol_format_binary(&stats, &bin_payload);
    TEST_ASSERT(bin_len == (int)sizeof(telemetry_binary_payload_t), "protocol_format_binary returned exact struct size");
    TEST_ASSERT(bin_payload.magic == TELEMETRY_BINARY_MAGIC, "Binary payload magic matches 0x54454C4D ('TELM')");
    TEST_ASSERT(bin_payload.version == TELEMETRY_VERSION, "Binary payload version matches 1");
    TEST_ASSERT(bin_payload.ram_total_bytes > 0, "Binary payload contains valid RAM size");
}

int main(void)
{
    printf("============================================================\n");
    printf("  Running Native Windows Telemetry Test Suite               \n");
    printf("============================================================\n");

    test_x86_64_asm_rdtsc();
    test_windows_apis();
    test_crypto_integration();
    test_protocol_framing();

    printf("\n============================================================\n");
    printf("Test Results: %d / %d passed.\n", g_tests_passed, g_tests_run);
    printf("============================================================\n");

    return (g_tests_passed == g_tests_run) ? 0 : 1;
}
