using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace EcosystemClient
{
    /// <summary>
    /// Application entry point and composition root.
    ///
    /// Startup order matters: exception handlers are installed first (so a failure in
    /// composition is reported instead of silently killing the process), then the log
    /// store, session and shell are created - all on the dispatcher thread - and only
    /// afterwards does any background work begin. The native core is warmed up on the
    /// thread pool so the UI thread never pays a DLL load.
    /// </summary>
    public partial class App : Application
    {
        private LogStore? _log;
        private ClientSession? _session;
        private MainViewModel? _viewModel;
        private MainWindow? _shell;
        private int _fatalDialogShown;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

            // Loader round-trip for asm_crypto.dll, off the dispatcher.
            _ = Task.Run(NativeCrypto.WarmUp);

            AppSettings settings = AppSettings.Load();
            _log = new LogStore();
            _session = new ClientSession(_log, settings);
            _viewModel = new MainViewModel(_log, _session);

            _log.Info($"settings: {AppSettings.SettingsPath}", "app");
            _log.Info($"runtime: {Environment.Version} · {(Environment.Is64BitProcess ? "x64" : "x86")} · {Environment.OSVersion}", "app");

            _shell = new MainWindow(settings, _viewModel);
            MainWindow = _shell;
            _shell.Show();

            _viewModel.StartSessionIfConfigured();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                _viewModel?.Dispose();
                _shell = null;

                if (_session is not null)
                {
                    // Bounded: a hung socket must never block shutdown.
                    _session.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(2));
                }
            }
            catch (AggregateException)
            {
                // Shutdown is best-effort by definition.
            }
            finally
            {
                base.OnExit(e);
            }
        }

        /// <summary>
        /// A UI exception is logged, reported once, and swallowed: a telemetry client
        /// that dies on a single bad frame is worse than one that keeps running.
        /// </summary>
        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            _log?.Error("UI exception: " + e.Exception.GetType().Name + ": " + e.Exception.Message, "app");

            if (System.Threading.Interlocked.Exchange(ref _fatalDialogShown, 1) == 0)
            {
                MessageBox.Show(
                    "The client hit an unexpected UI error and recovered.\n\n" +
                    e.Exception.GetType().Name + ": " + e.Exception.Message +
                    "\n\nDetails are in the terminal view.",
                    "Ecosystem Client",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            e.Handled = true;
        }

        private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex)
            {
                _log?.Error("fatal: " + ex.GetType().Name + ": " + ex.Message, "app");
            }
        }

        private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            _log?.Error("background task fault: " + e.Exception.GetBaseException().Message, "app");
            e.SetObserved();
        }
    }
}
