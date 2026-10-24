#ifndef GATEWAY_LISTENER_HPP
#define GATEWAY_LISTENER_HPP

#include <boost/asio/io_context.hpp>
#include <boost/asio/ip/tcp.hpp>
#include <boost/beast/core/error.hpp>

#include <cstdint>
#include <memory>

namespace gateway {

/*
 * Accepts plain TCP connections and promotes each one to a Session.
 *
 * TLS is terminated at the Cloudflare edge; cloudflared reaches this
 * listener over the container network, so the socket itself is plain HTTP
 * upgraded to WebSocket. Each accepted socket is bound to its own strand,
 * giving every Session a serialized call chain without a global lock.
 */
class Listener : public std::enable_shared_from_this<Listener> {
public:
    Listener(boost::asio::io_context &ioc,
             boost::asio::ip::tcp::endpoint endpoint,
             std::uint64_t crypto_key);

    void run();

private:
    void do_accept();
    void on_accept(boost::beast::error_code ec,
                   boost::asio::ip::tcp::socket socket);

    boost::asio::io_context &ioc_;
    boost::asio::ip::tcp::acceptor acceptor_;
    std::uint64_t crypto_key_;
};

} // namespace gateway

#endif // GATEWAY_LISTENER_HPP
