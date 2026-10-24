#ifndef GATEWAY_CONFIG_HPP
#define GATEWAY_CONFIG_HPP

#include <cstdint>
#include <string>

namespace gateway {

struct Config {
    // Interface the listener binds to. Cloudflared dials this address;
    // it is never exposed directly to the internet.
    std::string bind_address = "0.0.0.0";

    std::uint16_t port = 8080;

    // Repeating 64-bit key handed to the asm_crypto transform.
    std::uint64_t crypto_key = 0x0123456789ABCDEFull;

    // I/O worker threads. Zero means "derive from hardware_concurrency()".
    unsigned worker_threads = 0;
};

/*
 * Builds a Config from environment variables:
 *
 *   GATEWAY_BIND     IPv4/IPv6 address, default 0.0.0.0
 *   GATEWAY_PORT     TCP port,        default 8080
 *   GATEWAY_THREADS  worker count,    default auto
 *   ASM_CRYPTO_KEY   decimal or 0x-prefixed hexadecimal uint64
 *
 * Throws std::runtime_error when a variable is present but malformed.
 */
Config load_config_from_env();

} // namespace gateway

#endif // GATEWAY_CONFIG_HPP
