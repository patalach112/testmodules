#ifndef WIN_INPUT_HOOKS_WS_CLIENT_H
#define WIN_INPUT_HOOKS_WS_CLIENT_H

#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>

#ifdef _WIN32
#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>
#include <winsock2.h>
#include <ws2tcpip.h>
#else
typedef int SOCKET;
#define INVALID_SOCKET (-1)
#endif

typedef struct {
    SOCKET   sock;
    bool     connected;
    char     host[128];
    uint16_t port;
} ws_client_t;

bool ws_client_global_init(void);
void ws_client_global_cleanup(void);

void ws_client_init(ws_client_t *client);
bool ws_client_connect(ws_client_t *client, const char *host, uint16_t port);
bool ws_client_send(ws_client_t *client, const void *data, size_t len, bool binary);
int  ws_client_poll_read(ws_client_t *client, void *out_buf, size_t buflen, size_t *out_len, int timeout_ms);
void ws_client_close(ws_client_t *client);
bool ws_client_is_connected(const ws_client_t *client);

#endif /* WIN_INPUT_HOOKS_WS_CLIENT_H */
