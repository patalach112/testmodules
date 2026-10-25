#include "window_tracker.h"

#include <stdio.h>
#include <string.h>

static HWINEVENTHOOK    s_hook = NULL;
static window_change_cb s_callback = NULL;
static HWND             s_last_hwnd = NULL;

static uint64_t get_unix_time_ms(void)
{
    FILETIME ft;
    GetSystemTimeAsFileTime(&ft);
    uint64_t t = (((uint64_t)ft.dwHighDateTime) << 32) | ft.dwLowDateTime;
    const uint64_t unix_epoch_offset = 116444736000000000ULL;
    if (t >= unix_epoch_offset) {
        return (t - unix_epoch_offset) / 10000ULL;
    }
    return 0;
}

static void safe_copy(char *dst, size_t dst_size, const char *src)
{
    if (!dst || dst_size == 0) return;
    if (!src) { dst[0] = '\0'; return; }
    size_t len = strlen(src);
    if (len >= dst_size) len = dst_size - 1;
    memcpy(dst, src, len);
    dst[len] = '\0';
}

bool window_tracker_query_hwnd(HWND hwnd, window_info_t *out)
{
    if (!hwnd || !out || !IsWindow(hwnd)) {
        return false;
    }

    memset(out, 0, sizeof(*out));
    out->hwnd = hwnd;
    out->timestamp_ms = get_unix_time_ms();

    /* 1. Extract Window Title */
    int len = GetWindowTextA(hwnd, out->window_title, sizeof(out->window_title));
    if (len <= 0) {
        snprintf(out->window_title, sizeof(out->window_title), "<Untitled Window>");
    }

    /* 2. Extract Process ID */
    DWORD pid = 0;
    GetWindowThreadProcessId(hwnd, &pid);
    out->pid = (uint32_t)pid;

    /* 3. Extract Executable Path and Process Name */
    if (pid != 0) {
        HANDLE hProc = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, FALSE, pid);
        if (hProc) {
            DWORD size = sizeof(out->exe_path);
            if (QueryFullProcessImageNameA(hProc, 0, out->exe_path, &size)) {
                const char *slash = strrchr(out->exe_path, '\\');
                if (slash) {
                    safe_copy(out->process_name, sizeof(out->process_name), slash + 1);
                } else {
                    safe_copy(out->process_name, sizeof(out->process_name), out->exe_path);
                }
            } else {
                snprintf(out->process_name, sizeof(out->process_name), "pid_%lu.exe", pid);
            }
            CloseHandle(hProc);
        } else {
            snprintf(out->process_name, sizeof(out->process_name), "system_pid_%lu.exe", pid);
        }
    } else {
        snprintf(out->process_name, sizeof(out->process_name), "system");
    }

    /* 4. Check Fullscreen State (matches or exceeds primary screen resolution) */
    RECT rc;
    if (GetWindowRect(hwnd, &rc)) {
        int width = rc.right - rc.left;
        int height = rc.bottom - rc.top;
        int screen_w = GetSystemMetrics(SM_CXSCREEN);
        int screen_h = GetSystemMetrics(SM_CYSCREEN);
        if (width >= screen_w && height >= screen_h) {
            out->is_fullscreen = true;
        }
    }

    return true;
}

static void CALLBACK win_event_proc(
    HWINEVENTHOOK hWinEventHook,
    DWORD event,
    HWND hwnd,
    LONG idObject,
    LONG idChild,
    DWORD idEventThread,
    DWORD dwmsEventTime)
{
    (void)hWinEventHook;
    (void)event;
    (void)idObject;
    (void)idChild;
    (void)idEventThread;
    (void)dwmsEventTime;

    if (!hwnd || hwnd == s_last_hwnd || !IsWindow(hwnd)) {
        return;
    }

    s_last_hwnd = hwnd;

    if (s_callback) {
        window_info_t info;
        if (window_tracker_query_hwnd(hwnd, &info)) {
            s_callback(&info);
        }
    }
}

bool window_tracker_init(window_change_cb callback)
{
    s_callback = callback;

    /*
     * SetWinEventHook with WINEVENT_OUTOFCONTEXT:
     * Asynchronously receives EVENT_SYSTEM_FOREGROUND notifications
     * on the calling thread's message queue without any DLL injection
     * or polling overhead.
     */
    s_hook = SetWinEventHook(
        EVENT_SYSTEM_FOREGROUND,
        EVENT_SYSTEM_FOREGROUND,
        NULL,
        win_event_proc,
        0,
        0,
        WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS
    );

    if (!s_hook) {
        return false;
    }

    /* Trigger notification for currently active window upon launch */
    HWND foreground = GetForegroundWindow();
    if (foreground && IsWindow(foreground)) {
        s_last_hwnd = foreground;
        if (s_callback) {
            window_info_t info;
            if (window_tracker_query_hwnd(foreground, &info)) {
                s_callback(&info);
            }
        }
    }

    return true;
}

void window_tracker_shutdown(void)
{
    if (s_hook) {
        UnhookWinEvent(s_hook);
        s_hook = NULL;
    }
    s_callback = NULL;
    s_last_hwnd = NULL;
}
