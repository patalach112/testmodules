using System;
using System.Globalization;

namespace EcoUI.Controls
{
    /// <summary>Severity of a line in an <see cref="EcoTerminal"/>.</summary>
    public enum EcoLogLevel
    {
        Info,
        Success,
        Warning,
        Error,
        Debug
    }

    /// <summary>
    /// One immutable line of terminal output. Kept deliberately small: it is a display
    /// model, not a logging framework.
    /// </summary>
    public sealed class EcoLogEntry
    {
        /// <summary>Creates an entry.</summary>
        /// <param name="message">Line text. Null is treated as empty.</param>
        /// <param name="level">Severity, used for colouring.</param>
        /// <param name="timestamp">Wall-clock time; defaults to now.</param>
        public EcoLogEntry(string message, EcoLogLevel level = EcoLogLevel.Info, DateTimeOffset? timestamp = null)
        {
            Message = message ?? string.Empty;
            Level = level;
            Timestamp = timestamp ?? DateTimeOffset.Now;
        }

        /// <summary>When the line was produced.</summary>
        public DateTimeOffset Timestamp { get; }

        /// <summary>Severity of the line.</summary>
        public EcoLogLevel Level { get; }

        /// <summary>Line text.</summary>
        public string Message { get; }

        /// <summary>Fixed-width wall clock, e.g. <c>14:03:22</c>.</summary>
        public string TimeText => Timestamp.ToString("HH:mm:ss", CultureInfo.InvariantCulture);

        /// <summary>Short severity tag shown in the terminal gutter.</summary>
        public string LevelTag => Level switch
        {
            EcoLogLevel.Success => "OK",
            EcoLogLevel.Warning => "WARN",
            EcoLogLevel.Error => "ERR",
            EcoLogLevel.Debug => "DBG",
            _ => "INFO"
        };
    }
}
