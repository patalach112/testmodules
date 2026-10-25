#include "config.h"

#include <stdio.h>
#include <stdlib.h>
#include <string.h>

void config_init_defaults(telemetry_config_t *cfg)
{
    if (!cfg) return;
    memset(cfg, 0, sizeof(*cfg));

    strncpy(cfg->host, DEFAULT_GATEWAY_HOST, sizeof(cfg->host) - 1);
    cfg->port = DEFAULT_GATEWAY_PORT;
    cfg->interval_ms = DEFAULT_INTERVAL_MS;
    cfg->crypto_key = DEFAULT_CRYPTO_KEY;
    cfg->binary_mode = false;
    cfg->service_mode = false;
    cfg->install_service = false;
    cfg->uninstall_service = false;
    cfg->verbose = false;
    cfg->max_count = -1; /* Run continuously */

    /* Check environment variable overrides */
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

    const char *env_interval = getenv("TELEMETRY_INTERVAL");
    if (env_interval) {
        int iv = atoi(env_interval);
        if (iv >= 50) {
            cfg->interval_ms = (uint32_t)iv;
        }
    }
}

int config_parse_args(telemetry_config_t *cfg, int argc, char *argv[])
{
    for (int i = 1; i < argc; ++i) {
        const char *arg = argv[i];

        if (strcmp(arg, "--help") == 0 || strcmp(arg, "-h") == 0) {
            config_print_help(argv[0]);
            return 1; /* Request exit after help */
        } else if (strcmp(arg, "--install") == 0) {
            cfg->install_service = true;
        } else if (strcmp(arg, "--uninstall") == 0) {
            cfg->uninstall_service = true;
        } else if (strcmp(arg, "--service") == 0) {
            cfg->service_mode = true;
        } else if (strcmp(arg, "--binary") == 0) {
            cfg->binary_mode = true;
        } else if (strcmp(arg, "--verbose") == 0 || strcmp(arg, "-v") == 0) {
            cfg->verbose = true;
        } else if (strcmp(arg, "--host") == 0 && i + 1 < argc) {
            strncpy(cfg->host, argv[++i], sizeof(cfg->host) - 1);
        } else if (strcmp(arg, "--port") == 0 && i + 1 < argc) {
            cfg->port = (uint16_t)atoi(argv[++i]);
        } else if (strcmp(arg, "--interval") == 0 && i + 1 < argc) {
            int iv = atoi(argv[++i]);
            if (iv >= 50) cfg->interval_ms = (uint32_t)iv;
        } else if (strcmp(arg, "--key") == 0 && i + 1 < argc) {
            cfg->crypto_key = strtoull(argv[++i], NULL, 0);
        } else if (strcmp(arg, "--count") == 0 && i + 1 < argc) {
            cfg->max_count = atoll(argv[++i]);
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
    printf("Windows Telemetry & System Monitor Daemon\n\n");
    printf("Usage:\n");
    printf("  %s [options]\n\n", prog_name);
    printf("Options:\n");
    printf("  --host <ip/host>   Gateway address (default: 127.0.0.1)\n");
    printf("  --port <port>      Gateway port (default: 8080)\n");
    printf("  --interval <ms>    Sampling interval in ms (default: 1000)\n");
    printf("  --key <hex>        64-bit encryption key (default: 0x0123456789ABCDEF)\n");
    printf("  --binary           Send binary packed telemetry frame instead of JSON\n");
    printf("  --count <n>        Send n frames and exit (useful for testing)\n");
    printf("  --verbose, -v      Enable verbose hex dump and echo logging\n");
    printf("  --service          Run as Windows Service (invoked by SCM)\n");
    printf("  --install          Register and install as Windows Service\n");
    printf("  --uninstall        Remove Windows Service registration\n");
    printf("  --help, -h         Display this help message\n\n");
    printf("Environment Variables:\n");
    printf("  GATEWAY_HOST       Fallback host\n");
    printf("  GATEWAY_PORT       Fallback port\n");
    printf("  ASM_CRYPTO_KEY     Fallback 64-bit key\n");
    printf("  TELEMETRY_INTERVAL Fallback interval in ms\n");
}
