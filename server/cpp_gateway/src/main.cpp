#include "gateway/config.hpp"
#include "gateway/listener.hpp"
#include "gateway/log.hpp"

#include "asm_crypto.h"

#include <boost/asio/io_context.hpp>
#include <boost/asio/signal_set.hpp>

#include <algorithm>
#include <csignal>
#include <exception>
#include <iostream>
#include <thread>
#include <vector>

namespace net = boost::asio;
using tcp = net::ip::tcp;

int main()
{
    try {
        const gateway::Config cfg = gateway::load_config_from_env();

        // The concurrency hint only reflects the expected thread count;
        // io_context itself remains a shared, thread-safe scheduler.
        net::io_context ioc(cfg.worker_threads);

        // Signals stop the scheduler; outstanding handlers run down and
        // every Session closes its socket on destruction. Windows has no
        // SIGTERM: SIGBREAK covers the console close/Ctrl-Break event.
#ifdef _WIN32
        net::signal_set signals(ioc, SIGINT, SIGBREAK);
#else
        net::signal_set signals(ioc, SIGINT, SIGTERM);
#endif
        signals.async_wait([&](const boost::system::error_code &, int) {
            ioc.stop();
        });

        const tcp::endpoint endpoint(
            net::ip::make_address(cfg.bind_address), cfg.port);
        std::make_shared<gateway::Listener>(ioc, endpoint, cfg.crypto_key)
            ->run();

        const unsigned threads =
            cfg.worker_threads != 0
                ? cfg.worker_threads
                : std::max(2u, std::thread::hardware_concurrency());

        std::vector<std::thread> workers;
        workers.reserve(threads - 1);
        for (unsigned i = 1; i < threads; ++i)
            workers.emplace_back([&ioc] { ioc.run(); });

        gateway::Logger::info(
            "gateway " + std::string(asm_crypto_version()) +
            " listening on " + cfg.bind_address + ":" +
            std::to_string(cfg.port) + " with " +
            std::to_string(threads) + " worker thread(s)");

        ioc.run();
        for (std::thread &worker : workers)
            worker.join();

        gateway::Logger::info("gateway stopped");
    } catch (const std::exception &e) {
        std::cerr << "fatal: " << e.what() << '\n';
        return 1;
    }
    return 0;
}
