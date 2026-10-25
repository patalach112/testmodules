using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Shell;
using EcoUI.Interop;

namespace EcoUI
{
    /// <summary>
    /// Frameless window base class with native behaviour.
    ///
    /// <para><b>Why WindowChrome instead of AllowsTransparency.</b> A window with
    /// <c>WindowStyle="None"</c> loses its non-client frame, including drag, snap,
    /// double-click maximise and resize borders. The usual workaround -
    /// <c>AllowsTransparency="True"</c> plus a manual <c>DragMove()</c> - turns the
    /// window into a layered window: Windows then composites it in software, the
    /// resize borders disappear, and Aero Snap stops working.
    /// <see cref="WindowChrome"/> instead keeps the real HWND frame logic and simply
    /// lets WPF draw over it: drag, snap (including edge and corner snapping), the
    /// maximise animation, DPI-correct resize cursors and Windows 11's rounded
    /// corners all behave natively.</para>
    ///
    /// <para><b>What the template provides.</b> A title bar (title, optional
    /// <see cref="TitleBarContent"/>, minimise/maximise/close buttons) and a content
    /// presenter inside an <see cref="System.Windows.Documents.AdornerDecorator"/>.
    /// Parts are wired by name, so a consumer can restyle the window without touching
    /// code as long as the part names survive.</para>
    ///
    /// <code>
    /// &lt;eco:EcoWindow x:Class="MyApp.MainWindow" Title="My app" ...&gt;
    ///     &lt;Grid&gt; ... &lt;/Grid&gt;
    /// &lt;/eco:EcoWindow&gt;
    /// </code>
    /// </summary>
    [TemplatePart(Name = PartMinimizeButton, Type = typeof(ButtonBase))]
    [TemplatePart(Name = PartMaximizeButton, Type = typeof(ButtonBase))]
    [TemplatePart(Name = PartCloseButton, Type = typeof(ButtonBase))]
    public class EcoWindow : Window
    {
        /// <summary>Template part: minimise button.</summary>
        public const string PartMinimizeButton = "PART_MinimizeButton";

        /// <summary>Template part: maximise/restore button.</summary>
        public const string PartMaximizeButton = "PART_MaximizeButton";

        /// <summary>Template part: close button.</summary>
        public const string PartCloseButton = "PART_CloseButton";

        /// <summary>Height of the title bar, in device-independent pixels.</summary>
        public static readonly DependencyProperty TitleBarHeightProperty =
            DependencyProperty.Register(
                nameof(TitleBarHeight),
                typeof(double),
                typeof(EcoWindow),
                new FrameworkPropertyMetadata(44d, FrameworkPropertyMetadataOptions.None, OnChromeAffectingPropertyChanged));

        /// <summary>Shows or hides the built-in title bar (and its caption hit area).</summary>
        public static readonly DependencyProperty IsTitleBarVisibleProperty =
            DependencyProperty.Register(
                nameof(IsTitleBarVisible),
                typeof(bool),
                typeof(EcoWindow),
                new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.None, OnChromeAffectingPropertyChanged));

        /// <summary>Extra content placed after the title (search box, tabs, …).</summary>
        public static readonly DependencyProperty TitleBarContentProperty =
            DependencyProperty.Register(
                nameof(TitleBarContent),
                typeof(object),
                typeof(EcoWindow),
                new FrameworkPropertyMetadata(null));

        /// <summary>Whether the title bar shows a maximise button.</summary>
        public static readonly DependencyProperty ShowMaximizeButtonProperty =
            DependencyProperty.Register(
                nameof(ShowMaximizeButton),
                typeof(bool),
                typeof(EcoWindow),
                new FrameworkPropertyMetadata(true));

        /// <summary>Whether the title bar shows a minimise button.</summary>
        public static readonly DependencyProperty ShowMinimizeButtonProperty =
            DependencyProperty.Register(
                nameof(ShowMinimizeButton),
                typeof(bool),
                typeof(EcoWindow),
                new FrameworkPropertyMetadata(true));

        private static readonly DependencyPropertyKey IsMaximizedPropertyKey =
            DependencyProperty.RegisterReadOnly(
                nameof(IsMaximized),
                typeof(bool),
                typeof(EcoWindow),
                new FrameworkPropertyMetadata(false));

        /// <summary>True while the window is maximised (drives the restore glyph).</summary>
        public static readonly DependencyProperty IsMaximizedProperty = IsMaximizedPropertyKey.DependencyProperty;

        /// <summary>Edge thickness, in pixels, of the native resize border.</summary>
        private const double ResizeBorder = 6d;

        private ButtonBase? _minimizeButton;
        private ButtonBase? _maximizeButton;
        private ButtonBase? _closeButton;

