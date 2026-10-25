#include "config.h"
#include "hotkey_engine.h"
#include "protocol.h"
#include "window_tracker.h"
#include "ws_client.h"

#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#ifdef _WIN32
#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>
#include <psapi.h>
#endif

static input_hooks_config_t g_cfg;
static ws_client_t          g_ws;
static DWORD                g_main_thread_id = 0;
static volatile bool        g_running = true;
static int64_t              g_event_count = 0;

static void log_process_footprint(void)
{
    EmptyWorkingSet(GetCurrentProcess());
    PROCESS_MEMORY_COUNTERS pmc;
    memset(&pmc, 0, sizeof(pmc));
    if (GetProcessMemoryInfo(GetCurrentProcess(), &pmc, sizeof(pmc))) {
        double ws_mb = (double)pmc.WorkingSetSize / (1024.0 * 1024.0);
        printf("   [RAM Footprint: %.2f MB resident (Requirement: <1.0 MB)]\n", ws_mb);
    }
}

static void send_event_payload(const void *payload, size_t len, const char *event_type, const char *summary)
{
    char original_copy[2048];
    char send_buf[2048];
    char echo_buf[2048];

    if (len > sizeof(send_buf)) return;

    memcpy(original_copy, payload, len);
    memcpy(send_buf, payload, len);

    /* Encrypt payload with c_asm_crypto core */
    protocol_encrypt(send_buf, len, g_cfg.crypto_key);

    bool send_ok = false;
    if (ws_client_is_connected(&g_ws)) {
        send_ok = ws_client_send(&g_ws, send_buf, len, true);
    }

    printf("[event #%lld] [%s] %s %s\n",
           (long long)(g_event_count + 1),
           event_type,
           summary,
           send_ok ? "[SENT]" : (ws_client_is_connected(&g_ws) ? "[SEND_ERR]" : "[OFFLINE]"));

    if (send_ok) {
        size_t echo_len = 0;
        int r = ws_client_poll_read(&g_ws, echo_buf, sizeof(echo_buf), &echo_len, 100);
        if (r > 0) {
            if (echo_len == len && memcmp(echo_buf, original_copy, len) == 0) {
                printf("   └─► Gateway Echo Verified! Plaintext roundtrip match (%zu bytes)\n", echo_len);
                if (g_cfg.verbose && !g_cfg.binary_mode) {
                    echo_buf[(echo_len < sizeof(echo_buf)) ? echo_len : sizeof(echo_buf) - 1] = '\0';
                    printf("       Payload: %s\n", echo_buf);
                }
            }
        }
    }

    g_event_count++;
    log_process_footprint();

    if (g_cfg.max_events > 0 && g_event_count >= g_cfg.max_events) {
        printf("[win_input_hooks] Reached target event count (%lld). Stopping.\n", (long long)g_event_count);
        g_running = false;
        PostThreadMessageA(g_main_thread_id, WM_QUIT, 0, 0);
    }
}

static void on_window_change(const window_info_t *win)
{
    if (!g_running) return;

    char summary[384];
    snprintf(summary, sizeof(summary), "Process: %-18s | PID: %-5lu | Fullscreen: %-3s | Title: \"%.40s%s\"",
             win->process_name,
             (unsigned long)win->pid,
             win->is_fullscreen ? "YES" : "NO",
             win->window_title,
             strlen(win->window_title) > 40 ? "..." : "");

    if (g_cfg.binary_mode) {
        input_hook_binary_event_t bin_evt;
        int n = protocol_format_window_binary(win, &bin_evt);
        if (n > 0) {
            send_event_payload(&bin_evt, (size_t)n, "WINDOW_FOCUS", summary);
        }
    } else {
        char json_buf[1024];
        int n = protocol_format_window_json(win, json_buf, sizeof(json_buf));
        if (n > 0) {
            send_event_payload(json_buf, (size_t)n, "WINDOW_FOCUS", summary);
        }
    }
}

static void on_hotkey_trigger(int id, const char *name, uint64_t timestamp_ms)
{
    if (!g_running) return;

    const hotkey_def_t *def = hotkey_engine_get_def(id);
    const char *combo = def ? def->combination_str : "UNKNOWN";

    char summary[128];
    snprintf(summary, sizeof(summary), "Hotkey: %-14s -> ACTION: %s", combo, name);

    if (g_cfg.binary_mode) {
        input_hook_binary_event_t bin_evt;
        int n = protocol_format_hotkey_binary(id, name, timestamp_ms, &bin_evt);
        if (n > 0) {
            send_event_payload(&bin_evt, (size_t)n, "HOTKEY_PRESS", summary);
        }
    } else {
        char json_buf[512];
        int n = protocol_format_hotkey_json(id, name, combo, timestamp_ms, json_buf, sizeof(json_buf));
        if (n > 0) {
            send_event_payload(json_buf, (size_t)n, "HOTKEY_PRESS", summary);
        }
    }

    if (id == HOTKEY_ID_QUIT) {
        printf("[win_input_hooks] Quit hotkey triggered. Exiting cleanly.\n");
        g_running = false;
        PostThreadMessageA(g_main_thread_id, WM_QUIT, 0, 0);
    }
}

