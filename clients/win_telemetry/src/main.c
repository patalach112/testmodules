#include "config.h"
#include "hw_stats.h"
#include "protocol.h"
#include "service.h"
#include "ws_client.h"

#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#ifdef _WIN32
#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>
#endif

static telemetry_config_t g_cfg;
static ws_client_t        g_ws;

static void run_daemon_loop(void)
{
    printf("[win_telemetry] Starting daemon loop (interval=%ums, key=0x%016llX, format=%s)...\n",
           g_cfg.interval_ms,
           (unsigned long long)g_cfg.crypto_key,
           g_cfg.binary_mode ? "BINARY" : "JSON");

    hw_stats_init();
    ws_client_init(&g_ws);

    int64_t frames_sent = 0;
    char payload_buf[4096];
    char original_copy[4096];
    char echo_buf[4096];

    while (g_running) {
        uint64_t tick_start_ms = GetTickCount64();

        /* Ensure WebSocket connection */
        if (!ws_client_is_connected(&g_ws)) {
            printf("[ws] Connecting to gateway ws://%s:%u/ ...\n", g_cfg.host, g_cfg.port);
            if (ws_client_connect(&g_ws, g_cfg.host, g_cfg.port)) {
                printf("[ws] Connected successfully! Upgraded to RFC 6455 WebSocket.\n");
            } else {
                printf("[ws] Failed to connect to gateway ws://%s:%u/. Will retry in %ums.\n",
                       g_cfg.host, g_cfg.port, g_cfg.interval_ms);
            }
        }

        /* 1. Sample low-level hardware stats */
        hw_stats_t stats;
        hw_stats_sample(&stats);

        size_t payload_len = 0;

        /* 2. Format telemetry payload */
        if (g_cfg.binary_mode) {
            telemetry_binary_payload_t bin_payload;
            int n = protocol_format_binary(&stats, &bin_payload);
            if (n > 0 && (size_t)n <= sizeof(payload_buf)) {
                memcpy(payload_buf, &bin_payload, (size_t)n);
                payload_len = (size_t)n;
            }
        } else {
            int n = protocol_format_json(&stats, payload_buf, sizeof(payload_buf));
            if (n > 0) {
                payload_len = (size_t)n;
            }
        }

        if (payload_len > 0) {
            /* Keep original copy to verify gateway roundtrip echo */
            memcpy(original_copy, payload_buf, payload_len);

            /* 3. Encrypt payload with asm_crypto core */
            protocol_encrypt(payload_buf, payload_len, g_cfg.crypto_key);

            /* 4. Stream frame via WebSocket */
            bool send_ok = false;
            if (ws_client_is_connected(&g_ws)) {
                send_ok = ws_client_send(&g_ws, payload_buf, payload_len, true /* binary frame for raw bytes */);
            }

            /* Log status */
            double ram_used_mb = (double)stats.ram_used_bytes / (1024.0 * 1024.0);
            double ram_total_mb = (double)stats.ram_total_bytes / (1024.0 * 1024.0);
            double daemon_ws_mb = (double)stats.process_ws_bytes / (1024.0 * 1024.0);

            printf("[frame #%lld] CPU: %5.1f%% | RAM: %.0f/%.0f MB (%4.1f%%) | Uptime: %llus | "
                   "TSC: %llu (Δ%llu, ovh: %llu cyc) | Daemon RSS: %.2f MB%s\n",
                   (long long)(frames_sent + 1),
                   stats.cpu_usage_pct,
                   ram_used_mb,
                   ram_total_mb,
                   stats.ram_usage_pct,
                   (unsigned long long)(stats.uptime_ms / 1000),
                   (unsigned long long)stats.tsc_cycles,
                   (unsigned long long)stats.tsc_delta,
                   (unsigned long long)stats.tsc_overhead,
                   daemon_ws_mb,
                   send_ok ? " [SENT]" : (ws_client_is_connected(&g_ws) ? " [SEND_ERR]" : " [OFFLINE]"));

            /* 5. Receive & verify gateway echo (gateway applies asm_crypto XOR to decrypt) */
            if (send_ok) {
                size_t echo_len = 0;
                int r = ws_client_poll_read(&g_ws, echo_buf, sizeof(echo_buf), &echo_len, 200);
                if (r > 0) {
                    /*
                     * cpp_gateway applies asm_crypto_xor with the same key, which restores
                     * the original unencrypted plaintext!
                     */
                    if (echo_len == payload_len && memcmp(echo_buf, original_copy, payload_len) == 0) {
                        printf("   └─► Gateway Echo Verified! Roundtrip plaintext match (%zu bytes).\n", echo_len);
                        if (g_cfg.verbose && !g_cfg.binary_mode) {
                            echo_buf[(echo_len < sizeof(echo_buf)) ? echo_len : sizeof(echo_buf) - 1] = '\0';
                            printf("       Echo payload: %s\n", echo_buf);
                        }
                    } else {
                        printf("   └─► Gateway Echo received (%zu bytes) - signature mismatch!\n", echo_len);
                    }
                }
            }

            frames_sent++;
            if (g_cfg.max_count > 0 && frames_sent >= g_cfg.max_count) {
                printf("[win_telemetry] Reached target frame count (%lld). Exiting.\n", (long long)frames_sent);
                break;
            }
        }

        /* Sleep compensation */
        uint64_t elapsed_ms = GetTickCount64() - tick_start_ms;
        if (elapsed_ms < g_cfg.interval_ms && g_running) {
            DWORD sleep_dur = (DWORD)(g_cfg.interval_ms - elapsed_ms);
            /* Sleep in small increments to respond immediately to Ctrl+C */
            while (sleep_dur > 0 && g_running) {
                DWORD chunk = (sleep_dur > 50) ? 50 : sleep_dur;
                Sleep(chunk);
                sleep_dur -= chunk;
            }
        }
    }

    if (ws_client_is_connected(&g_ws)) {
        ws_client_close(&g_ws);
    }
    ws_client_global_cleanup();
    printf("[win_telemetry] Daemon stopped cleanly.\n");
}

int main(int argc, char *argv[])
{
    config_init_defaults(&g_cfg);

    int parse_res = config_parse_args(&g_cfg, argc, argv);
    if (parse_res != 0) {
        return (parse_res > 0) ? 0 : 1;
    }

    /* Windows Service Installation / Removal */
    if (g_cfg.install_service) {
        char binary_path[MAX_PATH];
        GetModuleFileNameA(NULL, binary_path, sizeof(binary_path));
        return service_install(binary_path) ? 0 : 1;
    }

    if (g_cfg.uninstall_service) {
        return service_uninstall() ? 0 : 1;
    }

    /* Windows Service execution mode */
    if (g_cfg.service_mode) {
        service_set_worker(run_daemon_loop);
        return service_run_as_service();
    }

    /* Interactive console daemon execution mode */
    service_setup_console_handler();
    printf("===================================================================\n");
    printf("  Windows Telemetry & System Monitor Daemon (Native C11 / x86_64)  \n");
    printf("===================================================================\n");
    printf("PID: %lu | Gateway: ws://%s:%u/ | Key: 0x%016llX\n\n",
           GetCurrentProcessId(), g_cfg.host, g_cfg.port, (unsigned long long)g_cfg.crypto_key);

    run_daemon_loop();
    return 0;
}
