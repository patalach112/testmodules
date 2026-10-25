using System;
using System.Buffers;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Threading;

namespace EcosystemClient
{
    /// <summary>
    /// Crypto core inspector: transform playground, native/managed parity self-test
    /// and a throughput benchmark. All heavy work runs on the thread pool.
    /// </summary>
    public sealed class CryptoViewModel : PageViewModel
    {
        private const int BenchmarkBytes = 4 * 1024 * 1024;
        private const int BenchmarkIterations = 24;

        private readonly ClientSession _session;
        private readonly LogStore _log;
        private readonly DispatcherTimer _statsTimer;

        private string _inputText = "Ecosystem transport payload — 0123456789";
        private string _cipherHex = string.Empty;
        private string _roundTripText = string.Empty;
        private string _statusText = "idle";
        private string _opsText = "0";
        private string _managedOpsText = "0";
        private string _volumeText = "0 B";
        private string _backendText = "managed fallback";
        private string _pipelineThroughputText = "--";
        private string _managedThroughputText = "--";
        private string _speedupText = "--";
        private string _selfTestText = "not run";
        private bool _isBusy;

        public CryptoViewModel(ClientSession session, LogStore log)
            : base("crypto", "Crypto Core", "asm_crypto_xor interop, parity and throughput", "M9,3 V6 M15,3 V6 M9,18 V21 M15,18 V21 M3,9 H6 M3,15 H6 M18,9 H21 M18,15 H21 M6,6 H18 V18 H6 Z M9,9 H15 V15 H9 Z")
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _log = log ?? throw new ArgumentNullException(nameof(log));

            TransformCommand = new RelayCommand(Transform);
            SwapCommand = new RelayCommand(Swap);
            SelfTestCommand = new AsyncRelayCommand(RunSelfTestAsync, onError: ReportFailure);
            BenchmarkCommand = new AsyncRelayCommand(RunBenchmarkAsync, () => !_isBusy, ReportFailure);

            _statsTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            _statsTimer.Tick += (_, _) => RefreshStats();
            _statsTimer.Start();

            Transform();
            RefreshStats();
        }

        public ClientSession Session => _session;

        public ICommand TransformCommand { get; }

        public ICommand SwapCommand { get; }

        public ICommand SelfTestCommand { get; }

        public ICommand BenchmarkCommand { get; }

        /// <summary>Two-way bound to the session key; edits take effect on reconnect.</summary>
        public string KeyText
        {
            get => _session.Settings.CryptoKey;
            set
            {
                if (_session.Settings.CryptoKey == value)
                {
                    return;
                }

                _session.Settings.CryptoKey = value;
                OnPropertiesChanged(nameof(KeyText), nameof(KeyPreview));
                Transform();
            }
        }

        public ulong ParsedKey => AppSettings.ParseKey(KeyText, NativeCrypto.DefaultKey);

        public string KeyPreview => AppSettings.FormatKey(ParsedKey);

        public string InputText
        {
            get => _inputText;
            set => SetProperty(ref _inputText, value);
        }

        public string CipherHex
        {
            get => _cipherHex;
            private set => SetProperty(ref _cipherHex, value);
        }

        public string RoundTripText
        {
            get => _roundTripText;
            private set => SetProperty(ref _roundTripText, value);
        }

        public string StatusText
        {
            get => _statusText;
            private set => SetProperty(ref _statusText, value);
        }

        public string OpsText
        {
            get => _opsText;
            private set => SetProperty(ref _opsText, value);
        }

        public string ManagedOpsText
        {
            get => _managedOpsText;
            private set => SetProperty(ref _managedOpsText, value);
        }

        public string VolumeText
        {
            get => _volumeText;
            private set => SetProperty(ref _volumeText, value);
        }

        public string BackendText
        {
            get => _backendText;
            private set => SetProperty(ref _backendText, value);
        }

        public string PipelineThroughputText
        {
            get => _pipelineThroughputText;
            private set => SetProperty(ref _pipelineThroughputText, value);
        }

        public string ManagedThroughputText
        {
            get => _managedThroughputText;
            private set => SetProperty(ref _managedThroughputText, value);
        }

