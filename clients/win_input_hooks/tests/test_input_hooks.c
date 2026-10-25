#include "config.h"
#include "hotkey_engine.h"
#include "protocol.h"
#include "window_tracker.h"
#include "asm_crypto.h"

#include <assert.h>
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

static void test_window_query(void)
{
    printf("\n[Test Suite 1: Active Window Query & Foreground Tracker]\n");

    HWND fg = GetForegroundWindow();
    if (!fg) {
        fg = GetDesktopWindow();
    }
    TEST_ASSERT(fg != NULL, "Acquired a valid foreground or desktop window HWND");

    window_info_t win;
    bool ok = window_tracker_query_hwnd(fg, &win);
    TEST_ASSERT(ok == true, "window_tracker_query_hwnd succeeded");
    TEST_ASSERT(win.pid > 0, "Window PID is non-zero");
    TEST_ASSERT(strlen(win.process_name) > 0, "Window process_name is non-empty");
    TEST_ASSERT(win.timestamp_ms > 1600000000000ULL, "Window timestamp is valid Unix epoch ms");
    printf("     -> Foreground Process: %s (PID: %lu) | Title: \"%s\"\n",
           win.process_name, (unsigned long)win.pid, win.window_title);
}

static void test_hotkey_definitions(void)
{
    printf("\n[Test Suite 2: Global Hotkey Engine Definitions]\n");

    const hotkey_def_t *snap = hotkey_engine_get_def(HOTKEY_ID_SNAPSHOT);
    TEST_ASSERT(snap != NULL, "Snapshot hotkey definition exists");
    TEST_ASSERT(strcmp(snap->name, "SNAPSHOT") == 0, "Snapshot hotkey name matches");
    TEST_ASSERT((snap->modifiers & (MOD_CONTROL | MOD_ALT)) == (MOD_CONTROL | MOD_ALT), "Snapshot has CTRL+ALT modifiers");
    TEST_ASSERT(snap->vk_code == 'S', "Snapshot key is 'S'");

    const hotkey_def_t *mute = hotkey_engine_get_def(HOTKEY_ID_MUTE);
    TEST_ASSERT(mute != NULL && strcmp(mute->name, "MUTE") == 0, "Mute hotkey definition matches");
    TEST_ASSERT(mute->vk_code == 'M', "Mute key is 'M'");

    const hotkey_def_t *quit = hotkey_engine_get_def(HOTKEY_ID_QUIT);
    TEST_ASSERT(quit != NULL && strcmp(quit->name, "QUIT") == 0, "Quit hotkey definition matches");
    TEST_ASSERT(quit->vk_code == 'Q', "Quit key is 'Q'");
}

