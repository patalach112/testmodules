/*
 * Unit tests for the Binary Packet Protocol core.
 *
 * Run with `make check`. Covers CRC-32C reference vectors, hardware/
 * software agreement, framing round trips at every alignment, the
 * zero-copy builder path and every parse failure mode.
 */
#include "binary_packer/binary_pack.h"
#include "../src/bp_crc_internal.h"

#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

static int failures;

#define CHECK(cond, ...)                                          \
    do {                                                          \
        if (!(cond)) {                                            \
            ++failures;                                           \
            fprintf(stderr, "FAIL %s:%d: ", __FILE__, __LINE__); \
            fprintf(stderr, __VA_ARGS__);                         \
            fputc('\n', stderr);                                  \
        }                                                         \
    } while (0)

static uint64_t rng_state = 0x2545F4914F6CDD1Dull;

static uint64_t rng_next(void)
{
    rng_state ^= rng_state << 13;
    rng_state ^= rng_state >> 7;
    rng_state ^= rng_state << 17;
    return rng_state;
}

static void test_crc_vectors(void)
{
    /* CRC-32C published check values. */
    CHECK(bp_crc32c(NULL, 0) == 0, "empty CRC must be 0");
    CHECK(bp_crc32c("", 0) == 0, "empty string CRC must be 0");

    static const char digits[] = "123456789";
    const uint32_t crc = bp_crc32c(digits, 9);
    CHECK(crc == 0xE3069283u, "CRC-32C(\"123456789\") = %08x, want e3069283",
          crc);

    /* The software path must match the same vectors on its own. */
    const uint32_t soft =
        bp_crc32c_soft(0xFFFFFFFFu, (const uint8_t *)digits, 9) ^ 0xFFFFFFFFu;
    CHECK(soft == 0xE3069283u, "software CRC vector = %08x", soft);

#if BP_CRC_HAVE_X86
    /* Both implementations must agree byte for byte on random input. */
    enum { N = 4096 };
    uint8_t *buf = (uint8_t *)malloc(N);
    for (int i = 0; i < N; ++i)
        buf[i] = (uint8_t)rng_next();

    for (size_t len = 0; len <= N; len += 7) {
        const uint32_t hw =
            bp_crc32c_hw(0xFFFFFFFFu, buf, len) ^ 0xFFFFFFFFu;
        const uint32_t sw =
            bp_crc32c_soft(0xFFFFFFFFu, buf, len) ^ 0xFFFFFFFFu;
        CHECK(hw == sw, "hw/sw CRC differ at len=%zu (%08x vs %08x)", len,
              hw, sw);
        CHECK(bp_crc32c(buf, len) == sw, "public CRC mismatch at len=%zu",
              len);
    }
    free(buf);
    printf("crc: hardware path %s\n",
           bp_crc32c_hw_available() ? "active (SSE4.2)" : "unavailable");
#endif
}

static void test_wire_layout(void)
{
    uint8_t packet[BP_OVERHEAD + 3];
    static const uint8_t payload[3] = {0xAA, 0xBB, 0xCC};

    const size_t n = bp_pack(packet, sizeof(packet), 0x07, 0x80,
                             0x01020304u, payload, 3);
    CHECK(n == BP_OVERHEAD + 3, "packed size = %zu", n);
    CHECK(packet[0] == BP_MAGIC_0 && packet[1] == BP_MAGIC_1, "bad magic");
    CHECK(packet[2] == 0x07 && packet[3] == 0x80, "opcode/flags");
    CHECK(packet[4] == 0x01 && packet[5] == 0x02 && packet[6] == 0x03 &&
              packet[7] == 0x04,
          "sequence not in network byte order");
    CHECK(packet[8] == 0x00 && packet[9] == 0x00 && packet[10] == 0x00 &&
              packet[11] == 0x03,
          "length not in network byte order");
    CHECK(memcmp(packet + 12, payload, 3) == 0, "payload moved");

    /* CRC field must equal a fresh checksum of header + payload. */
    const uint32_t want = bp_crc32c(packet, 15);
    uint32_t got = ((uint32_t)packet[15] << 24) | ((uint32_t)packet[16] << 16) |
                   ((uint32_t)packet[17] << 8) | packet[18];
    CHECK(got == want, "wire CRC %08x != computed %08x", got, want);
}