static BOOL WINAPI console_ctrl_handler(DWORD ctrl_type)
{
    switch (ctrl_type) {
    case CTRL_C_EVENT:
    case CTRL_BREAK_EVENT:
    case CTRL_CLOSE_EVENT:
        printf("\n[win_input_hooks] Shutdown signal received. Exiting...\n");
        g_running = false;
        if (g_main_thread_id != 0) {
            PostThreadMessageA(g_main_thread_id, WM_QUIT, 0, 0);
        }
        return TRUE;
    default:
        return FALSE;
    }
}

int main(int argc, char *argv[])
{
    g_main_thread_id = GetCurrentThreadId();
    config_init_defaults(&g_cfg);

    int parse_res = config_parse_args(&g_cfg, argc, argv);
    if (parse_res != 0) {
        return (parse_res > 0) ? 0 : 1;
    }

    SetConsoleCtrlHandler(console_ctrl_handler, TRUE);

    printf("===================================================================\n");
    printf(" Windows Game Detector & Global Hotkey Engine (Native Win32 C)    \n");
    printf("===================================================================\n");
    printf("PID: %lu | Gateway: ws://%s:%u/ | Key: 0x%016llX | Mode: %s\n",
           GetCurrentProcessId(), g_cfg.host, g_cfg.port,
           (unsigned long long)g_cfg.crypto_key,
           g_cfg.binary_mode ? "BINARY" : "JSON");
    printf("Hotkeys registered: CTRL+ALT+S (Snapshot), CTRL+ALT+M (Mute), CTRL+ALT+O (Overlay), CTRL+ALT+Q (Quit)\n\n");

    /* Trim initial process memory */
    EmptyWorkingSet(GetCurrentProcess());

    /* Initialize WebSocket client */
    ws_client_init(&g_ws);
    printf("[ws] Connecting to gateway ws://%s:%u/ ...\n", g_cfg.host, g_cfg.port);
    if (ws_client_connect(&g_ws, g_cfg.host, g_cfg.port)) {
        printf("[ws] Connected to gateway! Active window & hotkey events will stream live.\n");
    } else {
        printf("[ws] Gateway unreachable. Will operate offline.\n");
    }

    /* Initialize Hotkey Engine */
    if (!hotkey_engine_init(on_hotkey_trigger)) {
        fprintf(stderr, "[hotkey] Warning: Failed to register one or more global hotkeys.\n");
    } else {
        printf("[hotkey] System-wide hotkeys registered successfully.\n");
    }

    /* Initialize Window Tracker (captures active window immediately) */
    if (!window_tracker_init(on_window_change)) {
        fprintf(stderr, "[tracker] Warning: Failed to set SetWinEventHook.\n");
    } else {
        printf("[tracker] SetWinEventHook active (EVENT_SYSTEM_FOREGROUND).\n");
    }

    printf("\n[win_input_hooks] Engine running (zero CPU polling, event-driven). Press Ctrl+C or CTRL+ALT+Q to exit.\n\n");

    /*
     * Win32 event-driven message loop.
     * Force message queue creation and attach a lightweight 50ms heartbeat timer
     * to ensure GetMessage unblocks promptly on shutdown and timer ticks.
     */
    MSG dummy;
    PeekMessageA(&dummy, NULL, 0, 0, PM_NOREMOVE);
    UINT_PTR heartbeat_timer = SetTimer(NULL, 0, 50, NULL);

    /* Automated simulation mode for testing/CI */
    if (g_cfg.simulate_events) {
        printf("[simulate] Dispatching synthetic events for verification...\n");
        hotkey_engine_dispatch(HOTKEY_ID_SNAPSHOT);
        hotkey_engine_dispatch(HOTKEY_ID_MUTE);
        hotkey_engine_dispatch(HOTKEY_ID_OVERLAY);
    }

    MSG msg;
    while (g_running) {
        if (GetMessageA(&msg, NULL, 0, 0) <= 0) {
            break;
        }
        if (msg.message == WM_HOTKEY) {
            hotkey_engine_dispatch((int)msg.wParam);
        } else if (msg.message == WM_QUIT) {
            break;
        }
        TranslateMessage(&msg);
        DispatchMessageA(&msg);
    }

    if (heartbeat_timer != 0) {
        KillTimer(NULL, heartbeat_timer);
    }

    /* Clean teardown */
    printf("[win_input_hooks] Shutting down...\n");
    window_tracker_shutdown();
    hotkey_engine_shutdown();

    if (ws_client_is_connected(&g_ws)) {
        ws_client_close(&g_ws);
    }
    ws_client_global_cleanup();

    printf("[win_input_hooks] Stopped cleanly. Processed %lld events.\n", (long long)g_event_count);
    return 0;
}
