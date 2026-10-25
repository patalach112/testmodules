using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace EcosystemClient.Converters
{
    /// <summary>Shared, frozen brushes so converters never allocate during a binding update.</summary>
    internal static class Palette
    {
        internal static readonly Brush Cyan = Frozen(Color.FromRgb(0x22, 0xD3, 0xEE));
        internal static readonly Brush Teal = Frozen(Color.FromRgb(0x2D, 0xD4, 0xBF));
        internal static readonly Brush Purple = Frozen(Color.FromRgb(0xA8, 0x55, 0xF7));
        internal static readonly Brush Magenta = Frozen(Color.FromRgb(0xF4, 0x72, 0xB6));
        internal static readonly Brush Green = Frozen(Color.FromRgb(0x34, 0xD3, 0x99));
        internal static readonly Brush Amber = Frozen(Color.FromRgb(0xFB, 0xBF, 0x24));
        internal static readonly Brush Red = Frozen(Color.FromRgb(0xF8, 0x71, 0x71));
        internal static readonly Brush Blue = Frozen(Color.FromRgb(0x60, 0xA5, 0xFA));
        internal static readonly Brush Dim = Frozen(Color.FromRgb(0x8A, 0x8A, 0xA0));

        private static Brush Frozen(Color color)
        {
            SolidColorBrush brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
    }

    /// <summary>Boolean to <see cref="Visibility"/>; set <see cref="Invert"/> for the "false" case.</summary>
    public sealed class BoolToVisibilityConverter : IValueConverter
    {
        public bool Invert { get; set; }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool flag = value is bool b && b;
            if (Invert)
            {
                flag = !flag;
            }

            return flag ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is Visibility visibility && (visibility == Visibility.Visible) != Invert;
    }

    public sealed class InverseBooleanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is bool b && !b;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is bool b && !b;
    }

    /// <summary>Log level → neon accent used by the terminal gutter.</summary>
    public sealed class LogLevelToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            value switch
            {
                LogLevel.Inbound => Palette.Cyan,
                LogLevel.Outbound => Palette.Purple,
                LogLevel.Crypto => Palette.Magenta,
                LogLevel.Success => Palette.Green,
                LogLevel.Warn => Palette.Amber,
                LogLevel.Error => Palette.Red,
                LogLevel.Trace => Palette.Dim,
                LogLevel.Debug => Palette.Blue,
                _ => Palette.Dim
            };

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }

    /// <summary>Latency quality word → colour, so the headline number changes tone with health.</summary>
    public sealed class QualityToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            (value as string) switch
            {
                "EXCELLENT" => Palette.Green,
                "STABLE" => Palette.Cyan,
                "ELEVATED" => Palette.Amber,
                "DEGRADED" => Palette.Red,
                "METERING" => Palette.Blue,
                "IDLE" => Palette.Dim,
                _ => Palette.Dim
            };

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }

    /// <summary>Transport state → status-pill accent.</summary>
    public sealed class NetworkStateToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is NetworkState state
                ? state switch
                {
                    NetworkState.Connected => Palette.Green,
                    NetworkState.Connecting => Palette.Amber,
                    NetworkState.Reconnecting => Palette.Amber,
                    NetworkState.Stopping => Palette.Blue,
                    _ => Palette.Red
                }
                : Palette.Dim;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }

    /// <summary>True when the bound string is non-empty (used to show hints/placeholders).</summary>
    public sealed class EmptyStringToVisibilityConverter : IValueConverter
    {
        public bool Invert { get; set; }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool hasText = !string.IsNullOrWhiteSpace(value as string);
            if (Invert)
            {
                hasText = !hasText;
            }

            return hasText ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