static void test_round_trips(void)
{
    enum { MAX_LEN = 300, PAD = 16 };
    uint8_t *src_mem = (uint8_t *)malloc(MAX_LEN + PAD);
    uint8_t *pkt_mem = (uint8_t *)malloc(MAX_LEN + BP_OVERHEAD + PAD);

    for (size_t len = 0; len <= MAX_LEN; ++len) {
        for (int align = 0; align < 8; ++align) {
            uint8_t *payload = src_mem + align;
            uint8_t *packet = pkt_mem + align;
            for (size_t i = 0; i < len; ++i)
                payload[i] = (uint8_t)rng_next();

            const size_t cap = BP_OVERHEAD + len;
            const size_t n = bp_pack(packet, cap, (uint8_t)(len & 0xFF),
                                     (uint8_t)(align), 1000u + (uint32_t)len,
                                     payload, (uint32_t)len);
            CHECK(n == cap, "pack returned %zu for len %zu", n, len);

            bp_header_t header;
            const uint8_t *out_payload = NULL;
            size_t out_len = 0;
            const bp_status_t st =
                bp_parse(packet, n, &header, &out_payload, &out_len);
            CHECK(st == BP_OK, "parse status %d at len %zu", st, len);
            CHECK(header.opcode == (uint8_t)(len & 0xFF) &&
                      header.flags == (uint8_t)align &&
                      header.sequence == 1000u + (uint32_t)len &&
                      header.payload_length == len,
                  "header fields corrupted at len %zu", len);
            CHECK(out_len == len && out_payload == packet + BP_HEADER_SIZE,
                  "zero-copy payload view wrong at len %zu", len);
            CHECK(len == 0 || memcmp(out_payload, payload, len) == 0,
                  "payload mismatch at len %zu", len);

            /* Zero-copy builder: reserve the region, fill it in place,
             * then finalize; its bytes must match the one-shot packet. */
            uint8_t builder_buf[BP_OVERHEAD + MAX_LEN];
            const size_t header_bytes =
                bp_write_header(builder_buf, sizeof(builder_buf), 0x01, 0x00,
                                42u, (uint32_t)len);
            CHECK(header_bytes == BP_HEADER_SIZE,
                  "write_header failed at len %zu", len);
            if (header_bytes == BP_HEADER_SIZE) {
                memcpy(builder_buf + BP_HEADER_SIZE, payload, len);
                const size_t built = bp_finalize(builder_buf, (uint32_t)len);
                CHECK(built == cap, "finalize size %zu at len %zu", built,
                      len);
                CHECK(bp_parse(builder_buf, built, NULL, NULL, NULL) == BP_OK,
                      "zero-copy packet invalid at len %zu", len);
            }
        }
    }
    free(src_mem);
    free(pkt_mem);
}

static void test_failures(void)
{
    uint8_t good[BP_OVERHEAD + 8];
    static const uint8_t payload[8] = {1, 2, 3, 4, 5, 6, 7, 8};
    const size_t n = bp_pack(good, sizeof(good), 1, 0, 9, payload, 8);

    uint8_t scratch[64];

    /* Truncation at every boundary. */
    for (size_t cut = 0; cut < n; ++cut) {
        memcpy(scratch, good, n);
        CHECK(bp_parse(scratch, cut, NULL, NULL, NULL) != BP_OK,
              "truncated packet accepted at cut %zu", cut);
    }

    /* Bad magic. */
    memcpy(scratch, good, n);
    scratch[0] ^= 0xFF;
    CHECK(bp_parse(scratch, n, NULL, NULL, NULL) == BP_ERR_MAGIC, "magic");

    /* Declared length larger than the surrounding buffer. */
    memcpy(scratch, good, n);
    scratch[11] = (uint8_t)(scratch[11] + 16); /* claim 24 bytes payload */
    CHECK(bp_parse(scratch, n, NULL, NULL, NULL) == BP_ERR_LENGTH,
          "oversized length accepted");

    /* Payload corruption must trip the CRC. */
    memcpy(scratch, good, n);
    scratch[BP_HEADER_SIZE] ^= 0x01;
    CHECK(bp_parse(scratch, n, NULL, NULL, NULL) == BP_ERR_CRC,
          "payload corruption undetected");

    /* CRC field corruption likewise. */
    memcpy(scratch, good, n);
    scratch[n - 1] ^= 0x80;
    CHECK(bp_parse(scratch, n, NULL, NULL, NULL) == BP_ERR_CRC,
          "CRC corruption undetected");

    /* Packing into an undersized buffer is refused. */
    CHECK(bp_pack(scratch, n - 1, 1, 0, 9, payload, 8) == 0,
          "pack overran capacity");
}

int main(void)
{
    test_crc_vectors();
    test_wire_layout();
    test_round_trips();
    test_failures();

    if (failures != 0) {
        fprintf(stderr, "%d check(s) failed\n", failures);
        return 1;
    }
    puts("binary_packer: all tests passed");
    return 0;
}