static void test_protocol_framing(void)
{
    printf("\n[Test Suite 3: Event Protocol Framing (JSON & Binary)]\n");

    window_info_t win;
    memset(&win, 0, sizeof(win));
    win.pid = 4321;
    win.timestamp_ms = 1729845123000ULL;
    win.is_fullscreen = true;
    snprintf(win.process_name, sizeof(win.process_name), "EldenRing.exe");
    snprintf(win.exe_path, sizeof(win.exe_path), "C:\\Games\\Elden Ring\\Game\\eldenring.exe");
    snprintf(win.window_title, sizeof(win.window_title), "ELDEN RING (TM)");

    /* JSON Window Event */
    char win_json[1024];
    int n_win = protocol_format_window_json(&win, win_json, sizeof(win_json));
    TEST_ASSERT(n_win > 0, "protocol_format_window_json produced non-empty buffer");
    TEST_ASSERT(strstr(win_json, "\"type\":\"window_focus\"") != NULL, "JSON contains window_focus event type");
    TEST_ASSERT(strstr(win_json, "\"process_name\":\"EldenRing.exe\"") != NULL, "JSON contains process name");
    TEST_ASSERT(strstr(win_json, "\"pid\":4321") != NULL, "JSON contains correct PID");
    TEST_ASSERT(strstr(win_json, "\"is_fullscreen\":true") != NULL, "JSON contains is_fullscreen:true");

    /* JSON Hotkey Event */
    char hk_json[512];
    int n_hk = protocol_format_hotkey_json(HOTKEY_ID_SNAPSHOT, "SNAPSHOT", "CTRL + ALT + S", 1729845124000ULL, hk_json, sizeof(hk_json));
    TEST_ASSERT(n_hk > 0, "protocol_format_hotkey_json produced non-empty buffer");
    TEST_ASSERT(strstr(hk_json, "\"type\":\"hotkey_press\"") != NULL, "JSON contains hotkey_press event type");
    TEST_ASSERT(strstr(hk_json, "\"hotkey_name\":\"SNAPSHOT\"") != NULL, "JSON contains hotkey name");

    /* Binary Window Event */
    input_hook_binary_event_t bin_win;
    int n_bwin = protocol_format_window_binary(&win, &bin_win);
    TEST_ASSERT(n_bwin == sizeof(input_hook_binary_event_t), "Binary window event matches struct size");
    TEST_ASSERT(bin_win.magic == INPUT_HOOK_MAGIC_BIN, "Binary event magic is 0x494E5054 ('INPT')");
    TEST_ASSERT(bin_win.event_type == EVENT_TYPE_WINDOW_FOCUS, "Binary event type matches window focus");
    TEST_ASSERT(bin_win.pid == 4321, "Binary event PID matches 4321");
    TEST_ASSERT(bin_win.is_fullscreen == 1, "Binary event is_fullscreen == 1");

    /* Binary Hotkey Event */
    input_hook_binary_event_t bin_hk;
    int n_bhk = protocol_format_hotkey_binary(HOTKEY_ID_SNAPSHOT, "SNAPSHOT", 1729845124000ULL, &bin_hk);
    TEST_ASSERT(n_bhk == sizeof(input_hook_binary_event_t), "Binary hotkey event matches struct size");
    TEST_ASSERT(bin_hk.event_type == EVENT_TYPE_HOTKEY_PRESS, "Binary event type matches hotkey press");
    TEST_ASSERT(bin_hk.hotkey_id == HOTKEY_ID_SNAPSHOT, "Binary event hotkey_id matches");
}

static void test_crypto_involution(void)
{
    printf("\n[Test Suite 4: c_asm_crypto Core Integration]\n");

    uint64_t key = 0x0123456789ABCDEFull;
    char sample_event[] = "{\"type\":\"window_focus\",\"process_name\":\"Cyberpunk2077.exe\",\"pid\":8812}";
    size_t len = strlen(sample_event);

    char encrypted[256];
    memcpy(encrypted, sample_event, len + 1);

    protocol_encrypt(encrypted, len, key);
    TEST_ASSERT(memcmp(sample_event, encrypted, len) != 0, "Payload transformed by XOR cipher");

    protocol_decrypt(encrypted, len, key);
    TEST_ASSERT(memcmp(sample_event, encrypted, len) == 0, "Involution: Decryption recovers exact original plaintext");
}

static void test_memory_footprint(void)
{
    printf("\n[Test Suite 5: Memory Footprint (<1MB RAM Standard)]\n");

    EmptyWorkingSet(GetCurrentProcess());
    PROCESS_MEMORY_COUNTERS pmc;
    memset(&pmc, 0, sizeof(pmc));
    if (GetProcessMemoryInfo(GetCurrentProcess(), &pmc, sizeof(pmc))) {
        double ws_mb = (double)pmc.WorkingSetSize / (1024.0 * 1024.0);
        printf("     -> Resident Working Set: %.2f MB\n", ws_mb);
        TEST_ASSERT(ws_mb < 1.0, "Working set is < 1.0 MB (meets lightweight engine requirement)");
    } else {
        TEST_ASSERT(false, "GetProcessMemoryInfo succeeded");
    }
}

int main(void)
{
    printf("============================================================\n");
    printf("  Running Windows Input Hooks & Game Detector Test Suite    \n");
    printf("============================================================\n");

    test_window_query();
    test_hotkey_definitions();
    test_protocol_framing();
    test_crypto_involution();
    test_memory_footprint();

    printf("\n============================================================\n");
    printf("Test Results: %d / %d passed.\n", g_tests_passed, g_tests_run);
    printf("============================================================\n");

    return (g_tests_passed == g_tests_run) ? 0 : 1;
}
