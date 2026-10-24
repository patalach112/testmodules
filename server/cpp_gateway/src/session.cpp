#include "gateway/session.hpp"

#include "asm_crypto.h"
#include "gateway/log.hpp"

#include <boost/asio/buffer.hpp>
#include <boost/asio/dispatch.hpp>
#include <chrono>
#include <functional>
#include <string>
#include <utility>

namespace gateway {

namespace beast = boost::beast;
namespace net = boost::asio;
namespace websocket = beast::websocket;

Session::Session(net::ip::tcp::socket &&socket, std::uint64_t crypto_key)
    : ws_(std::move(socket)), crypto_key_(crypto_key)
{
}

void Session::run()
{
    // Hop onto the socket's strand so the whole session is serialized.
    net::dispatch(ws_.get_executor(),
                  std::bind_front(&Session::accept, shared_from_this()));
}

void Session::accept()
{
    // Bound the upgrade handshake; idle TCP connections never linger.
    ws_.next_layer().expires_after(std::chrono::seconds(30));
    ws_.read_message_max(kMaxFrameSize);

    // Reads the buffered HTTP Upgrade request and replies with HTTP 101.
    // Cloudflare already validated the edge TLS certificate; here we only
    // speak the WebSocket subprotocol (RFC 6455) over plain TCP.
    ws_.async_accept(
        std::bind_front(&Session::on_accept, shared_from_this()));
}

void Session::on_accept(beast::error_code ec)
{
    if (ec) {
        Logger::warn("websocket upgrade failed: " + ec.message());
        return;
    }

    // After the upgrade, liveness is driven by RFC 6455 ping/pong frames;
    // Beast answers pings automatically, so no transport timeout is wanted.
    ws_.next_layer().expires_never();
    read();
}

void Session::read()
{
    // Client frames arrive masked; Beast unmasks them into rx_ transparently.
    ws_.async_read(rx_,
                   std::bind_front(&Session::on_read, shared_from_this()));
}

void Session::on_read(beast::error_code ec, std::size_t /*bytes*/)
{
    // A normal close handshake is an expected end of lifecycle, not an error.
    if (ec == websocket::error::closed)
        return;
    if (ec) {
        Logger::warn("websocket read failed: " + ec.message());
        return;
    }

    const std::size_t size = net::buffer_size(rx_.data());
    tx_.resize(size);

    // flat_buffer stores its readable area in one contiguous block, so the
    // transform sees a plain pointer pair and never copies the frame twice.
    asm_crypto_xor(rx_.data().data(), tx_.data(), size, crypto_key_);

    // Echo in the same opcode class the client used (text or binary).
    ws_.binary(ws_.got_binary());
    ws_.async_write(net::buffer(tx_),
                    std::bind_front(&Session::on_write, shared_from_this()));
}

void Session::on_write(beast::error_code ec, std::size_t bytes)
{
    if (ec) {
        Logger::warn("websocket write failed: " + ec.message());
        return;
    }

    // Frame fully sent: release the receive buffer and the tx staging area.
    rx_.consume(bytes);
    tx_.clear();
    read();
}

} // namespace gateway
