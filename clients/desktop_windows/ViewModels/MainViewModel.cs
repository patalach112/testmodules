using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using System.Windows.Threading;

namespace EcosystemClient
{
    /// <summary>
    /// Shell view model: navigation, the title-bar status surface, the global clock
    /// and ownership of every page view model (and therefore of the session).
    /// </summary>
    public sealed class MainViewModel : ObservableObject, IDisposable
    {
        private readonly DispatcherTimer _clockTimer;
        private PageViewModel _selectedPage;
        private string _clockText = string.Empty;
        private bool _disposed;

        public MainViewModel(LogStore log, ClientSession session)
        {
            Log = log;
            Session = session;

            Dashboard = new DashboardViewModel(session, log);
            Terminal = new TerminalViewModel(session, log);
            Crypto = new CryptoViewModel(session, log);
            SettingsPage = new SettingsViewModel(session, log);

            Pages = new ObservableCollection<PageViewModel>
            {
                Dashboard,
                Terminal,
                Crypto,
                SettingsPage
            };

            _selectedPage = Dashboard;

            ClearLogsCommand = new RelayCommand(() =>
            {
                log.Clear();
                log.Info("terminal cleared", "ui");
            });

            NavigateCommand = new RelayCommand(parameter =>
            {
                if (parameter is PageViewModel page)
                {
                    SelectedPage = page;
                }
            });

            ShowDashboardCommand = new RelayCommand(() => SelectedPage = Dashboard);
            ShowTerminalCommand = new RelayCommand(() => SelectedPage = Terminal);
            ShowCryptoCommand = new RelayCommand(() => SelectedPage = Crypto);
            ShowSettingsCommand = new RelayCommand(() => SelectedPage = SettingsPage);

            _clockTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _clockTimer.Tick += (_, _) => UpdateClock();
            _clockTimer.Start();
            UpdateClock();

            log.Success("Ecosystem Client ready", "ui");
            log.Info(
                NativeCrypto.IsAvailable
                    ? $"native crypto core linked (v{NativeCrypto.LibraryVersion}) at {NativeCrypto.LibraryPath}"
                    : "native crypto core not found - running the managed fallback",
                "crypto");
        }

        public LogStore Log { get; }

        public ClientSession Session { get; }

        public DashboardViewModel Dashboard { get; }

        public TerminalViewModel Terminal { get; }

        public CryptoViewModel Crypto { get; }

        public SettingsViewModel SettingsPage { get; }

        public ObservableCollection<PageViewModel> Pages { get; }

        public PageViewModel SelectedPage
        {
            get => _selectedPage;
            set
            {
                if (value is null || ReferenceEquals(value, _selectedPage))
                {
                    return;
                }

                _selectedPage.OnDeactivated();
                if (SetProperty(ref _selectedPage, value))
                {
                    _selectedPage.OnActivated();
                    OnPropertiesChanged(nameof(HeaderTitle), nameof(HeaderSubtitle), nameof(HeaderIndex));
                }
            }
        }

        public string HeaderTitle => SelectedPage.Title;

        public string HeaderSubtitle => SelectedPage.Subtitle;

        /// <summary>1-based page index rendered in the header ("01 / 04").</summary>
        public string HeaderIndex
        {
            get
            {
                int index = Pages.IndexOf(SelectedPage) + 1;
                return index.ToString("00", CultureInfo.InvariantCulture) + " / " +
                       Pages.Count.ToString("00", CultureInfo.InvariantCulture);
            }
        }

        public string ClockText
        {
            get => _clockText;
            private set => SetProperty(ref _clockText, value);
        }

        public string BuildLabel => "v1.0.0 · net8.0-windows";

        public ICommand ClearLogsCommand { get; }

        public ICommand NavigateCommand { get; }

        public ICommand ShowDashboardCommand { get; }

        public ICommand ShowTerminalCommand { get; }

        public ICommand ShowCryptoCommand { get; }

        public ICommand ShowSettingsCommand { get; }

        public void StartSessionIfConfigured()
        {
            if (!Session.Settings.AutoConnect)
            {
                Log.Info("auto-connect disabled - press CONNECT to open the transport", "ui");
                return;
            }

            _ = ConnectSafelyAsync();
        }

        /// <summary>
        /// Auto-connect runs outside any command, so nothing else would observe a
        /// failure: absorb it into the log instead of the unobserved-task handler.
        /// </summary>
        private async System.Threading.Tasks.Task ConnectSafelyAsync()
        {
            try
            {
                await Session.ConnectAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                Log.Error("auto-connect failed: " + ex.GetType().Name + ": " + ex.Message, "session");
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _clockTimer.Stop();

            Dashboard.Dispose();
            Terminal.Dispose();
            Crypto.Dispose();

            // The session is disposed by App (it outlives this view model by one
            // shutdown step so the socket closes gracefully).
        }

        private void UpdateClock() =>
            ClockText = DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
    }
}
