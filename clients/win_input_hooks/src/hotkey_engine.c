#include "hotkey_engine.h"

#include <stdio.h>

#ifndef MOD_NOREPEAT
#define MOD_NOREPEAT 0x4000
#endif

static hotkey_def_t s_hotkeys[] = {
    { HOTKEY_ID_SNAPSHOT, "SNAPSHOT", "Trigger instant in-game snapshot", MOD_CONTROL | MOD_ALT, 'S', "CTRL + ALT + S" },
    { HOTKEY_ID_MUTE,     "MUTE",     "Toggle mute / telemetry signal",  MOD_CONTROL | MOD_ALT, 'M', "CTRL + ALT + M" },
    { HOTKEY_ID_OVERLAY,  "OVERLAY",  "Toggle in-game status overlay",   MOD_CONTROL | MOD_ALT, 'O', "CTRL + ALT + O" },
    { HOTKEY_ID_QUIT,     "QUIT",     "Request clean daemon shutdown",    MOD_CONTROL | MOD_ALT, 'Q', "CTRL + ALT + Q" },
};

#define HOTKEY_COUNT (sizeof(s_hotkeys) / sizeof(s_hotkeys[0]))

static hotkey_trigger_cb s_callback = NULL;

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

const hotkey_def_t *hotkey_engine_get_def(int id)
{
    for (size_t i = 0; i < HOTKEY_COUNT; ++i) {
        if (s_hotkeys[i].id == id) {
            return &s_hotkeys[i];
        }
    }
    return NULL;
}

bool hotkey_engine_init(hotkey_trigger_cb callback)
{
    s_callback = callback;
    int success_count = 0;

    for (size_t i = 0; i < HOTKEY_COUNT; ++i) {
        hotkey_def_t *hk = &s_hotkeys[i];

        /* Register system-wide hotkey. Try with MOD_NOREPEAT first */
        BOOL ok = RegisterHotKey(NULL, hk->id, hk->modifiers | MOD_NOREPEAT, hk->vk_code);
        if (!ok) {
            ok = RegisterHotKey(NULL, hk->id, hk->modifiers, hk->vk_code);
        }

        if (ok) {
            success_count++;
        } else {
            fprintf(stderr, "[hotkey] Failed to register %s (%s). Might conflict with another app.\n",
                    hk->name, hk->combination_str);
        }
    }

    return (success_count > 0);
}

void hotkey_engine_dispatch(int id)
{
    const hotkey_def_t *def = hotkey_engine_get_def(id);
    if (def && s_callback) {
        s_callback(id, def->name, get_unix_time_ms());
    }
}

void hotkey_engine_shutdown(void)
{
    for (size_t i = 0; i < HOTKEY_COUNT; ++i) {
        UnregisterHotKey(NULL, s_hotkeys[i].id);
    }
    s_callback = NULL;
}
