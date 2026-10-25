#include "protocol.h"
#include "asm_crypto.h"

#include <stdio.h>
#include <string.h>

static void escape_json_str(const char *src, char *dst, size_t dst_len)
{
    if (!src || !dst || dst_len == 0) return;
    size_t d = 0;
    for (size_t s = 0; src[s] != '\0' && d + 2 < dst_len; ++s) {
        char c = src[s];
        if (c == '\\') {
            dst[d++] = '\\';
            dst[d++] = '\\';
        } else if (c == '"') {
            dst[d++] = '\\';
            dst[d++] = '"';
        } else if ((unsigned char)c >= 32) {
            dst[d++] = c;
        }
    }
    dst[d] = '\0';
}

int protocol_format_window_json(const window_info_t *win, char *buf, size_t buflen)
{
    if (!win || !buf || buflen == 0) return -1;

    char escaped_title[512];
    char escaped_path[MAX_PATH * 2];
    char escaped_proc[128];

    escape_json_str(win->window_title, escaped_title, sizeof(escaped_title));
    escape_json_str(win->exe_path, escaped_path, sizeof(escaped_path));
    escape_json_str(win->process_name, escaped_proc, sizeof(escaped_proc));

    int written = snprintf(
        buf, buflen,
        "{"
        "\"type\":\"window_focus\","
        "\"timestamp_ms\":%llu,"
        "\"pid\":%lu,"
        "\"process_name\":\"%s\","
        "\"exe_path\":\"%s\","
        "\"window_title\":\"%s\","
        "\"is_fullscreen\":%s"
        "}",
        (unsigned long long)win->timestamp_ms,
        (unsigned long)win->pid,
        escaped_proc,
        escaped_path,
        escaped_title,
        win->is_fullscreen ? "true" : "false"
    );

    if (written < 0 || (size_t)written >= buflen) {
        return -1;
    }
    return written;
}

int protocol_format_hotkey_json(int hotkey_id, const char *name, const char *combination, uint64_t timestamp_ms, char *buf, size_t buflen)
{
    if (!buf || buflen == 0) return -1;

    int written = snprintf(
        buf, buflen,
        "{"
        "\"type\":\"hotkey_press\","
        "\"timestamp_ms\":%llu,"
        "\"hotkey_id\":%d,"
        "\"hotkey_name\":\"%s\","
        "\"combination\":\"%s\""
        "}",
        (unsigned long long)timestamp_ms,
        hotkey_id,
        name ? name : "UNKNOWN",
        combination ? combination : ""
    );

    if (written < 0 || (size_t)written >= buflen) {
        return -1;
    }
    return written;
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

int protocol_format_window_binary(const window_info_t *win, input_hook_binary_event_t *out)
{
    if (!win || !out) return -1;

    memset(out, 0, sizeof(*out));
    out->magic = INPUT_HOOK_MAGIC_BIN;
    out->version = INPUT_HOOK_VERSION;
    out->event_type = EVENT_TYPE_WINDOW_FOCUS;
    out->timestamp_ms = win->timestamp_ms;
    out->pid = win->pid;
    out->is_fullscreen = win->is_fullscreen ? 1 : 0;
    out->hotkey_id = 0;

    safe_copy(out->process_name, sizeof(out->process_name), win->process_name);
    safe_copy(out->event_label, sizeof(out->event_label), win->window_title);

    return (int)sizeof(input_hook_binary_event_t);
}

int protocol_format_hotkey_binary(int hotkey_id, const char *name, uint64_t timestamp_ms, input_hook_binary_event_t *out)
{
    if (!out) return -1;

    memset(out, 0, sizeof(*out));
    out->magic = INPUT_HOOK_MAGIC_BIN;
    out->version = INPUT_HOOK_VERSION;
    out->event_type = EVENT_TYPE_HOTKEY_PRESS;
    out->timestamp_ms = timestamp_ms;
    out->pid = (uint32_t)GetCurrentProcessId();
    out->is_fullscreen = 0;
    out->hotkey_id = (uint8_t)hotkey_id;

    snprintf(out->process_name, sizeof(out->process_name), "hotkey");
    snprintf(out->event_label, sizeof(out->event_label), "%s", name ? name : "UNKNOWN");

    return (int)sizeof(input_hook_binary_event_t);
}

void protocol_encrypt(void *data, size_t len, uint64_t key)
{
    asm_crypto_xor_in_place(data, len, key);
}

void protocol_decrypt(void *data, size_t len, uint64_t key)
{
    asm_crypto_xor_in_place(data, len, key);
}
