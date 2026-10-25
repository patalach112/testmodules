using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Text;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;

namespace EcosystemClient
{
    /// <summary>
    /// The live wire terminal: a filtered <see cref="ICollectionView"/> over the log
    /// store, plus a command line that pushes frames straight into the transport.
    /// </summary>
    public sealed class TerminalViewModel : PageViewModel
    {
        private readonly ClientSession _session;
        private readonly LogStore _log;
        private readonly DispatcherTimer _hintTimer;

        private string _filterText = string.Empty;
        private string _inputText = string.Empty;
        private bool _showSystem = true;
        private bool _showTraffic = true;
        private bool _showDiagnostics;
        private bool _autoScroll = true;
        private string _statusText = "ready";
        private string _counterText = "0 lines";
        private bool _isSending;

        public TerminalViewModel(ClientSession session, LogStore log)
            : base("terminal", "Wire Terminal", "Raw WebSocket traffic and engine diagnostics", "M4,5 H20 V19 H4 Z M7,10 L10,12.5 L7,15 M12,15 H17")
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _log = log ?? throw new ArgumentNullException(nameof(log));

            EntriesView = CollectionViewSource.GetDefaultView(log.Entries);
            EntriesView.Filter = FilterEntry;

            _log.Entries.CollectionChanged += OnEntriesChanged;

            SendCommand = new AsyncRelayCommand(SendAsync, () => !_isSending && !string.IsNullOrWhiteSpace(InputText));
            ClearCommand = new RelayCommand(() =>
            {
                _log.Clear();
                UpdateCounters();
                StatusText = "buffer cleared";
            });
            TogglePauseCommand = new RelayCommand(() =>
            {
                _log.IsPaused = !_log.IsPaused;
                StatusText = _log.IsPaused ? "capture paused" : "capture resumed";
            });
            CopyCommand = new RelayCommand(CopyVisible);
            SendPingCommand = new AsyncRelayCommand(SendPingAsync);

            _hintTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(400)
            };
            _hintTimer.Tick += (_, _) =>
            {
                _hintTimer.Stop();
                UpdateHint();
            };

            UpdateCounters();
            UpdateHint();
        }

        public ClientSession Session => _session;

        public ICollectionView EntriesView { get; }

        /// <summary>Raw backing collection, used by the view to follow the tail.</summary>
        public System.Collections.ObjectModel.ObservableCollection<LogEntry> Entries => _log.Entries;

        public ICommand SendCommand { get; }

        public ICommand ClearCommand { get; }

        public ICommand TogglePauseCommand { get; }

        public ICommand CopyCommand { get; }

        public ICommand SendPingCommand { get; }

        public string FilterText
        {
            get => _filterText;
            set
            {
                if (SetProperty(ref _filterText, value))
                {
                    EntriesView.Refresh();
                    UpdateCounters();
                }
            }
        }

        public string InputText
        {
            get => _inputText;
            set
            {
                if (SetProperty(ref _inputText, value))
                {
                    OnPropertyChanged(nameof(SendHint));
                }
            }
        }

        public bool ShowSystem
        {
            get => _showSystem;
            set
            {
                if (SetProperty(ref _showSystem, value))
                {
                    EntriesView.Refresh();
                    UpdateCounters();
                }
            }
        }

        public bool ShowTraffic
        {
            get => _showTraffic;
            set
            {
                if (SetProperty(ref _showTraffic, value))
                {
                    EntriesView.Refresh();
                    UpdateCounters();
                }
            }
        }

        public bool ShowDiagnostics
        {
            get => _showDiagnostics;
            set
            {
                if (SetProperty(ref _showDiagnostics, value))
                {
                    EntriesView.Refresh();
                    UpdateCounters();
                }
            }
        }

        public bool AutoScroll
        {
            get => _autoScroll;
            set => SetProperty(ref _autoScroll, value);
        }

        public bool IsPaused => _log.IsPaused;

        public string StatusText
        {
            get => _statusText;
            private set => SetProperty(ref _statusText, value);
        }

        public string CounterText
        {
            get => _counterText;
            private set => SetProperty(ref _counterText, value);
        }

        public string SendHint => _session.IsConnected
            ? "enter sends a text frame · the gateway echoes it XOR-transformed"
            : "offline · frames can only be sent while the transport is connected";

        public override void OnActivated()
        {
            UpdateHint();
            UpdateCounters();
        }

        public void Dispose()
        {
            _hintTimer.Stop();
            _log.Entries.CollectionChanged -= OnEntriesChanged;
        }

        // ---------------------------------------------------------------------

        private async System.Threading.Tasks.Task SendAsync()
        {
            string payload = InputText;
            if (string.IsNullOrWhiteSpace(payload))
            {
                return;
            }

            _isSending = true;
            SendCommand.RaiseCanExecuteChanged();

            try
            {
                bool sent = await _session.SendTextAsync(payload).ConfigureAwait(true);
                if (sent)
                {
                    InputText = string.Empty;
                    StatusText = $"sent {payload.Length} chars";
                }
                else
                {
                    StatusText = "send rejected - transport offline or queue full";
                }
            }
            finally
            {
                _isSending = false;
                SendCommand.RaiseCanExecuteChanged();
            }
        }

        private async System.Threading.Tasks.Task SendPingAsync()
        {
            // Application-level ping: any frame works, the gateway echoes it back.
            bool sent = await _session.SendTextAsync("{\"op\":\"ping\",\"ts\":" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + "}")
                .ConfigureAwait(true);
            StatusText = sent ? "ping frame queued" : "ping rejected - offline";
        }

        private void CopyVisible()
        {
            StringBuilder builder = new StringBuilder();
            foreach (object item in EntriesView)
            {
                if (item is LogEntry entry)
                {
                    builder.Append(entry.TimeText)
                        .Append(" [")
                        .Append(entry.LevelTag)
                        .Append("] ")
                        .Append(entry.CategoryText)
                        .Append(" · ")
                        .AppendLine(entry.Message);
                }
            }

            try
            {
                Clipboard.SetText(builder.ToString());
                StatusText = "visible lines copied to the clipboard";
            }
            catch (Exception ex)
            {
                StatusText = "clipboard unavailable: " + ex.GetType().Name;
            }
        }

        private bool FilterEntry(object item)
        {
            if (item is not LogEntry entry)
            {
                return false;
            }

            bool categoryAllowed = entry.Level switch
            {
                LogLevel.Inbound or LogLevel.Outbound => ShowTraffic,
                LogLevel.Trace or LogLevel.Debug => ShowDiagnostics,
                _ => ShowSystem
            };

            if (!categoryAllowed)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(_filterText))
            {
                return true;
            }

            return entry.Message.Contains(_filterText, StringComparison.OrdinalIgnoreCase)
                || entry.LevelTag.Contains(_filterText, StringComparison.OrdinalIgnoreCase)
                || entry.CategoryText.Contains(_filterText, StringComparison.OrdinalIgnoreCase);
        }

        private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            UpdateCounters();

            if (_hintTimer.IsEnabled)
            {
                return;
            }

            _hintTimer.Start();
        }

        private void UpdateCounters()
        {
            // O(1): counting the live view would enumerate every filtered line eight
            // times per second, which is pointless work on the dispatcher.
            CounterText = _log.Entries.Count.ToString() + " buffered · " +
                          _log.Written.ToString() + " captured";
        }

        private void UpdateHint()
        {
            OnPropertyChanged(nameof(SendHint));
            OnPropertyChanged(nameof(IsPaused));
            SendCommand.RaiseCanExecuteChanged();
        }
    }
}
