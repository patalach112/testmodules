#ifndef WIN_INPUT_HOOKS_WINDOW_TRACKER_H
#define WIN_INPUT_HOOKS_WINDOW_TRACKER_H

#include <stdbool.h>
#include <stdint.h>

#ifdef _WIN32
#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>
#endif

typedef struct {
    HWND     hwnd;
    uint32_t pid;
    char     process_name[64];
    char     exe_path[MAX_PATH];
    char     window_title[256];
    uint64_t timestamp_ms;
    bool     is_fullscreen;
} window_info_t;

typedef void (*window_change_cb)(const window_info_t *info);

/* Initializes SetWinEventHook and immediately captures the active foreground window */
bool window_tracker_init(window_change_cb callback);

/* Queries window details for an arbitrary HWND */
bool window_tracker_query_hwnd(HWND hwnd, window_info_t *out);

/* Removes the WinEvent hook cleanly */
void window_tracker_shutdown(void);

#endif /* WIN_INPUT_HOOKS_WINDOW_TRACKER_H */
