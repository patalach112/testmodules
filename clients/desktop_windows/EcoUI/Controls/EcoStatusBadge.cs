using System.Windows;
using System.Windows.Controls;

namespace EcoUI.Controls
{
    /// <summary>Semantic state shown by an <see cref="EcoStatusBadge"/>.</summary>
    public enum EcoStatus
    {
        Idle,
        Info,
        Success,
        Warning,
        Error
    }

    /// <summary>
    /// A minimal status indicator: a coloured dot plus optional text.
    ///
    /// <para>Colour comes from the theme's status tokens, never from a hardcoded
    /// value, so the badge stays legible after a theme switch. The dot is the primary
    /// signal; text is optional, which lets the same control serve as a compact
    /// presence indicator or a labelled state pill.</para>
    ///
    /// <code>
    /// &lt;eco:EcoStatusBadge Status="Success" Text="Operational" /&gt;
    /// </code>
    /// </summary>
    public class EcoStatusBadge : Control
    {
        /// <summary>Semantic state; drives the colours.</summary>
        public static readonly DependencyProperty StatusProperty =
            DependencyProperty.Register(
                nameof(Status),
                typeof(EcoStatus),
                typeof(EcoStatusBadge),
                new FrameworkPropertyMetadata(EcoStatus.Idle));

        /// <summary>Label shown next to the dot.</summary>
        public static readonly DependencyProperty TextProperty =
            DependencyProperty.Register(
                nameof(Text),
                typeof(string),
                typeof(EcoStatusBadge),
                new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.None, OnTextChanged));

        /// <summary>Show the label. When false only the dot is rendered.</summary>
        public static readonly DependencyProperty ShowTextProperty =
            DependencyProperty.Register(
                nameof(ShowText),
                typeof(bool),
                typeof(EcoStatusBadge),
                new FrameworkPropertyMetadata(true));

        private static readonly DependencyPropertyKey HasTextPropertyKey =
            DependencyProperty.RegisterReadOnly(
                nameof(HasText),
                typeof(bool),
                typeof(EcoStatusBadge),
                new FrameworkPropertyMetadata(false));

        /// <summary>True when text is present; combined with <see cref="ShowText"/> by the template.</summary>
        public static readonly DependencyProperty HasTextProperty = HasTextPropertyKey.DependencyProperty;

        static EcoStatusBadge()
        {
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(EcoStatusBadge),
                new FrameworkPropertyMetadata(typeof(EcoStatusBadge)));
        }

        /// <summary>Semantic state shown by the badge.</summary>
        public EcoStatus Status
        {
            get => (EcoStatus)GetValue(StatusProperty);
            set => SetValue(StatusProperty, value);
        }

        /// <summary>Label rendered next to the dot.</summary>
        public string Text
        {
            get => (string)GetValue(TextProperty);
            set => SetValue(TextProperty, value);
        }

        /// <summary>Whether the label is rendered.</summary>
        public bool ShowText
        {
            get => (bool)GetValue(ShowTextProperty);
            set => SetValue(ShowTextProperty, value);
        }

        /// <summary>True when a label is present.</summary>
        public bool HasText => (bool)GetValue(HasTextProperty);

        private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is EcoStatusBadge badge)
            {
                badge.SetValue(HasTextPropertyKey, !string.IsNullOrWhiteSpace(e.NewValue as string));
            }
        }
    }
}
