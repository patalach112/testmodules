using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Threading.Tasks;

namespace EcosystemClient
{
    /// <summary>
    /// Endpoint, key and transport tuning. Everything binds straight through to
    /// <see cref="AppSettings"/>; <see cref="SaveCommand"/> persists and reconnects.
    /// </summary>
    public sealed class SettingsViewModel : PageViewModel
    {
        private readonly ClientSession _session;
        private readonly LogStore _log;
        private string _statusText = "changes apply on save";

        public SettingsViewModel(ClientSession session, LogStore log)
            : base("settings", "Settings", "Transport, crypto core and diagnostic options", "M4,7 H20 M4,12 H20 M4,17 H20 M7.5,5 H10.5 V9 H7.5 Z M13.5,10 H16.5 V14 H13.5 Z M8.5,15 H11.5 V19 H8.5 Z")
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _log = log ?? throw new ArgumentNullException(nameof(log));

            SaveCommand = new AsyncRelayCommand(SaveAsync, onError: ReportFailure);
            ResetCommand = new RelayCommand(Reset);
            OpenSettingsFolderCommand = new RelayCommand(OpenSettingsFolder);
        }

        public AppSettings Settings => _session.Settings;

        public ICommand SaveCommand { get; }

        public ICommand ResetCommand { get; }

        public ICommand OpenSettingsFolderCommand { get; }

        public string StatusText
        {
            get => _statusText;
            private set => SetProperty(ref _statusText, value);
        }

        public string SettingsPathText => AppSettings.SettingsPath;

        public string RuntimeText => RuntimeInformation.FrameworkDescription;

        public string OsText => RuntimeInformation.OSDescription;

        public string ProcessText =>
            (Environment.Is64BitProcess ? "64-bit" : "32-bit") + " · " + RuntimeInformation.ProcessArchitecture;

        public string NativeStatusText => NativeCrypto.IsAvailable
            ? "linked · v" + NativeCrypto.LibraryVersion
            : "not found · managed fallback";

        public string NativePathText => string.IsNullOrEmpty(NativeCrypto.LibraryPath)
            ? "ECOSYSTEM_CRYPTO_LIBRARY not set"
            : NativeCrypto.LibraryPath;

        public string CryptoKeyPreview => AppSettings.FormatKey(_session.CryptoKey);

        public string EndpointHint =>
            "ws:// for a local gateway, wss:// behind Cloudflare. The client never disables certificate validation.";

        public async Task SaveAsync()
        {
            if (!AppSettings.TryParseEndpoint(Settings.Endpoint, out Uri endpoint, out string error))
            {
                StatusText = error;
                _log.Warn("settings rejected: " + error, "settings");
                return;
            }

            Settings.Endpoint = endpoint.AbsoluteUri;

            if (string.IsNullOrWhiteSpace(Settings.CryptoKey))
            {
                Settings.CryptoKey = AppSettings.DefaultKeyText;
            }

            await _session.ApplySettingsAsync().ConfigureAwait(true);

            StatusText = "saved · key " + CryptoKeyPreview;
            OnPropertiesChanged(nameof(SettingsPathText), nameof(CryptoKeyPreview), nameof(Settings));
        }

        private void Reset()
        {
            AppSettings defaults = new AppSettings();
            Settings.Endpoint = defaults.Endpoint;
            Settings.CryptoKey = defaults.CryptoKey;
            Settings.ReceiveBufferKb = defaults.ReceiveBufferKb;
            Settings.HealthTimeoutSeconds = defaults.HealthTimeoutSeconds;
            Settings.DecodeInbound = defaults.DecodeInbound;
            Settings.EnableLatencyProbes = defaults.EnableLatencyProbes;
            Settings.AutoConnect = defaults.AutoConnect;
            Settings.PreferOpaqueWindow = defaults.PreferOpaqueWindow;

            StatusText = "defaults restored · press SAVE to persist";
            OnPropertiesChanged(nameof(Settings), nameof(CryptoKeyPreview));
        }

        private void OpenSettingsFolder()
        {
            try
            {
                Directory.CreateDirectory(AppSettings.SettingsDirectory);
                Process.Start(new ProcessStartInfo
                {
                    FileName = AppSettings.SettingsDirectory,
                    UseShellExecute = true
                });
                StatusText = "opened " + AppSettings.SettingsDirectory;
            }
            catch (Exception ex)
            {
                StatusText = "cannot open folder: " + ex.Message;
            }
        }

        private void ReportFailure(Exception ex)
        {
            StatusText = ex.GetType().Name + ": " + ex.Message;
            _log.Error("settings: " + StatusText, "settings");
        }
    }
}
