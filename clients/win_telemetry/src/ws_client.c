#include "ws_client.h"

#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#ifdef _MSC_VER
#pragma comment(lib, "ws2_32.lib")
#endif

static bool s_winsock_initialized = false;

static const char b64_table[] = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";

static void base64_encode(const uint8_t *in, size_t len, char *out)
{
    size_t i = 0, j = 0;
    while (i < len) {
        uint32_t a = in[i++];
        uint32_t b = (i < len) ? in[i++] : 0;
        uint32_t c = (i < len) ? in[i++] : 0;
        uint32_t triple = (a << 16) | (b << 8) | c;

        out[j++] = b64_table[(triple >> 18) & 0x3F];
        out[j++] = b64_table[(triple >> 12) & 0x3F];
        out[j++] = (i > len + 1) ? '=' : b64_table[(triple >> 6) & 0x3F];
        out[j++] = (i > len) ? '=' : b64_table[triple & 0x3F];
    }
    out[j] = '\0';
}

static void generate_mask_key(uint8_t mask[4])
{
    /* Use high-resolution counter + rand for masking key */
    static uint32_t counter = 0x9e3779b9;
    counter += (uint32_t)GetTickCount64() + 17;
    mask[0] = (uint8_t)(counter & 0xFF);
    mask[1] = (uint8_t)((counter >> 8) & 0xFF);
    mask[2] = (uint8_t)((counter >> 16) & 0xFF);
    mask[3] = (uint8_t)((counter >> 24) & 0xFF);
    if (mask[0] == 0 && mask[1] == 0 && mask[2] == 0 && mask[3] == 0) {
        mask[0] = 0x5A;
    }
}

bool ws_client_global_init(void)
{
    if (s_winsock_initialized) return true;
    WSADATA wsa;
    int res = WSAStartup(MAKEWORD(2, 2), &wsa);
    if (res != 0) {
        return false;
    }
    s_winsock_initialized = true;
    return true;
}

void ws_client_global_cleanup(void)
{
    if (s_winsock_initialized) {
        WSACleanup();
        s_winsock_initialized = false;
    }
}

void ws_client_init(ws_client_t *client)
{
    if (!client) return;
    client->sock = INVALID_SOCKET;
    client->connected = false;
    memset(client->host, 0, sizeof(client->host));
    client->port = 0;
}

bool ws_client_connect(ws_client_t *client, const char *host, uint16_t port)
{
    if (!client || !host) return false;
    if (!ws_client_global_init()) return false;

    ws_client_close(client);

    strncpy(client->host, host, sizeof(client->host) - 1);
    client->port = port;

    char port_str[16];
    snprintf(port_str, sizeof(port_str), "%u", port);

    struct addrinfo hints;
    memset(&hints, 0, sizeof(hints));
    hints.ai_family = AF_INET; /* IPv4 */
    hints.ai_socktype = SOCK_STREAM;
    hints.ai_protocol = IPPROTO_TCP;

    struct addrinfo *result = NULL;
    int res = getaddrinfo(host, port_str, &hints, &result);
    if (res != 0 || !result) {
        return false;
    }

    SOCKET s = socket(result->ai_family, result->ai_socktype, result->ai_protocol);
    if (s == INVALID_SOCKET) {
        freeaddrinfo(result);
        return false;
    }

    /* Disable Nagle algorithm for low-latency telemetry streaming */
    int nodelay = 1;
    setsockopt(s, IPPROTO_TCP, TCP_NODELAY, (const char *)&nodelay, sizeof(nodelay));

    if (connect(s, result->ai_addr, (int)result->ai_addrlen) == SOCKET_ERROR) {
        closesocket(s);
        freeaddrinfo(result);
        return false;
    }
    freeaddrinfo(result);

    /* Generate random 16-byte Sec-WebSocket-Key */
    uint8_t nonce[16];
    for (int i = 0; i < 16; ++i) {
        nonce[i] = (uint8_t)((rand() + i * 13 + (int)GetTickCount64()) & 0xFF);
    }
    char b64_key[32];
    base64_encode(nonce, 16, b64_key);

    /* Send RFC 6455 HTTP Upgrade request */
    char req[512];
    int req_len = snprintf(
        req, sizeof(req),
        "GET / HTTP/1.1\r\n"
        "Host: %s:%u\r\n"
        "Upgrade: websocket\r\n"
        "Connection: Upgrade\r\n"
        "Sec-WebSocket-Key: %s\r\n"
        "Sec-WebSocket-Version: 13\r\n"
        "\r\n",
        host, port, b64_key
    );

    if (send(s, req, req_len, 0) == SOCKET_ERROR) {
        closesocket(s);
        return false;
    }

    /* Read HTTP 101 response */
    char resp[1024];
    int total_read = 0;
    while (total_read < (int)sizeof(resp) - 1) {
        int r = recv(s, resp + total_read, (int)sizeof(resp) - 1 - total_read, 0);
        if (r <= 0) {
            closesocket(s);
            return false;
        }
        total_read += r;
        resp[total_read] = '\0';
        if (strstr(resp, "\r\n\r\n")) {
            break;
        }
    }

    /* Verify HTTP 101 status */
    if (strstr(resp, "101 Switching Protocols") == NULL && strstr(resp, "HTTP/1.1 101") == NULL) {
        closesocket(s);
        return false;
    }

    client->sock = s;
    client->connected = true;
    return true;
}

