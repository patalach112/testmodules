#include "wsclient/ws_client.hpp"

#include <boost/asio/buffer.hpp>
#include <boost/asio/ip/tcp.hpp>
#include <utility>

namespace wsclient {

namespace beast = boost::beast;
namespace net = boost::asio;
using tcp = net::ip::tcp;

Client::Client(ClientOptions options)
    : options_(std::move(options)), ws_(ioc_)
{
}

Client::~Client()
{
    // A throwing destructor would terminate the process; a half-open
    // connection simply drops when the socket closes below.
    try {
        close();
    } catch (...) {
    }
}

void Client::connect()
{
    tcp::resolver resolver(ioc_);
    const auto endpoints = resolver.resolve(options_.host, options_.port);

    // The timeout arms the whole connect + handshake sequence. Beast's
    // tcp_stream cancels outstanding work with an error when it fires.
    ws_.next_layer().expires_after(options_.timeout);
    ws_.next_layer().connect(endpoints);

    // The Host header is host:port even for a literal IP, which is what
    // a plain upgrade endpoint behind a tunnel expects to see.
    ws_.handshake(options_.host + ":" + options_.port, options_.target);

    // After the upgrade, liveness is handled by the WebSocket ping/pong
    // machinery; re-arm a deadline per data operation instead.
    ws_.next_layer().expires_never();
    open_ = true;
}

std::vector<std::uint8_t>
Client::send(const std::vector<std::uint8_t> &payload, bool binary)
{
    ws_.next_layer().expires_after(options_.timeout);
    ws_.binary(binary);
    ws_.write(net::buffer(payload));

    beast::flat_buffer buffer;
    ws_.read(buffer);

    // Copy out of Beast's segmented buffer into one contiguous block that
    // the caller owns and can hand to the asm_crypto routines.
    std::vector<std::uint8_t> reply(net::buffer_size(buffer.data()));
    net::buffer_copy(net::buffer(reply), buffer.data());
    return reply;
}

void Client::close()
{
    if (!open_)
        return;

    beast::error_code ec;
    ws_.next_layer().expires_after(options_.timeout);
    ws_.close(beast::websocket::close_code::normal, ec);
    open_ = false;
}

} // namespace wsclient
