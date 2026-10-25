using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows.Threading;

namespace EcosystemClient
{
    /// <summary>
    /// Live telemetry for the main dashboard.
    ///
    /// <para>A single dispatcher timer samples the engine's lock-free counters four
    /// times a second. Those reads are plain <see cref="Interlocked"/> loads, so
    /// sampling costs microseconds and never contends with the transport threads.
    /// Rates are deltas between samples; sparkline histories are bounded ring buffers
    /// so memory stays flat no matter how long the client runs.</para>
    /// </summary>
    public sealed class DashboardViewModel : PageViewModel, IDisposable
    {
        private const int HistoryPoints = 96;
        private const double LatencyCeilingMs = 250d;
        private const double PacketsPerSecondCeiling = 500d;
        private const double CryptoPerSecondCeiling = 600d;
        private const double ThroughputCeilingBytesPerSecond = 2d * 1024d * 1024d;

        private readonly ClientSession _session;
        private readonly LogStore _log;
        private readonly DispatcherTimer _timer;

        private long _lastSampleTimestamp;
        private long _lastPacketsSent;
        private long _lastPacketsReceived;
        private long _lastBytesSent;
        private long _lastBytesReceived;
        private long _lastCryptoOperations;

        private string _latencyText = "--";
        private string _latencyQuality = "IDLE";
        private string _sentRateText = "0";
        private string _receivedRateText = "0";
        private string _packetsTotalText = "0 / 0";
        private string _throughputInText = "0 B/s";
        private string _throughputOutText = "0 B/s";
        private string _throughputTotalText = "0 B";
        private string _cryptoRateText = "0";
        private string _cryptoBackendText = "managed";
        private string _cryptoVolumeText = "0 B";
        private string _uptimeText = "00:00:00";
        private string _reconnectsText = "0";
        private string _droppedText = "0";
        private string _probeLossText = "0.0 %";
        private string _latencyPeakText = "--";
        private string _throughputPeakText = "0 B/s";
        private string _stateLabel = "OFFLINE";
        private string _stateDetail = "Transport idle";
        private string _endpointText = AppSettings.DefaultEndpoint;
        private string _sessionClockText = "--:--:--";
        private double _latencyGauge;
        private double _packetGauge;
        private double _cryptoGauge;
        private double _throughputGauge;
        private LogEntry? _newestEvent;

        public DashboardViewModel(ClientSession session, LogStore log)
            : base("dashboard", "Command Deck", "Live transport, crypto and latency telemetry", "M4,4 H10 V10 H4 Z M14,4 H20 V10 H14 Z M4,14 H10 V20 H4 Z M14,14 H20 V20 H14 Z")
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _log = log ?? throw new ArgumentNullException(nameof(log));

            RecentEvents = new ObservableCollection<LogEntry>();

            _timer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(250)
            };
            _timer.Tick += (_, _) => Refresh();

