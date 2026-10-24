#include "gateway/listener.hpp"

#include "gateway/log.hpp"
#include "gateway/session.hpp"

#include <boost/asio/strand.hpp>
#include <stdexcept>
#include <utility>

namespace gateway {

namespace beast = boost::beast;
namespace net = boost::asio;
using tcp = net::ip::tcp;

Listener::Listener(net::io_context &ioc, tcp::endpoint endpoint,
                   std::uint64_t crypto_key)
    : ioc_(ioc),
      acceptor_(net::make_strand(ioc)),
      crypto_key_(crypto_key)
{
    beast::error_code ec;

    acceptor_.open(endpoint.protocol(), ec);
    if (ec)
        throw std::runtime_error("acceptor open failed: " + ec.message());

    acceptor_.set_option(net::socket_base::reuse_address(true), ec);
    if (ec)
        throw std::runtime_error("reuse_address failed: " + ec.message());

    acceptor_.bind(endpoint, ec);
    if (ec)
        throw std::runtime_error("bind failed: " + ec.message());

    acceptor_.listen(net::socket_base::max_listen_connections, ec);
    if (ec)
        throw std::runtime_error("listen failed: " + ec.message());
}

void Listener::run()
{
    do_accept();
}

void Listener::do_accept()
{
    // Bind the new socket to a fresh strand: each Session then has a
    // serialized execution path independent of every other connection.
    acceptor_.async_accept(
        net::make_strand(ioc_),
        beast::bind_front_handler(&Listener::on_accept,
                                  shared_from_this()));
}

void Listener::on_accept(beast::error_code ec, tcp::socket socket)
{
    if (ec) {
        // Transient accept errors (EMFILE, ECONNABORTED, ...) must not kill
        // the accept loop; log and keep serving.
        Logger::warn("accept failed: " + ec.message());
    } else {
        std::make_shared<Session>(std::move(socket), crypto_key_)->run();
    }

    do_accept();
}

} // namespace gateway
