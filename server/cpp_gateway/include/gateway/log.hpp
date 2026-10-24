#ifndef GATEWAY_LOG_HPP
#define GATEWAY_LOG_HPP

#include <string_view>

namespace gateway {

/*
 * Minimal thread-safe line logger.
 *
 * Every WebSocket session runs on its own strand, so several sessions can
 * emit log lines concurrently. A single process-wide mutex keeps each line
 * (timestamp, level, message) intact on stdout.
 */
class Logger {
public:
    static void info(std::string_view message);
    static void warn(std::string_view message);
    static void error(std::string_view message);
};

} // namespace gateway

#endif // GATEWAY_LOG_HPP