        static EcoWindow()
        {
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(EcoWindow),
                new FrameworkPropertyMetadata(typeof(EcoWindow)));
        }

        public EcoWindow()
        {
            // The whole point of this class: no operating-system title bar.
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.CanResize;
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;

            UpdateWindowChrome();
            StateChanged += (_, _) => OnWindowStateChanged();
        }

        /// <summary>Height of the title bar.</summary>
        public double TitleBarHeight
        {
            get => (double)GetValue(TitleBarHeightProperty);
            set => SetValue(TitleBarHeightProperty, value);
        }

        /// <summary>Shows or hides the built-in title bar.</summary>
        public bool IsTitleBarVisible
        {
            get => (bool)GetValue(IsTitleBarVisibleProperty);
            set => SetValue(IsTitleBarVisibleProperty, value);
        }

        /// <summary>Additional title bar content (shown after the window title).</summary>
        public object? TitleBarContent
        {
            get => GetValue(TitleBarContentProperty);
            set => SetValue(TitleBarContentProperty, value);
        }

        /// <summary>Whether a maximise button is shown.</summary>
        public bool ShowMaximizeButton
        {
            get => (bool)GetValue(ShowMaximizeButtonProperty);
            set => SetValue(ShowMaximizeButtonProperty, value);
        }

        /// <summary>Whether a minimise button is shown.</summary>
        public bool ShowMinimizeButton
        {
            get => (bool)GetValue(ShowMinimizeButtonProperty);
            set => SetValue(ShowMinimizeButtonProperty, value);
        }

        /// <summary>True while the window is maximised.</summary>
        public bool IsMaximized
        {
            get => (bool)GetValue(IsMaximizedProperty);
            private set => SetValue(IsMaximizedPropertyKey, value);
        }

        /// <inheritdoc />
        public override void OnApplyTemplate()
        {
            DetachParts();
            base.OnApplyTemplate();
            AttachParts();

            // A consumer may swap the template at runtime; keep the caption in sync.
            UpdateWindowChrome();
        }

        /// <inheritdoc />
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            NativeFrame.Apply(this, ThemeEngine.CurrentTheme == EcoTheme.Dark);
            ThemeEngine.ThemeChanged += OnThemeChanged;
        }

        /// <inheritdoc />
        protected override void OnClosed(EventArgs e)
        {
            ThemeEngine.ThemeChanged -= OnThemeChanged;
            base.OnClosed(e);
        }

        // ---------------------------------------------------------------------
        // Window behaviour
        // ---------------------------------------------------------------------

        /// <summary>
        /// (Re)installs the window chrome. <c>CaptionHeight</c> is what gives the title
        /// bar its native drag, double-click and snap behaviour, so it must always match
        /// the rendered title bar.
        /// </summary>
        private void UpdateWindowChrome()
        {
            WindowChrome.SetWindowChrome(this, new WindowChrome
            {
                CaptionHeight = IsTitleBarVisible ? Math.Max(0d, TitleBarHeight) : 0d,
                ResizeBorderThickness = new Thickness(ResizeBorder),
                GlassFrameThickness = new Thickness(0),
                CornerRadius = new CornerRadius(0),
                UseAeroCaptionButtons = false
            });
        }

        private static void OnChromeAffectingPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is EcoWindow window)
            {
                window.UpdateWindowChrome();
            }
        }

        private void OnWindowStateChanged()
        {
            IsMaximized = WindowState == WindowState.Maximized;

            // Maximised windows sit flush against the work area; rounding would clip
            // the content at the screen edges.
            NativeFrame.SetRounded(this, WindowState != WindowState.Maximized);
        }

        private void OnThemeChanged(object? sender, EventArgs e) =>
            NativeFrame.Apply(this, ThemeEngine.CurrentTheme == EcoTheme.Dark);

        private void AttachParts()
        {
            if (GetTemplateChild(PartMinimizeButton) is ButtonBase minimize)
            {
                _minimizeButton = minimize;
                minimize.Click += OnMinimizeClick;
            }

            if (GetTemplateChild(PartMaximizeButton) is ButtonBase maximize)
            {
                _maximizeButton = maximize;
                maximize.Click += OnMaximizeRestoreClick;
            }

            if (GetTemplateChild(PartCloseButton) is ButtonBase close)
            {
                _closeButton = close;
                close.Click += OnCloseClick;
            }
        }

        private void DetachParts()
        {
            if (_minimizeButton is not null)
            {
                _minimizeButton.Click -= OnMinimizeClick;
                _minimizeButton = null;
            }

            if (_maximizeButton is not null)
            {
                _maximizeButton.Click -= OnMaximizeRestoreClick;
                _maximizeButton = null;
            }

            if (_closeButton is not null)
            {
                _closeButton.Click -= OnCloseClick;
                _closeButton = null;
            }
        }

        private void OnMinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        private void OnMaximizeRestoreClick(object sender, RoutedEventArgs e) =>
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

        private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
    }
}