        public string SpeedupText
        {
            get => _speedupText;
            private set => SetProperty(ref _speedupText, value);
        }

        public string SelfTestText
        {
            get => _selfTestText;
            private set => SetProperty(ref _selfTestText, value);
        }

        public bool NativeAvailable => NativeCrypto.IsAvailable;

        public string LibraryVersionText => NativeCrypto.IsAvailable ? "v" + NativeCrypto.LibraryVersion : "not loaded";

        public string LibraryPathText => string.IsNullOrEmpty(NativeCrypto.LibraryPath)
            ? "searching app dir, native/, runtimes/win-x64/native/"
            : NativeCrypto.LibraryPath;

        public string ArchitectureText
        {
            get
            {
                string process = Environment.Is64BitProcess ? "x64" : "x86 (native core disabled)";
                return process + " · " + NativeCrypto.ImportName;
            }
        }

        public void Dispose() => _statsTimer.Stop();

        // ---------------------------------------------------------------------

        private void Transform()
        {
            string input = _inputText ?? string.Empty;
            if (input.Length == 0)
            {
                CipherHex = string.Empty;
                RoundTripText = string.Empty;
                return;
            }

            byte[] buffer = Encoding.UTF8.GetBytes(input);
            byte[] restored = new byte[buffer.Length];

            try
            {
                CryptoBackend backend = NativeCrypto.Xor(buffer, buffer, ParsedKey);
                CryptoBackend back = NativeCrypto.Xor(buffer, restored, ParsedKey);

                CipherHex = ToHex(buffer.AsSpan());
                bool intact = restored.AsSpan().SequenceEqual(Encoding.UTF8.GetBytes(input));
                RoundTripText = intact
                    ? "round trip verified · " + backend + " → " + back
                    : "ROUND TRIP FAILED";
                StatusText = buffer.Length.ToString("N0", CultureInfo.InvariantCulture) + " bytes transformed";
            }
            catch (Exception ex)
            {
                StatusText = "transform failed: " + ex.Message;
                ReportFailure(ex);
            }
        }

        private void Swap()
        {
            // Feed the ciphertext back in as the input, so the round trip can be
            // inspected from the other direction.
            InputText = CipherHex.Replace(" ", string.Empty, StringComparison.Ordinal);
            Transform();
            StatusText = "ciphertext loaded as input";
        }

        private async Task RunSelfTestAsync()
        {
            _isBusy = true;
            BenchmarkCommand.RaiseCanExecuteChanged();
            StatusText = "running parity self-test…";

            try
            {
                (bool ok, int cases, string detail) = await Task.Run(RunParitySelfTest).ConfigureAwait(true);

                SelfTestText = ok ? $"PASS · {cases} cases" : "FAIL · " + detail;
                StatusText = ok
                    ? $"native core matches the C# reference across {cases} random cases"
                    : "parity failure: " + detail;

                _log.Write(
                    ok ? LogLevel.Success : LogLevel.Error,
                    "crypto parity self-test: " + SelfTestText,
                    "crypto");
            }
            finally
            {
                _isBusy = false;
                BenchmarkCommand.RaiseCanExecuteChanged();
            }
        }

        private static (bool Ok, int Cases, string Detail) RunParitySelfTest()
        {
            int cases = 0;

            for (int length = 0; length <= 4096; length += 337)
            {
                foreach (ulong key in new[] { 0UL, 1UL, NativeCrypto.DefaultKey, 0xDEADBEEFCAFEBABEUL })
                {
                    byte[] source = ArrayPool<byte>.Shared.Rent(Math.Max(1, length));
                    byte[] native = ArrayPool<byte>.Shared.Rent(Math.Max(1, length));
                    byte[] reference = ArrayPool<byte>.Shared.Rent(Math.Max(1, length));

                    try
                    {
                        Span<byte> input = source.AsSpan(0, length);
                        Random.Shared.NextBytes(input);

                        NativeCrypto.Xor(input, native.AsSpan(0, length), key);
                        NativeCrypto.XorManaged(input, reference.AsSpan(0, length), key);

                        if (!native.AsSpan(0, length).SequenceEqual(reference.AsSpan(0, length)))
                        {
                            return (false, cases, $"mismatch at length {length}, key 0x{key:X16}");
                        }

                        cases++;
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(source);
                        ArrayPool<byte>.Shared.Return(native);
                        ArrayPool<byte>.Shared.Return(reference);
                    }
                }
            }

            return (true, cases, "ok");
        }

