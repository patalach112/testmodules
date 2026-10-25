using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace EcosystemClient
{
    /// <summary>
    /// The single place that owns connectivity. View models talk to this object,
    /// never to <see cref="NetworkEngine"/> directly, so the UI has exactly one
    /// source of truth for state, counters and configuration.
    ///
    /// <para><b>Threading.</b> Public state is always mutated on the dispatcher
    /// thread; engine events arrive on whatever thread the transport used and are
    /// marshalled here. <see cref="LogStore.Write"/> is thread-safe, so diagnostics
    /// skip the hop entirely.</para>
    /// </summary>
    public sealed class ClientSession : ObservableObject, IAsyncDisposable
    {
        private readonly LogStore _log;
        private NetworkEngine? _engine;
        private NetworkState _state = NetworkState.Disconnected;

        public ClientSession(LogStore log, AppSettings settings)
        {
            _log = log ?? throw new ArgumentNullException(nameof(log));
            Settings = settings ?? throw new ArgumentNullException(nameof(settings));

            ConnectCommand = new AsyncRelayCommand(ConnectAsync, () => !IsBusy && !IsConnected, OnCommandFailure);
            DisconnectCommand = new AsyncRelayCommand(DisconnectAsync, () => IsConnected || State == NetworkState.Connecting, OnCommandFailure);
            ReconnectCommand = new AsyncRelayCommand(ReconnectAsync, () => !IsBusy, OnCommandFailure);
        }

        public AppSettings Settings { get; }

        public AsyncRelayCommand ConnectCommand { get; }

        public AsyncRelayCommand DisconnectCommand { get; }

        public AsyncRelayCommand ReconnectCommand { get; }

        /// <summary>Transform key currently in effect.</summary>
        public ulong CryptoKey => AppSettings.ParseKey(Settings.CryptoKey, NativeCrypto.DefaultKey);

        public NetworkState State
        {
            get => _state;
            private set
            {
                if (!SetProperty(ref _state, value))
                {
                    return;
                }

                OnPropertiesChanged(nameof(IsConnected), nameof(IsBusy), nameof(StateLabel), nameof(StateDetail));
                ConnectCommand.RaiseCanExecuteChanged();
                DisconnectCommand.RaiseCanExecuteChanged();
                ReconnectCommand.RaiseCanExecuteChanged();
                StateChanged?.Invoke(this, value);
            }
        }

        /// <summary>Raised on the dispatcher thread after <see cref="State"/> changed.</summary>
        public event EventHandler<NetworkState>? StateChanged;

        public bool IsConnected => State == NetworkState.Connected;

        public bool IsBusy => State is NetworkState.Connecting or NetworkState.Reconnecting or NetworkState.Stopping;

        public string StateLabel => State switch
        {
            NetworkState.Connected => "ONLINE",
            NetworkState.Connecting => "LINKING",
            NetworkState.Reconnecting => "RECOVERING",
            NetworkState.Stopping => "CLOSING",
            _ => "OFFLINE"
        };

        public string StateDetail => State switch
        {
            NetworkState.Connected => "Secure WebSocket session is live",
            NetworkState.Connecting => "Negotiating the WebSocket handshake",
            NetworkState.Reconnecting => "Transport lost - backoff in progress",
            NetworkState.Stopping => "Closing the socket gracefully",
            _ => "Transport idle"
        };

        /// <summary>Live counter snapshot. Safe to call from the dispatcher on a timer.</summary>
        public NetworkCounters Counters => _engine?.GetCounters() ?? default;

        public async Task ConnectAsync()
        {
            if (IsConnected || State == NetworkState.Connecting)
            {
                return;
            }

            if (!AppSettings.TryParseEndpoint(Settings.Endpoint, out Uri endpoint, out string error))
            {
                _log.Error(error, "session");
                State = NetworkState.Disconnected;
                return;
            }

            NetworkEngine engine = new NetworkEngine(BuildOptions(endpoint));
            engine.Log += OnEngineLog;
            engine.StateChanged += OnEngineStateChanged;
            engine.FrameReceived += OnFrameReceived;
            engine.LatencyUpdated += OnLatencyUpdated;

            NetworkEngine? previous = Interlocked.Exchange(ref _engine, engine);
            if (previous is not null)
            {
                Detach(previous);
                await previous.DisposeAsync().ConfigureAwait(true);
            }

            State = NetworkState.Connecting;
            _log.Info($"opening transport to {endpoint.AbsoluteUri}", "session");
            _log.Info($"crypto key {AppSettings.FormatKey(CryptoKey)} - native core {(NativeCrypto.IsAvailable ? "active" : "unavailable (managed fallback)")}", "session");

            await engine.StartAsync().ConfigureAwait(true);
        }

        public async Task DisconnectAsync()
        {
            NetworkEngine? engine = Interlocked.Exchange(ref _engine, null);
            if (engine is null)
            {
                State = NetworkState.Disconnected;
                return;
            }

            Detach(engine);
            State = NetworkState.Stopping;
            _log.Info("closing transport", "session");
            await engine.DisposeAsync().ConfigureAwait(true);
            State = NetworkState.Disconnected;
            _log.Info("transport closed", "session");
        }

        public async Task ReconnectAsync()
        {
            _log.Info("manual reconnect requested", "session");
            await DisconnectAsync().ConfigureAwait(true);
            await ConnectAsync().ConfigureAwait(true);
        }

        /// <summary>Persists the current settings and restarts the transport if needed.</summary>
        public async Task ApplySettingsAsync()
        {
            Settings.Normalise();
            Settings.Save();
            _log.Success($"settings saved to {AppSettings.SettingsPath}", "session");

            if (IsConnected || State == NetworkState.Connecting)
            {
                await ReconnectAsync().ConfigureAwait(true);
            }
        }

        public async Task<bool> SendTextAsync(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            NetworkEngine? engine = Volatile.Read(ref _engine);
            if (engine is null || !engine.IsConnected)
            {
                _log.Warn("send rejected: transport is not connected", "tx");
                return false;
            }

            bool queued = await engine.SendTextAsync(text).ConfigureAwait(true);
            if (queued)
            {
                _log.Write(LogLevel.Outbound, text, "tx");
            }
            else
            {
                _log.Warn("send rejected: outbound queue is saturated", "tx");
            }

            return queued;
        }

        public async ValueTask DisposeAsync()
        {
            NetworkEngine? engine = Interlocked.Exchange(ref _engine, null);
            if (engine is not null)
            {
                Detach(engine);
                await engine.DisposeAsync().ConfigureAwait(false);
            }
        }

        // ---------------------------------------------------------------------
        // Engine wiring
        // ---------------------------------------------------------------------

        private NetworkOptions BuildOptions(Uri endpoint) => new NetworkOptions
        {
            Endpoint = endpoint,
            CryptoKey = CryptoKey,
            DecodeInbound = Settings.DecodeInbound,
            EnableLatencyProbes = Settings.EnableLatencyProbes,
            ReceiveBufferSize = Math.Clamp(Settings.ReceiveBufferKb, 1, 1024) * 1024,
            HealthTimeout = TimeSpan.FromSeconds(Math.Clamp(Settings.HealthTimeoutSeconds, 3, 300)),
            Headers = null,
            SubProtocol = null
        };

        private void Detach(NetworkEngine engine)
        {
            engine.Log -= OnEngineLog;
            engine.StateChanged -= OnEngineStateChanged;
            engine.FrameReceived -= OnFrameReceived;
            engine.LatencyUpdated -= OnLatencyUpdated;
        }

        private void OnEngineStateChanged(object? sender, NetworkState state) =>
            RunOnUi(() => ApplyEngineState(sender as NetworkEngine, state));

        private void ApplyEngineState(NetworkEngine? source, NetworkState state)
        {
            // Ignore stray events from an engine that has already been replaced.
            if (source is not null && !ReferenceEquals(source, Volatile.Read(ref _engine)))
            {
                return;
            }

            State = state;
        }

        private void OnEngineLog(object? sender, EngineLogEventArgs e) =>
            _log.Write(e.Level, e.Message, "transport");

        private void OnLatencyUpdated(object? sender, LatencyEventArgs e) =>
            _log.Write(LogLevel.Trace, $"probe #{e.Sequence} round trip {e.Milliseconds:F2} ms", "probe");

        private void OnFrameReceived(object? sender, FrameReceivedEventArgs e)
        {
            if (e.MessageType == WebSocketMessageType.Text)
            {
                string text = e.AsText();
                _log.Write(LogLevel.Inbound, text, "rx");
                return;
            }

            // Binary payloads (that are not probe echoes, which the engine swallows)
            // get a compact hex preview so the terminal stays readable.
            _log.Write(LogLevel.Inbound, DescribeBinary(e.Payload.Span) + $"  [{e.Payload.Length} B]", "rx");
        }

        private void OnCommandFailure(Exception ex) =>
            _log.Error(ex.GetType().Name + ": " + ex.Message, "session");

        private static string DescribeBinary(ReadOnlySpan<byte> payload)
        {
            const int Preview = 48;
            int count = Math.Min(Preview, payload.Length);
            StringBuilder builder = new StringBuilder(count * 3 + 4);

            for (int i = 0; i < count; i++)
            {
                if (i > 0)
                {
                    builder.Append(' ');
                }

                builder.Append(payload[i].ToString("X2"));
            }

            if (payload.Length > Preview)
            {
                builder.Append(" ...");
            }

            return "0x " + builder;
        }

        private static void RunOnUi(Action action)
        {
            Dispatcher? dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is null || dispatcher.CheckAccess())
            {
                action();
                return;
            }

            dispatcher.InvokeAsync(action, DispatcherPriority.Background);
        }
    }
}
