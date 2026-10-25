#ifndef WIN_INPUT_HOOKS_HOTKEY_ENGINE_H
#define WIN_INPUT_HOOKS_HOTKEY_ENGINE_H

#include <stdbool.h>
#include <stdint.h>

#ifdef _WIN32
#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>
#endif

typedef enum {
    HOTKEY_ID_SNAPSHOT = 1,
    HOTKEY_ID_MUTE     = 2,
    HOTKEY_ID_OVERLAY  = 3,
    HOTKEY_ID_QUIT     = 4
} hotkey_action_id_t;

typedef struct {
    int         id;
    const char *name;
    const char *description;
    UINT        modifiers;
    UINT        vk_code;
    const char *combination_str;
} hotkey_def_t;

typedef void (*hotkey_trigger_cb)(int id, const char *name, uint64_t timestamp_ms);

/* Initializes and registers global hotkeys via RegisterHotKey */
bool hotkey_engine_init(hotkey_trigger_cb callback);

/* Dispatches a triggered WM_HOTKEY message */
void hotkey_engine_dispatch(int id);

/* Retrieves definition for given hotkey ID */
const hotkey_def_t *hotkey_engine_get_def(int id);

/* Unregisters all global hotkeys */
void hotkey_engine_shutdown(void);

#endif /* WIN_INPUT_HOOKS_HOTKEY_ENGINE_H */