bool ws_client_send(ws_client_t *client, const void *data, size_t len, bool binary)
{
    if (!client || !client->connected || client->sock == INVALID_SOCKET) {
        return false;
    }

    uint8_t opcode = binary ? 0x02 : 0x01;
    uint8_t header[14];
    size_t header_len = 0;

    header[0] = 0x80 | (opcode & 0x0F); /* FIN + opcode */

    /* Client-to-server frames MUST have the mask bit set (0x80) */
    if (len < 126) {
        header[1] = 0x80 | (uint8_t)len;
        header_len = 2;
    } else if (len <= 0xFFFF) {
        header[1] = 0x80 | 126;
        header[2] = (uint8_t)((len >> 8) & 0xFF);
        header[3] = (uint8_t)(len & 0xFF);
        header_len = 4;
    } else {
        header[1] = 0x80 | 127;
        for (int i = 0; i < 8; ++i) {
            header[2 + i] = (uint8_t)((len >> (56 - i * 8)) & 0xFF);
        }
        header_len = 10;
    }

    uint8_t mask[4];
    generate_mask_key(mask);
    memcpy(header + header_len, mask, 4);
    header_len += 4;

    /* Send frame header */
    if (send(client->sock, (const char *)header, (int)header_len, 0) == SOCKET_ERROR) {
        ws_client_close(client);
        return false;
    }

    if (len == 0) return true;

    /* Staging buffer for masked payload to avoid heap allocation */
    uint8_t chunk[4096];
    const uint8_t *src = (const uint8_t *)data;
    size_t offset = 0;

    while (offset < len) {
        size_t chunk_len = len - offset;
        if (chunk_len > sizeof(chunk)) {
            chunk_len = sizeof(chunk);
        }

        for (size_t i = 0; i < chunk_len; ++i) {
            chunk[i] = src[offset + i] ^ mask[(offset + i) & 3];
        }

        int sent = send(client->sock, (const char *)chunk, (int)chunk_len, 0);
        if (sent <= 0) {
            ws_client_close(client);
            return false;
        }
        offset += (size_t)sent;
    }

    return true;
}

int ws_client_poll_read(ws_client_t *client, void *out_buf, size_t buflen, size_t *out_len, int timeout_ms)
{
    if (!client || !client->connected || client->sock == INVALID_SOCKET) {
        return -1;
    }

    fd_set read_fds;
    FD_ZERO(&read_fds);
    FD_SET(client->sock, &read_fds);

    struct timeval tv;
    tv.tv_sec = timeout_ms / 1000;
    tv.tv_usec = (timeout_ms % 1000) * 1000;

    int sel = select((int)client->sock + 1, &read_fds, NULL, NULL, &tv);
    if (sel <= 0) {
        return sel; /* 0 = timeout, < 0 = socket error */
    }

    /* Read frame header (at least 2 bytes) */
    uint8_t hdr[2];
    int r = recv(client->sock, (char *)hdr, 2, 0);
    if (r <= 0) {
        ws_client_close(client);
        return -1;
    }

    uint8_t opcode = hdr[0] & 0x0F;
    bool masked = (hdr[1] & 0x80) != 0;
    uint64_t payload_len = hdr[1] & 0x7F;

    if (payload_len == 126) {
        uint8_t ext[2];
        if (recv(client->sock, (char *)ext, 2, 0) != 2) {
            ws_client_close(client);
            return -1;
        }
        payload_len = ((uint64_t)ext[0] << 8) | ext[1];
    } else if (payload_len == 127) {
        uint8_t ext[8];
        if (recv(client->sock, (char *)ext, 8, 0) != 8) {
            ws_client_close(client);
            return -1;
        }
        payload_len = 0;
        for (int i = 0; i < 8; ++i) {
            payload_len = (payload_len << 8) | ext[i];
        }
    }

    uint8_t mask_key[4] = {0};
    if (masked) {
        if (recv(client->sock, (char *)mask_key, 4, 0) != 4) {
            ws_client_close(client);
            return -1;
        }
    }

    /* Read payload */
    size_t to_read = (size_t)payload_len;
    if (to_read > buflen) {
        to_read = buflen;
    }

    size_t total_payload = 0;
    uint8_t *dst = (uint8_t *)out_buf;
    while (total_payload < to_read) {
        int bytes = recv(client->sock, (char *)(dst + total_payload), (int)(to_read - total_payload), 0);
        if (bytes <= 0) {
            ws_client_close(client);
            return -1;
        }
        total_payload += (size_t)bytes;
    }

    /* If frame had mask (unexpected from server per RFC, but handle anyway) */
    if (masked) {
        for (size_t i = 0; i < total_payload; ++i) {
            dst[i] ^= mask_key[i & 3];
        }
    }

    /* Drain any excess bytes if buflen was smaller */
    if (payload_len > to_read) {
        size_t excess = (size_t)payload_len - to_read;
        char scrap[256];
        while (excess > 0) {
            size_t take = (excess > sizeof(scrap)) ? sizeof(scrap) : excess;
            int bytes = recv(client->sock, scrap, (int)take, 0);
            if (bytes <= 0) break;
            excess -= (size_t)bytes;
        }
    }

    if (out_len) {
        *out_len = total_payload;
    }

    /* Handle Ping: automatically reply with Pong */
    if (opcode == 0x09) {
        ws_client_send(client, out_buf, total_payload, true);
    }

    return (int)total_payload;
}

void ws_client_close(ws_client_t *client)
{
    if (!client) return;
    if (client->sock != INVALID_SOCKET) {
        /* Send WebSocket Close frame (0x88) */
        uint8_t close_frame[6] = {0x88, 0x80, 0x00, 0x00, 0x00, 0x00};
        send(client->sock, (const char *)close_frame, sizeof(close_frame), 0);

        shutdown(client->sock, SD_BOTH);
        closesocket(client->sock);
        client->sock = INVALID_SOCKET;
    }
    client->connected = false;
}

bool ws_client_is_connected(const ws_client_t *client)
{
    return client && client->connected && (client->sock != INVALID_SOCKET);
}
