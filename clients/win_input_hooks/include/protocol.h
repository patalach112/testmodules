#ifndef WIN_INPUT_HOOKS_PROTOCOL_H
#define WIN_INPUT_HOOKS_PROTOCOL_H

#include "hotkey_engine.h"
#include "window_tracker.h"

#include <stddef.h>
#include <stdint.h>

#define INPUT_HOOK_MAGIC_BIN 0x494E5054u /* 'INPT' */
#define INPUT_HOOK_VERSION   1

typedef enum {
    EVENT_TYPE_WINDOW_FOCUS = 1,
    EVENT_TYPE_HOTKEY_PRESS = 2
} input_event_type_t;

#pragma pack(push, 1)
typedef struct {
    uint32_t magic;           /* 0x494E5054 ('INPT') */
    uint16_t version;         /* Protocol version: 1 */
    uint16_t event_type;      /* 1: window_focus, 2: hotkey_press */
    uint64_t timestamp_ms;    /* Unix epoch timestamp in ms */
    uint32_t pid;             /* Active window PID */
    uint8_t  is_fullscreen;   /* 1 if fullscreen window, 0 otherwise */
    uint8_t  hotkey_id;       /* Hotkey identifier (or 0 for window event) */
    char     process_name[32];/* Process executable name */
    char     event_label[64]; /* Window title or hotkey combination string */
} input_hook_binary_event_t;
#pragma pack(pop)

/* Formats window switch into JSON buffer */
int protocol_format_window_json(const window_info_t *win, char *buf, size_t buflen);

/* Formats hotkey press into JSON buffer */
int protocol_format_hotkey_json(int hotkey_id, const char *name, const char *combination, uint64_t timestamp_ms, char *buf, size_t buflen);

/* Formats window switch into packed binary event */
int protocol_format_window_binary(const window_info_t *win, input_hook_binary_event_t *out);

/* Formats hotkey press into packed binary event */
int protocol_format_hotkey_binary(int hotkey_id, const char *name, uint64_t timestamp_ms, input_hook_binary_event_t *out);

/* In-place XOR encryption / decryption with c_asm_crypto */
void protocol_encrypt(void *data, size_t len, uint64_t key);
void protocol_decrypt(void *data, size_t len, uint64_t key);

#endif /* WIN_INPUT_HOOKS_PROTOCOL_H */
