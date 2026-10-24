#ifndef GATEWAY_SESSION_HPP
#define GATEWAY_SESSION_HPP

#include <boost/beast/core/flat_buffer.hpp>
#include <boost/beast/core/tcp_stream.hpp>
#include <boost/beast/websocket/stream.hpp>

#include <cstdint>
#include <memory>
#include <vector>

namespace gateway {

/*
 * One Session owns one WebSocket connection and its full lifecycle:
 *
 *   TCP accept (see Listener)
 *     -> WebSocket upgrade handshake, async_accept()
 *     -> repeated async_read() / transform / async_write() loop
 *     -> close handshake or transport teardown on destruction
 *
 * Incoming frames are passed through the asm_crypto XOR transform and the
 * transformed bytes are echoed back, so the data path stays entirely in
 * process memory. The object keeps itself alive with shared_from_this()
 * until the last asynchronous operation completes.
 */
class Session : public std::enable_shared_from_this<Session> {
public:
    Session(boost::asio::ip::tcp::socket &&socket, std::uint64_t crypto_key);

    // Called once by the Listener. Completes on the connection's strand.
    void run();

private:
    using WebSocket =
        boost::beast::websocket::stream<boost::beast::tcp_stream>;

    static constexpr std::size_t kMaxFrameSize = 16u * 1024u * 1024u;

    void accept();
    void on_accept(boost::beast::error_code ec);
    void read();
    void on_read(boost::beast::error_code ec, std::size_t bytes_transferred);
    void on_write(boost::beast::error_code ec, std::size_t bytes_transferred);

    WebSocket ws_;
    boost::beast::flat_buffer rx_;

    // Holds the transformed frame for the duration of async_write(). Reused
    // across frames so steady-state traffic causes no reallocations.
    std::vector<std::uint8_t> tx_;

    std::uint64_t crypto_key_;
};

} // namespace gateway

#endif // GATEWAY_SESSION_HPP
