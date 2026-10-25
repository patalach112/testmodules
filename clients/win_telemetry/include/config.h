#ifndef WIN_TELEMETRY_CONFIG_H
#define WIN_TELEMETRY_CONFIG_H

#include <stdbool.h>
#include <stdint.h>

#define DEFAULT_GATEWAY_HOST "127.0.0.1"
#define DEFAULT_GATEWAY_PORT 8080
#define DEFAULT_INTERVAL_MS  1000
#define DEFAULT_CRYPTO_KEY   0x0123456789ABCDEFull

typedef struct {
    char     host[128];
    uint16_t port;
    uint32_t interval_ms;
    uint64_t crypto_key;
    bool     binary_mode;
    bool     service_mode;
    bool     install_service;
    bool     uninstall_service;
    bool     verbose;
    int64_t  max_count; /* -1 for continuous daemon mode */
} telemetry_config_t;

void config_init_defaults(telemetry_config_t *cfg);
int  config_parse_args(telemetry_config_t *cfg, int argc, char *argv[]);
void config_print_help(const char *prog_name);

#endif /* WIN_TELEMETRY_CONFIG_H */
