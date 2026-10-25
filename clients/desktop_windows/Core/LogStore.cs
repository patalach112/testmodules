using System;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading;
using System.Windows.Threading;

namespace EcosystemClient
{
    /// <summary>Severity/kind of a log line. Drives colour and filtering in the terminal view.</summary>
    public enum LogLevel
    {
        Trace = 0,
        Debug = 1,
        Info = 2,
        Success = 3,
        Inbound = 4,
        Outbound = 5,
        Crypto = 6,
        Warn = 7,
        Error = 8
    }

    /// <summary>One immutable terminal line.</summary>
    public sealed class LogEntry
    {
        public LogEntry(DateTimeOffset timestamp, LogLevel level, string category, string message)
        {
            Timestamp = timestamp;
            Level = level;
            Category = category ?? string.Empty;
            Message = message ?? string.Empty;
        }

        public DateTimeOffset Timestamp { get; }

        public LogLevel Level { get; }

        public string Category { get; }

        public string Message { get; }

        /// <summary>Fixed-width wall clock, e.g. <c>14:03:22.481</c>.</summary>
        public string TimeText => Timestamp.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);

        /// <summary>Four-character tag rendered in the level gutter.</summary>
        public string LevelTag => Level switch
        {
            LogLevel.Trace => "TRCE",
            LogLevel.Debug => "DBUG",
            LogLevel.Info => "INFO",
            LogLevel.Success => "OKAY",
            LogLevel.Inbound => "<<<",
            LogLevel.Outbound => ">>>",
            LogLevel.Crypto => "CRYP",
            LogLevel.Warn => "WARN",
            LogLevel.Error => "FAIL",
            _ => "LOG"
        };

        public string CategoryText => string.IsNullOrEmpty(Category) ? "core" : Category;
    }

    /// <summary>
    /// Thread-safe log sink with a bounded, UI-friendly facade.
    ///
    /// <para>Writers (network pumps, crypto workers, commands) enqueue into a
    /// <see cref="ConcurrentQueue{T}"/> and return immediately - no dispatcher
    /// marshalling, no lock contention, no chance of stalling the transport behind a
    /// busy UI. A <see cref="DispatcherTimer"/> drains the queue in batches on the UI
    /// thread, which keeps the observable collection consistent and lets WPF bind to
    /// it directly.</para>
    /// </summary>
    public sealed class LogStore : ObservableObject
    {
        /// <summary>Lines kept in memory; older lines are trimmed from the front.</summary>
        public const int MaxEntries = 2500;

        private readonly ConcurrentQueue<LogEntry> _pending = new ConcurrentQueue<LogEntry>();
        private readonly DispatcherTimer _drainTimer;
        private long _written;
        private long _dropped;
        private bool _isPaused;

        public LogStore()
        {
            Entries = new ObservableCollection<LogEntry>();

            _drainTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(120)
            };
            _drainTimer.Tick += (_, _) => Drain();
            _drainTimer.Start();
        }

        public ObservableCollection<LogEntry> Entries { get; }

        /// <summary>Total lines ever accepted, including ones already trimmed away.</summary>
        public long Written => Interlocked.Read(ref _written);

        /// <summary>True when the terminal view is frozen (new lines are still counted).</summary>
        public bool IsPaused
        {
            get => _isPaused;
            set => SetProperty(ref _isPaused, value);
        }

        /// <summary>Writes a line. Safe from any thread, including unmanaged callbacks.</summary>
        public void Write(LogLevel level, string message, string category = "core")
        {
            Interlocked.Increment(ref _written);
            _pending.Enqueue(new LogEntry(DateTimeOffset.Now, level, category, message));
        }

        public void Info(string message, string category = "core") => Write(LogLevel.Info, message, category);

        public void Warn(string message, string category = "core") => Write(LogLevel.Warn, message, category);

        public void Error(string message, string category = "core") => Write(LogLevel.Error, message, category);

        public void Success(string message, string category = "core") => Write(LogLevel.Success, message, category);

        /// <summary>Drains everything queued so far onto the UI collection. UI thread only.</summary>
        public void Drain()
        {
            if (IsPaused)
            {
                return;
            }

            int batch = 0;
            while (batch < 400 && _pending.TryDequeue(out LogEntry? entry))
            {
                Entries.Add(entry);
                batch++;
            }

            if (Entries.Count > MaxEntries)
            {
                int excess = Entries.Count - MaxEntries;
                for (int i = 0; i < excess; i++)
                {
                    Entries.RemoveAt(0);
                }

                Interlocked.Add(ref _dropped, excess);
            }
        }

        /// <summary>Clears the view (and anything still queued). UI thread only.</summary>
        public void Clear()
        {
            while (_pending.TryDequeue(out _))
            {
                // Drop the backlog too - "clear" should mean clear.
            }

            Entries.Clear();
            Interlocked.Exchange(ref _dropped, 0);
        }
    }
}
