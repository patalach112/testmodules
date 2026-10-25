using System.Windows;
using System.Windows.Controls;

namespace EcoUI.Controls
{
    /// <summary>Meaning of the trend chip: positive, negative or neutral movement.</summary>
    public enum EcoTrendKind
    {
        None,
        Positive,
        Negative,
        Neutral
    }

    /// <summary>
    /// A metric tile: a title, a headline value, optional subtext and an optional
    /// trend chip.
    ///
    /// <para>Pure presentation - it holds no data source and performs no formatting of
    /// numbers, so the same control works for money, counts, percentages or
    /// durations. Supply strings; the control decides the typography.</para>
    ///
    /// <code>
    /// &lt;eco:EcoStatCard Title="Monthly revenue" Value="$48,120"
    ///                    Subtext="vs. last month" Trend="+12.4%"
    ///                    TrendKind="Positive" /&gt;
    /// </code>
    /// </summary>
    public class EcoStatCard : Control
    {
        /// <summary>Small label above the value.</summary>
        public static readonly DependencyProperty TitleProperty =
            DependencyProperty.Register(
                nameof(Title),
                typeof(string),
                typeof(EcoStatCard),
                new FrameworkPropertyMetadata(string.Empty));

        /// <summary>Headline value. Pre-formatted by the consumer.</summary>
        public static readonly DependencyProperty ValueProperty =
            DependencyProperty.Register(
                nameof(Value),
                typeof(string),
                typeof(EcoStatCard),
                new FrameworkPropertyMetadata(string.Empty));

        /// <summary>Supporting line below the value.</summary>
        public static readonly DependencyProperty SubtextProperty =
            DependencyProperty.Register(
                nameof(Subtext),
                typeof(string),
                typeof(EcoStatCard),
                new FrameworkPropertyMetadata(string.Empty));

        /// <summary>Trend text, e.g. <c>+12.4%</c>. Hidden while empty.</summary>
        public static readonly DependencyProperty TrendProperty =
            DependencyProperty.Register(
                nameof(Trend),
                typeof(string),
                typeof(EcoStatCard),
                new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.None, OnTrendChanged));

        /// <summary>Colour role of the trend chip.</summary>
        public static readonly DependencyProperty TrendKindProperty =
            DependencyProperty.Register(
                nameof(TrendKind),
                typeof(EcoTrendKind),
                typeof(EcoStatCard),
                new FrameworkPropertyMetadata(EcoTrendKind.None));

        private static readonly DependencyPropertyKey HasTrendPropertyKey =
            DependencyProperty.RegisterReadOnly(
                nameof(HasTrend),
                typeof(bool),
                typeof(EcoStatCard),
                new FrameworkPropertyMetadata(false));

        /// <summary>True when <see cref="Trend"/> has content (drives the chip visibility).</summary>
        public static readonly DependencyProperty HasTrendProperty = HasTrendPropertyKey.DependencyProperty;

        static EcoStatCard()
        {
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(EcoStatCard),
                new FrameworkPropertyMetadata(typeof(EcoStatCard)));
        }

        /// <summary>Small label above the value.</summary>
        public string Title
        {
            get => (string)GetValue(TitleProperty);
            set => SetValue(TitleProperty, value);
        }

        /// <summary>Headline value.</summary>
        public string Value
        {
            get => (string)GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }

        /// <summary>Supporting line below the value.</summary>
        public string Subtext
        {
            get => (string)GetValue(SubtextProperty);
            set => SetValue(SubtextProperty, value);
        }

        /// <summary>Trend text shown in the chip.</summary>
        public string Trend
        {
            get => (string)GetValue(TrendProperty);
            set => SetValue(TrendProperty, value);
        }

        /// <summary>Colour role of the trend chip.</summary>
        public EcoTrendKind TrendKind
        {
            get => (EcoTrendKind)GetValue(TrendKindProperty);
            set => SetValue(TrendKindProperty, value);
        }

        /// <summary>True when the trend chip has content.</summary>
        public bool HasTrend => (bool)GetValue(HasTrendProperty);

        private static void OnTrendChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is EcoStatCard card)
            {
                string? trend = e.NewValue as string;
                card.SetValue(HasTrendPropertyKey, !string.IsNullOrWhiteSpace(trend));
            }
        }
    }
}