        private async Task RunBenchmarkAsync()
        {
            _isBusy = true;
            BenchmarkCommand.RaiseCanExecuteChanged();
            StatusText = "benchmarking 4 MiB × 24 iterations…";

            try
            {
                ulong key = ParsedKey;
                (double pipeline, double managed) = await Task.Run(() => Measure(key)).ConfigureAwait(true);

                PipelineThroughputText = FormatThroughput(pipeline);
                ManagedThroughputText = FormatThroughput(managed);
                SpeedupText = managed > 0d && pipeline > 0d
                    ? (pipeline / managed).ToString("0.00", CultureInfo.InvariantCulture) + "×"
                    : "--";
                StatusText = "benchmark complete";

                _log.Write(LogLevel.Info,
                    $"crypto benchmark · pipeline {PipelineThroughputText} · managed {ManagedThroughputText} · {SpeedupText}",
                    "crypto");
            }
            finally
            {
                _isBusy = false;
                BenchmarkCommand.RaiseCanExecuteChanged();
                RefreshStats();
            }
        }

        private static (double Pipeline, double Managed) Measure(ulong key)
        {
            byte[] buffer = ArrayPool<byte>.Shared.Rent(BenchmarkBytes);

            try
            {
                Span<byte> span = buffer.AsSpan(0, BenchmarkBytes);
                Random.Shared.NextBytes(span);

                // Warm up both paths so JIT and page-in costs stay out of the numbers.
                NativeCrypto.XorInPlace(span, key);
                NativeCrypto.XorManaged(span, span, key);

                Stopwatch sw = Stopwatch.StartNew();
                for (int i = 0; i < BenchmarkIterations; i++)
                {
                    NativeCrypto.XorInPlace(span, key);
                }

                sw.Stop();
                double pipeline = MegabytesPerSecond(BenchmarkBytes, BenchmarkIterations, sw.Elapsed.TotalSeconds);

                sw.Restart();
                for (int i = 0; i < BenchmarkIterations; i++)
                {
                    NativeCrypto.XorManaged(span, span, key);
                }

                sw.Stop();
                double managed = MegabytesPerSecond(BenchmarkBytes, BenchmarkIterations, sw.Elapsed.TotalSeconds);

                return (pipeline, managed);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        private void RefreshStats()
        {
            CryptoStatistics stats = NativeCrypto.GetStatistics();

            OpsText = stats.NativeOperations.ToString("N0", CultureInfo.InvariantCulture);
            ManagedOpsText = stats.ManagedOperations.ToString("N0", CultureInfo.InvariantCulture);
            VolumeText = DashboardViewModel.FormatBytes(stats.BytesTransformed);
            BackendText = stats.NativeAvailable
                ? "native asm core v" + stats.LibraryVersion
                : "managed fallback";

            OnPropertiesChanged(nameof(NativeAvailable), nameof(LibraryVersionText), nameof(LibraryPathText), nameof(KeyPreview));
        }

        private void ReportFailure(Exception ex)
        {
            StatusText = ex.GetType().Name + ": " + ex.Message;
            _log.Error("crypto page: " + StatusText, "crypto");
        }

        private static double MegabytesPerSecond(int bytes, int iterations, double seconds) =>
            seconds <= 0d ? 0d : bytes * (double)iterations / (1024d * 1024d) / seconds;

        private static string FormatThroughput(double megabytesPerSecond) =>
            megabytesPerSecond <= 0d
                ? "--"
                : megabytesPerSecond.ToString("N1", CultureInfo.InvariantCulture) + " MB/s";

        private static string ToHex(ReadOnlySpan<byte> bytes)
        {
            if (bytes.IsEmpty)
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder(bytes.Length * 3);
            for (int i = 0; i < bytes.Length; i++)
            {
                if (i > 0)
                {
                    builder.Append(' ');
                }

                builder.Append(bytes[i].ToString("X2", CultureInfo.InvariantCulture));
            }

            return builder.ToString();
        }
    }
}
