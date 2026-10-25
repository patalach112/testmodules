#include "config.h"

#include <stdio.h>
#include <stdlib.h>
#include <string.h>

void config_init_defaults(input_hooks_config_t *cfg)
{
    if (!cfg) return;
    memset(cfg, 0, sizeof(*cfg));

    strncpy(cfg->host, DEFAULT_GATEWAY_HOST, sizeof(cfg->host) - 1);
    cfg->port = DEFAULT_GATEWAY_PORT;
    cfg->crypto_key = DEFAULT_CRYPTO_KEY;
    cfg->binary_mode = false;
    cfg->verbose = false;
    cfg->max_events = -1;
    cfg->simulate_events = false;

    const char *env_host = getenv("GATEWAY_HOST");
    if (env_host && env_host[0] != '\0') {
        strncpy(cfg->host, env_host, sizeof(cfg->host) - 1);
    }

    const char *env_port = getenv("GATEWAY_PORT");
    if (env_port) {
        int p = atoi(env_port);
        if (p > 0 && p <= 65535) {
            cfg->port = (uint16_t)p;
        }
    }

    const char *env_key = getenv("ASM_CRYPTO_KEY");
    if (env_key) {
        cfg->crypto_key = strtoull(env_key, NULL, 0);
    }
}

int config_parse_args(input_hooks_config_t *cfg, int argc, char *argv[])
{
    for (int i = 1; i < argc; ++i) {
        const char *arg = argv[i];

        if (strcmp(arg, "--help") == 0 || strcmp(arg, "-h") == 0) {
            config_print_help(argv[0]);
            return 1;
        } else if (strcmp(arg, "--binary") == 0) {
            cfg->binary_mode = true;
        } else if (strcmp(arg, "--verbose") == 0 || strcmp(arg, "-v") == 0) {
            cfg->verbose = true;
        } else if (strcmp(arg, "--host") == 0 && i + 1 < argc) {
            strncpy(cfg->host, argv[++i], sizeof(cfg->host) - 1);
        } else if (strcmp(arg, "--port") == 0 && i + 1 < argc) {
            cfg->port = (uint16_t)atoi(argv[++i]);
        } else if (strcmp(arg, "--key") == 0 && i + 1 < argc) {
            cfg->crypto_key = strtoull(argv[++i], NULL, 0);
        } else if ((strcmp(arg, "--count") == 0 || strcmp(arg, "-n") == 0) && i + 1 < argc) {
            cfg->max_events = atoll(argv[++i]);
        } else if (strcmp(arg, "--simulate") == 0) {
            cfg->simulate_events = true;
        } else {
            fprintf(stderr, "Unknown or incomplete argument: %s\n", arg);
            config_print_help(argv[0]);
            return -1;
        }
    }
    return 0;
}

void config_print_help(const char *prog_name)
{
    printf("Windows Game Detector & Global Hotkey Engine (Native Win32 C)\n\n");
    printf("Usage:\n");
    printf("  %s [options]\n\n", prog_name);
    printf("Options:\n");
    printf("  --host <ip/host>   Gateway address (default: 127.0.0.1)\n");
    printf("  --port <port>      Gateway port (default: 8080)\n");
    printf("  --key <hex>        64-bit encryption key (default: 0x0123456789ABCDEF)\n");
    printf("  --binary           Send binary packed telemetry frame instead of JSON\n");
    printf("  --count, -n <num>  Exit after processing <num> events (for testing)\n");
    printf("  --simulate         Simulate window and hotkey events for verification\n");
    printf("  --verbose, -v      Enable verbose logging and payload preview\n");
    printf("  --help, -h         Display this help menu\n\n");
    printf("Global Hotkeys Registered:\n");
    printf("  CTRL + ALT + S     Snapshot capture trigger\n");
    printf("  CTRL + ALT + M     Mute / signal state toggle\n");
    printf("  CTRL + ALT + O     In-game overlay toggle\n");
    printf("  CTRL + ALT + Q     Clean shutdown request\n\n");
    printf("Environment Variables:\n");
    printf("  GATEWAY_HOST       Fallback host\n");
    printf("  GATEWAY_PORT       Fallback port\n");
    printf("  ASM_CRYPTO_KEY     Fallback 64-bit key\n");
}
