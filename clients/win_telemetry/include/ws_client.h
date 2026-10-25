#ifndef WIN_TELEMETRY_WS_CLIENT_H
#define WIN_TELEMETRY_WS_CLIENT_H

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

/* Initialize Winsock subsystem */
bool ws_client_global_init(void);

/* Cleanup Winsock subsystem */
void ws_client_global_cleanup(void);

/* Initialize client struct */
void ws_client_init(ws_client_t *client);

/* Connect to gateway and perform RFC 6455 handshake */
bool ws_client_connect(ws_client_t *client, const char *host, uint16_t port);

/* Send WebSocket frame (text or binary) with RFC 6455 client masking */
bool ws_client_send(ws_client_t *client, const void *data, size_t len, bool binary);

/* Poll for incoming frame with timeout in milliseconds */
int  ws_client_poll_read(ws_client_t *client, void *out_buf, size_t buflen, size_t *out_len, int timeout_ms);

/* Gracefully close WebSocket connection */
void ws_client_close(ws_client_t *client);

/* Check if connected */
bool ws_client_is_connected(const ws_client_t *client);

#endif /* WIN_TELEMETRY_WS_CLIENT_H */