            _session.StateChanged += OnSessionStateChanged;
            ApplyState();
            Refresh();
            _timer.Start();
        }

        public ClientSession Session => _session;

        public ObservableCollection<double> LatencyHistory { get; } = new ObservableCollection<double>();

        public ObservableCollection<double> ThroughputInHistory { get; } = new ObservableCollection<double>();

        public ObservableCollection<double> ThroughputOutHistory { get; } = new ObservableCollection<double>();

        public ObservableCollection<double> CryptoHistory { get; } = new ObservableCollection<double>();

        public ObservableCollection<LogEntry> RecentEvents { get; }

        // ---- status -------------------------------------------------------

        public string StateLabel
        {
            get => _stateLabel;
            private set => SetProperty(ref _stateLabel, value);
        }

        public string StateDetail
        {
            get => _stateDetail;
            private set => SetProperty(ref _stateDetail, value);
        }

        public string EndpointText
        {
            get => _endpointText;
            private set => SetProperty(ref _endpointText, value);
        }

        public string SessionClockText
        {
            get => _sessionClockText;
            private set => SetProperty(ref _sessionClockText, value);
        }

        public string UptimeText
        {
            get => _uptimeText;
            private set => SetProperty(ref _uptimeText, value);
        }

        // ---- latency ------------------------------------------------------

        public string LatencyText
        {
            get => _latencyText;
            private set => SetProperty(ref _latencyText, value);
        }

        public string LatencyQuality
        {
            get => _latencyQuality;
            private set => SetProperty(ref _latencyQuality, value);
        }

        public string LatencyPeakText
        {
            get => _latencyPeakText;
            private set => SetProperty(ref _latencyPeakText, value);
        }

        public string ProbeLossText
        {
            get => _probeLossText;
            private set => SetProperty(ref _probeLossText, value);
        }

        public double LatencyGauge
        {
            get => _latencyGauge;
            private set => SetProperty(ref _latencyGauge, value);
        }

        // ---- packets ------------------------------------------------------

        public string SentRateText
        {
            get => _sentRateText;
            private set => SetProperty(ref _sentRateText, value);
        }

        public string ReceivedRateText
        {
            get => _receivedRateText;
            private set => SetProperty(ref _receivedRateText, value);
        }

        public string PacketsTotalText
        {
            get => _packetsTotalText;
            private set => SetProperty(ref _packetsTotalText, value);
        }

        public double PacketGauge
        {
            get => _packetGauge;
            private set => SetProperty(ref _packetGauge, value);
        }

        public string DroppedText
        {
            get => _droppedText;
            private set => SetProperty(ref _droppedText, value);
        }

        public string ReconnectsText
        {
            get => _reconnectsText;
            private set => SetProperty(ref _reconnectsText, value);
        }

        // ---- throughput ---------------------------------------------------

        public string ThroughputInText
        {
            get => _throughputInText;
            private set => SetProperty(ref _throughputInText, value);
        }

        public string ThroughputOutText
        {
            get => _throughputOutText;
            private set => SetProperty(ref _throughputOutText, value);
        }

        public string ThroughputTotalText
        {
            get => _throughputTotalText;
            private set => SetProperty(ref _throughputTotalText, value);
        }

        public string ThroughputPeakText
        {
            get => _throughputPeakText;
            private set => SetProperty(ref _throughputPeakText, value);
        }

        public double ThroughputGauge
        {
            get => _throughputGauge;
            private set => SetProperty(ref _throughputGauge, value);
        }

        // ---- crypto -------------------------------------------------------

        public string CryptoRateText
        {
            get => _cryptoRateText;
            private set => SetProperty(ref _cryptoRateText, value);
        }

        public string CryptoBackendText
        {
            get => _cryptoBackendText;
            private set => SetProperty(ref _cryptoBackendText, value);
        }

        public string CryptoVolumeText
        {
            get => _cryptoVolumeText;
            private set => SetProperty(ref _cryptoVolumeText, value);
        }

        public double CryptoGauge
        {
            get => _cryptoGauge;
            private set => SetProperty(ref _cryptoGauge, value);
        }

        public override void OnActivated()
        {
            _timer.Interval = TimeSpan.FromMilliseconds(250);
            _timer.Start();
        }

        public override void OnDeactivated()
        {
            // Keep sampling so the sparklines stay continuous when the user returns,
            // but drop to a lazy cadence that costs (almost) nothing.
            _timer.Interval = TimeSpan.FromMilliseconds(750);
        }

        public void Dispose()
        {
            _timer.Stop();
            _session.StateChanged -= OnSessionStateChanged;
        }

        // ---------------------------------------------------------------------
        // Sampling
        // ---------------------------------------------------------------------

        private void Refresh()
        {
            NetworkCounters counters = _session.Counters;
            long now = Stopwatch.GetTimestamp();

            double seconds = _lastSampleTimestamp == 0
                ? 0d
                : (now - _lastSampleTimestamp) / (double)Stopwatch.Frequency;

            double sentRate = 0d;
            double receivedRate = 0d;
            double bytesInRate = 0d;
            double bytesOutRate = 0d;
            double cryptoRate = 0d;

            if (seconds > 0.001d)
            {
                sentRate = Math.Max(0d, (counters.PacketsSent - _lastPacketsSent) / seconds);
                receivedRate = Math.Max(0d, (counters.PacketsReceived - _lastPacketsReceived) / seconds);
                bytesInRate = Math.Max(0d, (counters.BytesReceived - _lastBytesReceived) / seconds);
                bytesOutRate = Math.Max(0d, (counters.BytesSent - _lastBytesSent) / seconds);
                cryptoRate = Math.Max(0d, (counters.CryptoOperations - _lastCryptoOperations) / seconds);
            }

            _lastSampleTimestamp = now;
            _lastPacketsSent = counters.PacketsSent;
            _lastPacketsReceived = counters.PacketsReceived;
            _lastBytesSent = counters.BytesSent;
            _lastBytesReceived = counters.BytesReceived;
            _lastCryptoOperations = counters.CryptoOperations;

            // Latency
            double latency = counters.LatencyMilliseconds;
            bool hasLatency = latency > 0d;

            LatencyText = hasLatency ? latency.ToString("0.0", CultureInfo.InvariantCulture) : "--";
            LatencyQuality = !hasLatency
                ? (_session.IsConnected ? "METERING" : "IDLE")
                : latency < 60d ? "EXCELLENT" : latency < 140d ? "STABLE" : latency < 260d ? "ELEVATED" : "DEGRADED";
            LatencyGauge = hasLatency ? Math.Clamp(latency / LatencyCeilingMs, 0d, 1d) : 0d;
            ProbeLossText = counters.ProbesSent == 0
                ? "0.0 %"
                : counters.ProbeLossPercent.ToString("0.0", CultureInfo.InvariantCulture) + " %";

            // Packets
            SentRateText = sentRate.ToString("0", CultureInfo.InvariantCulture);
            ReceivedRateText = receivedRate.ToString("0", CultureInfo.InvariantCulture);
            PacketsTotalText = counters.PacketsSent.ToString("N0", CultureInfo.InvariantCulture) + " / " +
                               counters.PacketsReceived.ToString("N0", CultureInfo.InvariantCulture);
            PacketGauge = Math.Clamp((sentRate + receivedRate) / PacketsPerSecondCeiling, 0d, 1d);
            DroppedText = counters.DroppedFrames.ToString("N0", CultureInfo.InvariantCulture);
            ReconnectsText = counters.Reconnects.ToString("N0", CultureInfo.InvariantCulture);

            // Throughput
            ThroughputInText = FormatRate(bytesInRate);
            ThroughputOutText = FormatRate(bytesOutRate);
            ThroughputTotalText = FormatBytes(counters.BytesSent + counters.BytesReceived);
            ThroughputGauge = Math.Clamp((bytesInRate + bytesOutRate) / ThroughputCeilingBytesPerSecond, 0d, 1d);

            // Crypto
            CryptoRateText = cryptoRate.ToString("0", CultureInfo.InvariantCulture);
            CryptoGauge = Math.Clamp(cryptoRate / CryptoPerSecondCeiling, 0d, 1d);

            CryptoStatistics cryptoStats = NativeCrypto.GetStatistics();
            CryptoBackendText = cryptoStats.NativeAvailable ? "asm core" : "managed";
            CryptoVolumeText = FormatBytes(Math.Max(cryptoStats.BytesTransformed, counters.CryptoBytes));

            // Session
            TimeSpan uptime = counters.Uptime;
            UptimeText = uptime.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);
            SessionClockText = _session.IsConnected ? "LIVE" : "--:--:--";

            PushHistory(LatencyHistory, hasLatency ? latency : 0d);
            PushHistory(ThroughputInHistory, bytesInRate);
            PushHistory(ThroughputOutHistory, bytesOutRate);
            PushHistory(CryptoHistory, cryptoRate);

            LatencyPeakText = "peak " + Peak(LatencyHistory).ToString("0.0", CultureInfo.InvariantCulture) + " ms";
            ThroughputPeakText = "peak " + FormatRate(Peak(ThroughputInHistory));

            RefreshEvents();
        }

        private void RefreshEvents()
        {
            ObservableCollection<LogEntry> entries = _log.Entries;
            if (entries.Count == 0)
            {
                if (RecentEvents.Count > 0)
                {
                    RecentEvents.Clear();
                    _newestEvent = null;
                }

                return;
            }

            LogEntry newest = entries[entries.Count - 1];
            if (ReferenceEquals(newest, _newestEvent))
            {
                return;
            }

            _newestEvent = newest;

            int take = Math.Min(6, entries.Count);
            RecentEvents.Clear();
            for (int i = entries.Count - take; i < entries.Count; i++)
            {
                RecentEvents.Add(entries[i]);
            }
        }

        private void ApplyState()
        {
            StateLabel = _session.StateLabel;
            StateDetail = _session.StateDetail;
            EndpointText = _session.Settings.Endpoint;
        }

        private void OnSessionStateChanged(object? sender, NetworkState state)
        {
            // Session events are already raised on the dispatcher thread by ClientSession.
            if (System.Windows.Application.Current?.Dispatcher.CheckAccess() == false)
            {
                System.Windows.Application.Current.Dispatcher.InvokeAsync(ApplyState);
                return;
            }

            ApplyState();
        }

        private static void PushHistory(ObservableCollection<double> history, double value)
        {
            history.Add(value);
            while (history.Count > HistoryPoints)
            {
                history.RemoveAt(0);
            }
        }

        private static double Peak(ObservableCollection<double> values)
        {
            double peak = 0d;
            for (int i = 0; i < values.Count; i++)
            {
                if (values[i] > peak)
                {
                    peak = values[i];
                }
            }

            return peak;
        }

        internal static string FormatRate(double bytesPerSecond) => FormatBytes(bytesPerSecond) + "/s";

        internal static string FormatBytes(double bytes)
        {
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double value = Math.Max(0d, bytes);
            int unit = 0;

            while (value >= 1024d && unit < units.Length - 1)
            {
                value /= 1024d;
                unit++;
            }

            string format = unit == 0 ? "0" : "0.0";
            return value.ToString(format, CultureInfo.InvariantCulture) + " " + units[unit];
        }
    }
}
