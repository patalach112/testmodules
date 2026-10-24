#include "gateway/config.hpp"

#include <charconv>
#include <cstdlib>
#include <stdexcept>
#include <string>
#include <string_view>

namespace gateway {
namespace {

std::string_view env_or(const char *name, std::string_view fallback)
{
    const char *value = std::getenv(name);
    return (value != nullptr && *value != '\0') ? std::string_view(value)
                                                : fallback;
}

std::uint16_t parse_port(std::string_view raw)
{
    unsigned value = 0;
    const auto [ptr, ec] =
        std::from_chars(raw.data(), raw.data() + raw.size(), value);
    if (ec != std::errc{} || ptr != raw.data() + raw.size() ||
        value == 0 || value > 65535) {
        throw std::runtime_error(
            "GATEWAY_PORT must be a number between 1 and 65535");
    }
    return static_cast<std::uint16_t>(value);
}

std::uint64_t parse_key(std::string_view raw)
{
    int base = 10;
    if (raw.size() > 2 && raw[0] == '0' &&
        (raw[1] == 'x' || raw[1] == 'X')) {
        raw.remove_prefix(2);
        base = 16;
    }

    std::uint64_t value = 0;
    const auto [ptr, ec] =
        std::from_chars(raw.data(), raw.data() + raw.size(), value, base);
    if (ec != std::errc{} || ptr != raw.data() + raw.size()) {
        throw std::runtime_error(
            "ASM_CRYPTO_KEY must be a decimal or 0x-prefixed hexadecimal "
            "64-bit integer");
    }
    return value;
}

unsigned parse_threads(std::string_view raw)
{
    unsigned value = 0;
    const auto [ptr, ec] =
        std::from_chars(raw.data(), raw.data() + raw.size(), value);
    if (ec != std::errc{} || ptr != raw.data() + raw.size()) {
        throw std::runtime_error("GATEWAY_THREADS must be a non-negative integer");
    }
    return value;
}

} // namespace

Config load_config_from_env()
{
    Config cfg;
    cfg.bind_address = std::string(env_or("GATEWAY_BIND", cfg.bind_address));
    cfg.port = parse_port(env_or("GATEWAY_PORT", "8080"));
    cfg.crypto_key = parse_key(env_or("ASM_CRYPTO_KEY", "0x0123456789ABCDEF"));
    cfg.worker_threads =
        parse_threads(env_or("GATEWAY_THREADS", "0"));
    return cfg;
}

} // namespace gateway
