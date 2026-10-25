#ifndef WIN_INPUT_HOOKS_CONFIG_H
#define WIN_INPUT_HOOKS_CONFIG_H

#include <stdbool.h>
#include <stdint.h>

#define DEFAULT_GATEWAY_HOST "127.0.0.1"
#define DEFAULT_GATEWAY_PORT 8080
#define DEFAULT_CRYPTO_KEY   0x0123456789ABCDEFull

typedef struct {
    char     host[128];
    uint16_t port;
    uint64_t crypto_key;
    bool     binary_mode;
    bool     verbose;
    int64_t  max_events;     /* -1 for continuous operation */
    bool     simulate_events;/* Simulate test events for automation */
} input_hooks_config_t;

void config_init_defaults(input_hooks_config_t *cfg);
int  config_parse_args(input_hooks_config_t *cfg, int argc, char *argv[]);
void config_print_help(const char *prog_name);

#endif /* WIN_INPUT_HOOKS_CONFIG_H */
