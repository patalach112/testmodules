using System;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace EcosystemClient
{
    /// <summary>
    /// User-editable client configuration, persisted as JSON under
    /// <c>%APPDATA%\EcosystemClient\settings.json</c>.
    ///
    /// Every load is defensive: a corrupt or unwritable file degrades to defaults
    /// rather than preventing the client from starting.
    /// </summary>
    public sealed class AppSettings
    {
        public const string DefaultEndpoint = "ws://127.0.0.1:8080/";
        public const string DefaultKeyText = "0x0123456789ABCDEF";

        public string Endpoint { get; set; } = DefaultEndpoint;

        public string CryptoKey { get; set; } = DefaultKeyText;

        public bool AutoConnect { get; set; } = true;

        public bool DecodeInbound { get; set; } = true;

        public bool EnableLatencyProbes { get; set; } = true;

        /// <summary>Receive buffer size in KiB (1-1024).</summary>
        public int ReceiveBufferKb { get; set; } = 32;

        /// <summary>Dead-socket detection window in seconds (3-300).</summary>
        public int HealthTimeoutSeconds { get; set; } = 15;

        /// <summary>Also measure the managed fallback so the crypto page can show a delta.</summary>
        public bool BenchmarkFallback { get; set; } = true;

        /// <summary>
        /// Opt out of <c>AllowsTransparency</c> for maximum GPU throughput (square
        /// corners instead of rounded, no outer glow). See README "performance modes".
        /// </summary>
        public bool PreferOpaqueWindow { get; set; }

        public static string SettingsDirectory => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "EcosystemClient");

        public static string SettingsPath => Path.Combine(SettingsDirectory, "settings.json");

        public static AppSettings Load()
        {
            try
            {
                string path = SettingsPath;
                if (!File.Exists(path))
                {
                    return new AppSettings();
                }

                string json = File.ReadAllText(path);
                AppSettings? loaded = JsonSerializer.Deserialize<AppSettings>(json);
                if (loaded is null)
                {
                    return new AppSettings();
                }

                loaded.Normalise();
                return loaded;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is JsonException)
            {
                return new AppSettings();
            }
        }

        public void Save()
        {
            try
            {
                Normalise();
                Directory.CreateDirectory(SettingsDirectory);
                File.WriteAllText(
                    SettingsPath,
                    JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is NotSupportedException)
            {
                // Configuration is a convenience, never a hard dependency.
            }
        }

        public void Normalise()
        {
            if (string.IsNullOrWhiteSpace(Endpoint))
            {
                Endpoint = DefaultEndpoint;
            }

            if (string.IsNullOrWhiteSpace(CryptoKey))
            {
                CryptoKey = DefaultKeyText;
            }

            ReceiveBufferKb = Math.Clamp(ReceiveBufferKb, 1, 1024);
            HealthTimeoutSeconds = Math.Clamp(HealthTimeoutSeconds, 3, 300);
        }

        /// <summary>Fresh copy, so editing UI fields never mutates the persisted snapshot.</summary>
        public AppSettings Clone() => new AppSettings
        {
            Endpoint = Endpoint,
            CryptoKey = CryptoKey,
            AutoConnect = AutoConnect,
            DecodeInbound = DecodeInbound,
            EnableLatencyProbes = EnableLatencyProbes,
            ReceiveBufferKb = ReceiveBufferKb,
            HealthTimeoutSeconds = HealthTimeoutSeconds,
            BenchmarkFallback = BenchmarkFallback,
            PreferOpaqueWindow = PreferOpaqueWindow
        };

        /// <summary>
        /// Parses <c>0x</c>-prefixed hex or plain decimal text into the 64-bit transform
        /// key. Never throws: malformed input yields <paramref name="fallback"/>.
        /// </summary>
        public static ulong ParseKey(string? text, ulong fallback)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return fallback;
            }

            ReadOnlySpan<char> span = text.AsSpan().Trim();

            if (span.Length > 2 && span[0] == '0' && (span[1] == 'x' || span[1] == 'X'))
            {
                return ulong.TryParse(span.Slice(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong hex)
                    ? hex
                    : fallback;
            }

            if (ulong.TryParse(span, NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong dec))
            {
                return dec;
            }

            return ulong.TryParse(span, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong bareHex)
                ? bareHex
                : fallback;
        }

        /// <summary>Validates an endpoint string and reports the parse error to the UI.</summary>
        public static bool TryParseEndpoint(string? text, out Uri endpoint, out string error)
        {
            endpoint = new Uri(DefaultEndpoint);
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(text))
            {
                error = "Endpoint is empty.";
                return false;
            }

            if (!Uri.TryCreate(text.Trim(), UriKind.Absolute, out Uri? parsed))
            {
                error = "Endpoint is not an absolute URI.";
                return false;
            }

            if (parsed.Scheme != "ws" && parsed.Scheme != "wss")
            {
                error = "Endpoint must use ws:// or wss://";
                return false;
            }

            endpoint = parsed;
            return true;
        }

        /// <summary>Formats a key the way the gateway's <c>ASM_CRYPTO_KEY</c> expects it.</summary>
        public static string FormatKey(ulong key) => "0x" + key.ToString("X16", CultureInfo.InvariantCulture);
    }
}
