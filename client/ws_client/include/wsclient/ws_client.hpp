#ifndef WSCLIENT_WS_CLIENT_HPP
#define WSCLIENT_WS_CLIENT_HPP

#include <boost/asio/io_context.hpp>
#include <boost/beast/core/flat_buffer.hpp>
#include <boost/beast/core/tcp_stream.hpp>
#include <boost/beast/websocket/stream.hpp>

#include <chrono>
#include <cstdint>
#include <string>
#include <vector>

namespace wsclient {

struct ClientOptions {
    std::string host;                 // DNS name or literal IP address
    std::string port = "80";
    std::string target = "/";         // HTTP path used in the upgrade request
    std::chrono::seconds timeout{10}; // per-operation deadline
};

/*
 * Minimal synchronous WebSocket client.
 *
 * Every method blocks until the operation finishes or the per-operation
 * timeout expires, in which case a boost::system::system_error is thrown.
 * The blocking model is deliberate: command-line tools and early client
 * prototypes want a call/response shape, while an asynchronous event loop
 * can be layered on top later without changing the wire logic.
 *
 * Plain ws:// only. TLS (wss://) is terminated at the Cloudflare edge for
 * the server, and adding it means linking an SSL stack; it is intentionally
 * left out of this module's first revision.
 */
class Client {
public:
    explicit Client(ClientOptions options);
    ~Client();

    Client(const Client &) = delete;
    Client &operator=(const Client &) = delete;

    // DNS resolution, TCP connect and the HTTP/1.1 Upgrade handshake.
    void connect();

    /*
     * Sends one frame and returns the server's reply as raw bytes.
     * binary=false sends a text opcode, true a binary opcode; the reply
     * is returned regardless of its opcode class.
     */
    std::vector<std::uint8_t> send(const std::vector<std::uint8_t> &payload,
                                   bool binary);

    // RFC 6455 close handshake; safe to call repeatedly.
    void close();

private:
    using WebSocket =
        boost::beast::websocket::stream<boost::beast::tcp_stream>;

    ClientOptions options_;
    boost::asio::io_context ioc_;
    WebSocket ws_;
    bool open_ = false;
};

} // namespace wsclient

#endif // WSCLIENT_WS_CLIENT_HPP
