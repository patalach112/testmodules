#include "gateway/log.hpp"

#include <chrono>
#include <ctime>
#include <iomanip>
#include <iostream>
#include <mutex>
#include <sstream>

namespace gateway {
namespace {

std::mutex &output_mutex()
{
    static std::mutex mutex;
    return mutex;
}

void emit(char level, std::string_view message)
{
    const auto now = std::chrono::system_clock::now();
    const std::time_t now_c = std::chrono::system_clock::to_time_t(now);

    std::tm utc{};
    gmtime_r(&now_c, &utc);

    std::ostringstream line;
    line << std::put_time(&utc, "%Y-%m-%dT%H:%M:%SZ") << ' ' << level << ' '
         << message << '\n';

    std::lock_guard<std::mutex> lock(output_mutex());
    std::cout << line.str();
    std::cout.flush();
}

} // namespace

void Logger::info(std::string_view message) { emit('I', message); }
void Logger::warn(std::string_view message) { emit('W', message); }
void Logger::error(std::string_view message) { emit('E', message); }

} // namespace gateway
