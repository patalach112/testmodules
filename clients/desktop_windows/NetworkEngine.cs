using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace EcosystemClient
{
    /// <summary>Connection lifecycle as observed by the UI.</summary>
    public enum NetworkState
    {
        Disconnected = 0,
        Connecting = 1,
        Connected = 2,
        Reconnecting = 3,
        Stopping = 4
    }

    /// <summary>Immutable, lock-free snapshot of the transport counters.</summary>
    public readonly record struct NetworkCounters(
        long PacketsSent,
        long PacketsReceived,
        long BytesSent,
        long BytesReceived,
        long DroppedFrames,
        long CryptoOperations,
        long CryptoBytes,
        long ProbesSent,
        long ProbesAnswered,
        int Reconnects,
        double LatencyMilliseconds,
        TimeSpan Uptime)
    {
        /// <summary>Round-trip probes that were never answered, as a percentage.</summary>
        public double ProbeLossPercent =>
            ProbesSent <= 0 ? 0d : Math.Max(0d, (ProbesSent - ProbesAnswered) * 100d / ProbesSent);
    }

    /// <summary>Frame delivered by the engine. Data is plaintext when <see cref="NetworkOptions.DecodeInbound"/> is on.</summary>
    public sealed class FrameReceivedEventArgs : EventArgs
    {
        public FrameReceivedEventArgs(WebSocketMessageType messageType, ReadOnlyMemory<byte> payload)
        {
            MessageType = messageType;
            Payload = payload;
        }

        public WebSocketMessageType MessageType { get; }

        /// <summary>
        /// Backed by a pooled buffer owned by the engine. The memory is only valid for
        /// the duration of the event handler; call <c>ToArray()</c> to retain it.
        /// </summary>
        public ReadOnlyMemory<byte> Payload { get; }

        /// <summary>Convenience for text frames: UTF-8 decode with replacement (never throws).</summary>
        public string AsText() => Encoding.UTF8.GetString(Payload.Span);
    }

    /// <summary>Telemetry line produced by the engine on a background thread.</summary>
    public sealed class EngineLogEventArgs : EventArgs
    {
        public EngineLogEventArgs(LogLevel level, string message)
        {
            Level = level;
            Message = message;
        }

        public LogLevel Level { get; }

        public string Message { get; }
    }

    /// <summary>Latency probe measurement.</summary>
    public sealed class LatencyEventArgs : EventArgs
    {
        public LatencyEventArgs(int sequence, double milliseconds)
        {
            Sequence = sequence;
            Milliseconds = milliseconds;
        }

        public int Sequence { get; }

        public double Milliseconds { get; }
    }

    /// <summary>Immutable configuration for a <see cref="NetworkEngine"/> instance.</summary>
    public sealed class NetworkOptions
    {
        public Uri Endpoint { get; init; } = new Uri("ws://127.0.0.1:8080/");

        /// <summary>64-bit transform key shared with the gateway (<c>ASM_CRYPTO_KEY</c>).</summary>
        public ulong CryptoKey { get; init; } = NativeCrypto.DefaultKey;

        /// <summary>XOR-decode inbound frames in place (the gateway always transforms its echoes).</summary>
        public bool DecodeInbound { get; init; } = true;

        /// <summary>Send round-trip probes to measure real latency (and to drive the health check).</summary>
        public bool EnableLatencyProbes { get; init; } = true;

        public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(10);

        public TimeSpan ProbeInterval { get; init; } = TimeSpan.FromSeconds(2);

        /// <summary>No inbound traffic for this long is treated as a dead socket.</summary>
        public TimeSpan HealthTimeout { get; init; } = TimeSpan.FromSeconds(15);

        /// <summary>Ring buffer used for every <c>ReceiveAsync</c> call. Rented once per connection.</summary>
        public int ReceiveBufferSize { get; init; } = 32 * 1024;

        /// <summary>Hard ceiling for a single (possibly fragmented) message. Protects the pool.</summary>
        public int MaxMessageBytes { get; init; } = 4 * 1024 * 1024;

        /// <summary>Outbound queue depth before frames are dropped instead of accumulating.</summary>
        public int SendQueueCapacity { get; init; } = 2048;

        public TimeSpan BaseReconnectDelay { get; init; } = TimeSpan.FromMilliseconds(400);

        public TimeSpan MaxReconnectDelay { get; init; } = TimeSpan.FromSeconds(20);

        /// <summary>A session that survived this long resets the backoff ladder.</summary>
        public TimeSpan StableConnectionTime { get; init; } = TimeSpan.FromSeconds(20);

        /// <summary>Reconnect attempts before giving up; 0 means retry forever.</summary>
        public int MaxReconnectAttempts { get; init; }

        /// <summary>Extra HTTP headers for the upgrade request (auth tokens, routing hints).</summary>
        public System.Collections.Generic.IReadOnlyDictionary<string, string>? Headers { get; init; }

        public string? SubProtocol { get; init; }

        /// <summary>Bytes per probe frame: 4 magic + 4 sequence + 8 timestamp + 16 filler.</summary>
        public int ProbeFrameSize => 32;
    }

    /// <summary>
    /// Bulletproof <see cref="ClientWebSocket"/> supervisor.
    ///
    /// <para><b>Threading model.</b> Nothing in this class ever touches a UI object.
    /// <see cref="StartAsync"/> hands a supervisor loop to the thread pool; that loop
    /// owns four cooperating pumps (receive, send, probe, health) per connection.
    /// All <c>await</c>s use <c>ConfigureAwait(false)</c>, so no continuation can
    /// capture the dispatcher, and the WPF thread is never blocked by I/O.</para>
    ///
    /// <para><b>Memory model.</b> Receive buffers are rented once per connection from
    /// <see cref="ArrayPool{T}.Shared"/>; a message that arrives in fragments grows by
    /// doubling and is returned to the pool as soon as the handler returns. Outbound
    /// frames carry pooled arrays with transfer-of-ownership (<see cref="OutboundFrame"/>)
    /// and are always returned exactly once, including on the drop path. Nothing on the
    /// steady-state path allocates aside from the frame object itself.</para>
    ///
    /// <para><b>Failure model.</b> Every pump is wrapped by a guard that cancels its
    /// siblings. Whichever pump ends first (server close, socket error, health timeout,
    /// caller stop) tears the connection down; the supervisor then applies exponential
    /// backoff with jitter and reconnects. <c>ClientWebSocket</c> allows exactly one
    /// concurrent send, so all sends are serialised through a semaphore.</para>
    /// </summary>
    public sealed class NetworkEngine : IAsyncDisposable
    {
        private const byte ProbeMagic0 = 0xEC;
        private const byte ProbeMagic1 = 0x05;
        private const byte ProbeMagic2 = 0x01;
        private const byte ProbeMagic3 = 0x00;
        private const int ProbeHeaderSize = 16;

        private readonly NetworkOptions _options;
        private readonly object _lifecycleGate = new object();
        private readonly SemaphoreSlim _sendGate = new SemaphoreSlim(1, 1);

        private CancellationTokenSource? _lifetime;
        private Task? _supervisor;
        private Channel<OutboundFrame>? _outbound;

        private volatile NetworkState _state = NetworkState.Disconnected;

        private long _startedTimestamp;
        private long _lastRxTimestamp;
        private long _latencyBits;

        private long _packetsSent;
        private long _packetsReceived;
        private long _bytesSent;
        private long _bytesReceived;
        private long _droppedFrames;
        private long _cryptoOperations;
        private long _cryptoBytes;
        private long _probesSent;
        private long _probesAnswered;
        private int _reconnects;
        private int _probeSequence;
        private long _lastDropLog;

        public NetworkEngine(NetworkOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        /// <summary>Raised on a background thread whenever the connection state changes.</summary>
        public event EventHandler<NetworkState>? StateChanged;

        /// <summary>Raised on a background thread for every inbound data frame.</summary>
        public event EventHandler<FrameReceivedEventArgs>? FrameReceived;

        /// <summary>Raised on a background thread for every answered latency probe.</summary>
        public event EventHandler<LatencyEventArgs>? LatencyUpdated;

        /// <summary>Raised on a background thread for engine diagnostics.</summary>
        public event EventHandler<EngineLogEventArgs>? Log;

        public NetworkOptions Options => _options;

        public NetworkState State => _state;

        public bool IsConnected => _state == NetworkState.Connected;

        /// <summary>Starts (or restarts) the supervisor. Safe to call repeatedly.</summary>
        public Task StartAsync()
        {
            lock (_lifecycleGate)
            {
                if (_supervisor is { IsCompleted: false })
                {
                    return Task.CompletedTask;
                }

                ResetCounters();

                CancellationTokenSource lifetime = new CancellationTokenSource();
                _lifetime = lifetime;
                _startedTimestamp = Stopwatch.GetTimestamp();
                _supervisor = Task.Run(() => SupervisorAsync(lifetime.Token));
            }

            return Task.CompletedTask;
        }

        /// <summary>Stops the supervisor, closes the socket gracefully and waits for teardown.</summary>
        public async Task StopAsync()
        {
            Task? supervisor;
            CancellationTokenSource? lifetime;

            lock (_lifecycleGate)
            {
                supervisor = _supervisor;
                lifetime = _lifetime;
                _supervisor = null;
                _lifetime = null;
            }

            if (lifetime is null)
            {
                return;
            }

            SetState(NetworkState.Stopping);

            try
            {
                lifetime.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Already torn down; nothing to cancel.
            }

            if (supervisor is not null)
            {
                try
                {
                    await supervisor.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Expected during shutdown.
                }
            }

            lifetime.Dispose();
            SetState(NetworkState.Disconnected);
        }

        /// <summary>
        /// Queues a UTF-8 text frame. Returns <c>false</c> (never throws) when the
        /// connection is not open or the outbound queue is saturated.
        /// </summary>
        public ValueTask<bool> SendTextAsync(string text, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(text))
            {
                return new ValueTask<bool>(false);
            }

            int byteCount = Encoding.UTF8.GetByteCount(text);
            byte[] buffer = ArrayPool<byte>.Shared.Rent(byteCount);
            try
            {
                Encoding.UTF8.GetBytes(text, 0, text.Length, buffer, 0);
            }
            catch
            {
                ArrayPool<byte>.Shared.Return(buffer);
                throw;
            }

            return EnqueueAsync(buffer, byteCount, WebSocketMessageType.Text, cancellationToken);
        }

        /// <summary>Queues a binary frame; the payload is copied into a pooled buffer.</summary>
        public ValueTask<bool> SendBinaryAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default)
        {
            if (payload.IsEmpty)
            {
                return new ValueTask<bool>(false);
            }

            int length = payload.Length;
            byte[] buffer = ArrayPool<byte>.Shared.Rent(length);
            payload.Span.CopyTo(buffer);

            return EnqueueAsync(buffer, length, WebSocketMessageType.Binary, cancellationToken);
        }

        /// <summary>Reads every counter with a single consistent (per-counter) read.</summary>
        public NetworkCounters GetCounters() => new NetworkCounters(
            PacketsSent: Interlocked.Read(ref _packetsSent),
            PacketsReceived: Interlocked.Read(ref _packetsReceived),
            BytesSent: Interlocked.Read(ref _bytesSent),
            BytesReceived: Interlocked.Read(ref _bytesReceived),
            DroppedFrames: Interlocked.Read(ref _droppedFrames),
            CryptoOperations: Interlocked.Read(ref _cryptoOperations),
            CryptoBytes: Interlocked.Read(ref _cryptoBytes),
            ProbesSent: Interlocked.Read(ref _probesSent),
            ProbesAnswered: Interlocked.Read(ref _probesAnswered),
            Reconnects: Volatile.Read(ref _reconnects),
            LatencyMilliseconds: BitConverter.Int64BitsToDouble(Interlocked.Read(ref _latencyBits)),
            Uptime: _startedTimestamp == 0
                ? TimeSpan.Zero
                : Stopwatch.GetElapsedTime(Interlocked.Read(ref _startedTimestamp)));

        public async ValueTask DisposeAsync()
        {
            await StopAsync().ConfigureAwait(false);
            _sendGate.Dispose();
        }

        // ---------------------------------------------------------------------
        // Supervisor
        // ---------------------------------------------------------------------

        private async Task SupervisorAsync(CancellationToken cancellationToken)
        {
            int attempt = 0;

            while (!cancellationToken.IsCancellationRequested)
            {
                attempt++;
                TimeSpan connectedFor = TimeSpan.Zero;

                try
                {
                    SetState(attempt == 1 ? NetworkState.Connecting : NetworkState.Reconnecting);
                    connectedFor = await RunConnectionAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    EmitLog(LogLevel.Error, "connection attempt failed: " + Describe(ex));
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                // A session that stayed up long enough proves the endpoint is healthy:
                // restart the backoff ladder instead of inheriting a 20 s delay.
                if (connectedFor >= _options.StableConnectionTime)
                {
                    attempt = 0;
                }

                if (_options.MaxReconnectAttempts > 0 && attempt >= _options.MaxReconnectAttempts)
                {
                    EmitLog(LogLevel.Error,
                        $"giving up after {attempt} reconnect attempts (MaxReconnectAttempts).");
                    break;
                }

                Interlocked.Increment(ref _reconnects);

                TimeSpan delay = ComputeBackoff(attempt);
                SetState(NetworkState.Reconnecting);
                EmitLog(LogLevel.Warn,
                    $"reconnecting in {delay.TotalMilliseconds:F0} ms (attempt {attempt})");

                try
                {
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            SetState(NetworkState.Disconnected);
        }

        private TimeSpan ComputeBackoff(int attempt)
        {
            double baseMs = Math.Max(1d, _options.BaseReconnectDelay.TotalMilliseconds);
            double maxMs = Math.Max(baseMs, _options.MaxReconnectDelay.TotalMilliseconds);
            double exponent = Math.Min(16, Math.Max(0, attempt - 1));
            double raw = Math.Min(maxMs, baseMs * Math.Pow(2d, exponent));
            double jitter = 0.75d + (Random.Shared.NextDouble() * 0.5d);
            return TimeSpan.FromMilliseconds(Math.Min(maxMs, raw * jitter));
        }

        // ---------------------------------------------------------------------
        // One connection
        // ---------------------------------------------------------------------

        private async Task<TimeSpan> RunConnectionAsync(CancellationToken cancellationToken)
        {
            using ClientWebSocket socket = new ClientWebSocket();
            ConfigureSocket(socket);

            EmitLog(LogLevel.Info, "connecting to " + _options.Endpoint.AbsoluteUri);

            using (CancellationTokenSource connectTimeout =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                connectTimeout.CancelAfter(_options.ConnectTimeout);

                try
                {
                    await socket.ConnectAsync(_options.Endpoint, connectTimeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new TimeoutException(
                        $"handshake did not complete within {_options.ConnectTimeout.TotalSeconds:F1} s");
                }
            }

            Channel<OutboundFrame> outbound = Channel.CreateBounded<OutboundFrame>(
                new BoundedChannelOptions(_options.SendQueueCapacity)
                {
                    SingleReader = true,
                    SingleWriter = false,
                    FullMode = BoundedChannelFullMode.Wait,
                    AllowSynchronousContinuations = false
                });

            Volatile.Write(ref _outbound, outbound);
            Interlocked.Exchange(ref _lastRxTimestamp, Stopwatch.GetTimestamp());

            long connectedAt = Stopwatch.GetTimestamp();
            SetState(NetworkState.Connected);
            EmitLog(LogLevel.Success,
                $"websocket established (subprotocol: {DescribeSubProtocol(socket)})");

            using CancellationTokenSource connection = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            Task[] pumps =
            {
                GuardAsync(token => ReceivePumpAsync(socket, token), connection, "receive"),
                GuardAsync(token => SendPumpAsync(socket, outbound, token), connection, "send"),
                GuardAsync(token => ProbePumpAsync(socket, token), connection, "probe"),
                GuardAsync(token => HealthPumpAsync(token), connection, "health")
            };

            try
            {
                await Task.WhenAll(pumps).ConfigureAwait(false);
            }
            finally
            {
                Volatile.Write(ref _outbound, null);
                outbound.Writer.TryComplete();
                DrainOutbound(outbound);

                await CloseGracefullyAsync(socket).ConfigureAwait(false);
            }

            return Stopwatch.GetElapsedTime(connectedAt);
        }

        private void ConfigureSocket(ClientWebSocket socket)
        {
            /*
             * Only KeepAliveInterval is set here: it is available on every supported
             * ClientWebSocketOptions surface, whereas a keep-alive *timeout* is not.
             * The real response deadline is enforced by HealthPumpAsync, which tears
             * the socket down when no inbound frame arrives within HealthTimeout.
             */
            socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);

            // The gateway terminates TLS at the Cloudflare edge; the client still
            // validates certificates for wss:// endpoints (the default). Nothing is
            // ever accepted blindly here.
            if (_options.Headers is not null)
            {
                foreach (System.Collections.Generic.KeyValuePair<string, string> header in _options.Headers)
                {
                    socket.Options.SetRequestHeader(header.Key, header.Value);
                }
            }

            if (!string.IsNullOrWhiteSpace(_options.SubProtocol))
            {
                socket.Options.AddSubProtocol(_options.SubProtocol!);
            }
        }

        /// <summary>
        /// Runs one pump and, whatever happens to it, cancels its siblings so the
        /// whole connection unwinds promptly. Exceptions are logged, never rethrown.
        /// </summary>
        private async Task GuardAsync(Func<CancellationToken, Task> pump, CancellationTokenSource connection, string name)
        {
            try
            {
                await pump(connection.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Normal teardown path.
            }
            catch (Exception ex)
            {
                EmitLog(LogLevel.Warn, $"{name} pump stopped: {Describe(ex)}");
            }
            finally
            {
                try
                {
                    connection.Cancel();
                }
                catch (ObjectDisposedException)
                {
                    // Sibling already disposed the shared source.
                }
            }
        }

        // ---------------------------------------------------------------------
        // Pumps
        // ---------------------------------------------------------------------

        private async Task ReceivePumpAsync(ClientWebSocket socket, CancellationToken cancellationToken)
        {
            byte[] scratch = ArrayPool<byte>.Shared.Rent(Math.Max(1024, _options.ReceiveBufferSize));
            byte[]? assembled = null;
            int assembledLength = 0;

            try
            {
                while (!cancellationToken.IsCancellationRequested && socket.State == WebSocketState.Open)
                {
                    ValueWebSocketReceiveResult result;

                    try
                    {
                        result = await socket.ReceiveAsync(
                            scratch.AsMemory(0, scratch.Length),
                            cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }

                    Interlocked.Exchange(ref _lastRxTimestamp, Stopwatch.GetTimestamp());

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        EmitLog(LogLevel.Info,
                            $"server closed the session ({socket.CloseStatus}: {socket.CloseStatusDescription})");
                        return;
                    }

                    if (result.Count > 0)
                    {
                        if (assembled is null)
                        {
                            if (result.EndOfMessage)
                            {
                                // Single-fragment fast path: zero copies.
                                DispatchFrame(result.MessageType, scratch.AsMemory(0, result.Count));
                                continue;
                            }

                            assembled = ArrayPool<byte>.Shared.Rent(
                                Math.Max(result.Count * 2, _options.ReceiveBufferSize));
                            Buffer.BlockCopy(scratch, 0, assembled, 0, result.Count);
                            assembledLength = result.Count;
                        }
                        else
                        {
                            if ((long)assembledLength + result.Count > _options.MaxMessageBytes)
                            {
                                throw new InvalidOperationException(
                                    $"inbound message exceeded MaxMessageBytes ({_options.MaxMessageBytes})");
                            }

                            if (assembledLength + result.Count > assembled.Length)
                            {
                                byte[] larger = ArrayPool<byte>.Shared.Rent(
                                    Math.Min(_options.MaxMessageBytes, Math.Max(assembledLength + result.Count, assembled.Length * 2)));
                                Buffer.BlockCopy(assembled, 0, larger, 0, assembledLength);
                                ArrayPool<byte>.Shared.Return(assembled);
                                assembled = larger;
                            }

                            Buffer.BlockCopy(scratch, 0, assembled, assembledLength, result.Count);
                            assembledLength += result.Count;
                        }
                    }

                    if (result.EndOfMessage && assembled is not null)
                    {
                        DispatchFrame(result.MessageType, assembled.AsMemory(0, assembledLength));
                        ArrayPool<byte>.Shared.Return(assembled);
                        assembled = null;
                        assembledLength = 0;
                    }
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(scratch);
                if (assembled is not null)
                {
                    ArrayPool<byte>.Shared.Return(assembled);
                }
            }
        }

        private async Task SendPumpAsync(ClientWebSocket socket, Channel<OutboundFrame> outbound, CancellationToken cancellationToken)
        {
            ChannelReader<OutboundFrame> reader = outbound.Reader;

            try
            {
                while (await reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    while (reader.TryRead(out OutboundFrame? frame))
                    {
                        if (frame is null)
                        {
                            continue;
                        }

                        try
                        {
                            if (socket.State != WebSocketState.Open)
                            {
                                return;
                            }

                            await _sendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
                            try
                            {
                                await socket.SendAsync(
                                    frame.Memory,
                                    frame.MessageType,
                                    endOfMessage: true,
                                    cancellationToken).ConfigureAwait(false);
                            }
                            finally
                            {
                                _sendGate.Release();
                            }

                            Interlocked.Increment(ref _packetsSent);
                            Interlocked.Add(ref _bytesSent, frame.Length);
                        }
                        finally
                        {
                            frame.Dispose();
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Teardown.
            }
            finally
            {
                DrainOutbound(outbound);
            }
        }

        private async Task ProbePumpAsync(ClientWebSocket socket, CancellationToken cancellationToken)
        {
            if (!_options.EnableLatencyProbes)
            {
                // Keep the pump (and therefore the connection) alive; the guard
                // cancels siblings as soon as any pump returns.
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
                return;
            }

            // Reused across probes: no steady-state allocation at all.
            byte[] probe = new byte[_options.ProbeFrameSize];

            using PeriodicTimer timer = new PeriodicTimer(_options.ProbeInterval);

            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                if (socket.State != WebSocketState.Open)
                {
                    return;
                }

                int sequence = Interlocked.Increment(ref _probeSequence);
                long sentAt = Stopwatch.GetTimestamp();

                ReadOnlyMemory<byte> payload = BuildProbeFrame(probe, sequence, sentAt);

                /*
                 * The probe travels as plaintext: the gateway XORs every frame it
                 * echoes, and DispatchFrame inverts that before parsing. Sending the
                 * probe pre-transformed would therefore arrive double-transformed.
                 */
                await SendRawAsync(socket, payload, WebSocketMessageType.Binary, cancellationToken).ConfigureAwait(false);
                Interlocked.Increment(ref _probesSent);
            }
        }

        /// <summary>
        /// Serialises a probe frame into <paramref name="buffer"/>.
        ///
        /// <para>Deliberately not part of the async pump: a <see cref="Span{T}"/> is a
        /// ref struct and may not live in an async state machine (CS8652), and
        /// <c>SendAsync</c> wants a <see cref="ReadOnlyMemory{T}"/> anyway. Working on
        /// the pooled array here keeps the hot loop allocation-free.</para>
        /// </summary>
        private static ReadOnlyMemory<byte> BuildProbeFrame(byte[] buffer, int sequence, long sentAt)
        {
            Span<byte> payload = buffer.AsSpan();

            payload[0] = ProbeMagic0;
            payload[1] = ProbeMagic1;
            payload[2] = ProbeMagic2;
            payload[3] = ProbeMagic3;
            BinaryPrimitives.WriteInt32LittleEndian(payload.Slice(4, 4), sequence);
            BinaryPrimitives.WriteInt64LittleEndian(payload.Slice(8, 8), sentAt);

            // Random filler: two probes never share a ciphertext, which keeps the
            // transform honest and makes the frame useless to a passive observer.
            Random.Shared.NextBytes(payload.Slice(ProbeHeaderSize));

            return buffer;
        }

        private async Task HealthPumpAsync(CancellationToken cancellationToken)
        {
            using PeriodicTimer timer = new PeriodicTimer(TimeSpan.FromSeconds(1));

            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                long lastRx = Interlocked.Read(ref _lastRxTimestamp);
                TimeSpan idle = Stopwatch.GetElapsedTime(lastRx);

                if (idle >= _options.HealthTimeout)
                {
                    throw new TimeoutException(
                        $"no inbound frame for {idle.TotalSeconds:F1} s (HealthTimeout {_options.HealthTimeout.TotalSeconds:F0} s)");
                }
            }
        }

        // ---------------------------------------------------------------------
        // Frame handling
        // ---------------------------------------------------------------------

        private void DispatchFrame(WebSocketMessageType messageType, Memory<byte> payload)
        {
            Interlocked.Increment(ref _packetsReceived);
            Interlocked.Add(ref _bytesReceived, payload.Length);

            if (_options.DecodeInbound && payload.Length > 0)
            {
                // In-place decode on the pooled buffer: no second copy, no allocation.
                NativeCrypto.XorInPlace(payload.Span, _options.CryptoKey);
                Interlocked.Increment(ref _cryptoOperations);
                Interlocked.Add(ref _cryptoBytes, payload.Length);
            }

            if (messageType == WebSocketMessageType.Binary && TryConsumeProbeEcho(payload.Span))
            {
                return;
            }

            EventHandler<FrameReceivedEventArgs>? handler = FrameReceived;
            if (handler is null)
            {
                return;
            }

            try
            {
                handler(this, new FrameReceivedEventArgs(messageType, payload));
            }
            catch (Exception ex)
            {
                // A misbehaving subscriber must never kill the receive loop.
                EmitLog(LogLevel.Error, "frame handler threw: " + Describe(ex));
            }
        }

        private bool TryConsumeProbeEcho(ReadOnlySpan<byte> payload)
        {
            if (payload.Length < ProbeHeaderSize ||
                payload[0] != ProbeMagic0 ||
                payload[1] != ProbeMagic1 ||
                payload[2] != ProbeMagic2 ||
                payload[3] != ProbeMagic3)
            {
                return false;
            }

            int sequence = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(4, 4));
            long sentAt = BinaryPrimitives.ReadInt64LittleEndian(payload.Slice(8, 8));

            if (sentAt <= 0 || sentAt > Stopwatch.GetTimestamp())
            {
                // Not one of ours (or a stale echo from a previous session).
                return false;
            }

            double milliseconds = Stopwatch.GetElapsedTime(sentAt).TotalMilliseconds;
            Interlocked.Exchange(ref _latencyBits, BitConverter.DoubleToInt64Bits(milliseconds));
            Interlocked.Increment(ref _probesAnswered);

            EventHandler<LatencyEventArgs>? handler = LatencyUpdated;
            if (handler is not null)
            {
                try
                {
                    handler(this, new LatencyEventArgs(sequence, milliseconds));
                }
                catch (Exception ex)
                {
                    EmitLog(LogLevel.Error, "latency handler threw: " + Describe(ex));
                }
            }

            return true;
        }

        // ---------------------------------------------------------------------
        // Outbound plumbing
        // ---------------------------------------------------------------------

        /// <summary>
        /// Enqueues without ever blocking the caller. The queue is in-memory, so the
        /// returned <see cref="ValueTask{TResult}"/> is always already complete - the
        /// async shape exists purely so call sites read like I/O.
        /// </summary>
        private ValueTask<bool> EnqueueAsync(
            byte[] buffer,
            int length,
            WebSocketMessageType messageType,
            CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                ArrayPool<byte>.Shared.Return(buffer);
                return new ValueTask<bool>(Task.FromCanceled<bool>(cancellationToken));
            }

            Channel<OutboundFrame>? outbound = Volatile.Read(ref _outbound);

            if (outbound is null || _state != NetworkState.Connected)
            {
                ArrayPool<byte>.Shared.Return(buffer);
                return new ValueTask<bool>(false);
            }

            OutboundFrame frame = new OutboundFrame(buffer, length, messageType);

            if (!outbound.Writer.TryWrite(frame))
            {
                frame.Dispose();
                Interlocked.Increment(ref _droppedFrames);
                LogDrop();
                return new ValueTask<bool>(false);
            }

            return new ValueTask<bool>(true);
        }

        private async Task SendRawAsync(
            ClientWebSocket socket,
            ReadOnlyMemory<byte> payload,
            WebSocketMessageType messageType,
            CancellationToken cancellationToken)
        {
            await _sendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await socket.SendAsync(payload, messageType, endOfMessage: true, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _sendGate.Release();
            }

            Interlocked.Increment(ref _packetsSent);
            Interlocked.Add(ref _bytesSent, payload.Length);
        }

        private void DrainOutbound(Channel<OutboundFrame> outbound)
        {
            while (outbound.Reader.TryRead(out OutboundFrame? frame))
            {
                frame?.Dispose();
            }
        }

        private void LogDrop()
        {
            // Rate-limit: at most one line per second, even under a log storm.
            long now = Stopwatch.GetTimestamp();
            long previous = Interlocked.Read(ref _lastDropLog);
            if (now - previous < Stopwatch.Frequency)
            {
                return;
            }

            Interlocked.Exchange(ref _lastDropLog, now);
            EmitLog(LogLevel.Warn,
                $"outbound queue full ({_options.SendQueueCapacity} frames) - frame dropped");
        }

        // ---------------------------------------------------------------------
        // Helpers
        // ---------------------------------------------------------------------

        private async Task CloseGracefullyAsync(ClientWebSocket socket)
        {
            try
            {
                if (socket.State != WebSocketState.Open && socket.State != WebSocketState.CloseReceived)
                {
                    return;
                }

                using CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "client shutdown", timeout.Token)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is WebSocketException || ex is OperationCanceledException || ex is ObjectDisposedException)
            {
                try
                {
                    socket.Abort();
                }
                catch (ObjectDisposedException)
                {
                    // Nothing left to abort.
                }
            }
        }

        private void SetState(NetworkState state)
        {
            if (_state == state)
            {
                return;
            }

            _state = state;

            EventHandler<NetworkState>? handler = StateChanged;
            if (handler is null)
            {
                return;
            }

            try
            {
                handler(this, state);
            }
            catch (Exception ex)
            {
                EmitLog(LogLevel.Error, "state handler threw: " + Describe(ex));
            }
        }

        private void EmitLog(LogLevel level, string message)
        {
            EventHandler<EngineLogEventArgs>? handler = Log;
            if (handler is null)
            {
                return;
            }

            try
            {
                handler(this, new EngineLogEventArgs(level, message));
            }
            catch
            {
                // Diagnostics must never break the transport.
            }
        }

        private void ResetCounters()
        {
            Interlocked.Exchange(ref _packetsSent, 0);
            Interlocked.Exchange(ref _packetsReceived, 0);
            Interlocked.Exchange(ref _bytesSent, 0);
            Interlocked.Exchange(ref _bytesReceived, 0);
            Interlocked.Exchange(ref _droppedFrames, 0);
            Interlocked.Exchange(ref _cryptoOperations, 0);
            Interlocked.Exchange(ref _cryptoBytes, 0);
            Interlocked.Exchange(ref _probesSent, 0);
            Interlocked.Exchange(ref _probesAnswered, 0);
            Interlocked.Exchange(ref _latencyBits, 0);
            Interlocked.Exchange(ref _probeSequence, 0);
            Volatile.Write(ref _reconnects, 0);
        }

        private static string DescribeSubProtocol(ClientWebSocket socket) =>
            string.IsNullOrEmpty(socket.SubProtocol) ? "<none>" : socket.SubProtocol;

        private static string Describe(Exception ex)
        {
            if (ex is WebSocketException webSocket)
            {
                return $"{webSocket.WebSocketErrorCode}: {webSocket.Message}";
            }

            return ex.GetType().Name + ": " + ex.Message;
        }
    }

    /// <summary>
    /// A queued outbound frame with transfer-of-ownership semantics: the engine owns
    /// the pooled array, and <see cref="Dispose"/> is the single place it goes back
    /// to the pool - whether the frame was sent, dropped, or abandoned at teardown.
    /// </summary>
    internal sealed class OutboundFrame : IDisposable
    {
        private byte[]? _buffer;

        public OutboundFrame(byte[] buffer, int length, WebSocketMessageType messageType)
        {
            _buffer = buffer;
            Length = length;
            MessageType = messageType;
        }

        public int Length { get; }

        public WebSocketMessageType MessageType { get; }

        public ReadOnlyMemory<byte> Memory =>
            _buffer is null ? ReadOnlyMemory<byte>.Empty : new ReadOnlyMemory<byte>(_buffer, 0, Length);

        public void Dispose()
        {
            byte[]? buffer = Interlocked.Exchange(ref _buffer, null);
            if (buffer is not null)
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
    }
}
