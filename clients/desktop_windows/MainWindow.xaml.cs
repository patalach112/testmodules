using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using EcosystemClient.Interop;

namespace EcosystemClient
{
    /// <summary>
    /// Borderless shell with a hand-built title bar.
    ///
    /// <para><b>Two rendering modes.</b> The default mode is
    /// <c>AllowsTransparency="True"</c>: rounded corners, a real outer drop shadow
    /// and a floating window. That makes Windows treat the window as layered, which
    /// disables hardware acceleration for its content. Users who want maximum
    /// throughput can set <c>PreferOpaqueWindow</c> (Settings → Saves to
    /// settings.json, or <c>ECOSYSTEM_OPAQUE_WINDOW=1</c>): the window then becomes a
    /// normal accelerated HwndSource with GPU compositing and Mica on Windows 11,
    /// trading the rounded silhouette for frame rate.</para>
    ///
    /// <para><b>Resizing</b> is provided by <see cref="WindowChrome"/> (a 6 px hit
    /// border) instead of the removed non-client frame, and the maximise bounds are
    /// clamped to the monitor work area by <see cref="NativeWindow"/> so the window
    /// never covers the taskbar.</para>
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _viewModel;
        private readonly bool _opaque;

        public MainWindow(AppSettings settings, MainViewModel viewModel)
        {
            _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            _opaque = settings.PreferOpaqueWindow ||
                      string.Equals(Environment.GetEnvironmentVariable("ECOSYSTEM_OPAQUE_WINDOW"), "1", StringComparison.Ordinal);

            InitializeComponent();

            DataContext = viewModel;

            if (_opaque)
            {
                // Must happen before the HWND is created.
                AllowsTransparency = false;
                Background = new SolidColorBrush(Color.FromRgb(0x0B, 0x0B, 0x0F));
            }

            ConfigureWindowChrome();
            ApplyChromeVisuals();

            StateChanged += (_, _) => ApplyChromeVisuals();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            NativeWindow.Attach(this, _opaque);
        }

        // ---------------------------------------------------------------------
        // Window chrome
        // ---------------------------------------------------------------------

        private void ConfigureWindowChrome()
        {
            /*
             * CaptionHeight = 0 keeps every pixel client-side (we do our own dragging),
             * while ResizeBorderThickness hands resize hit-testing back to Windows -
             * which is what makes edge dragging and DPI-correct cursors work.
             */
            WindowChrome chrome = new WindowChrome
            {
                CaptionHeight = 0,
                ResizeBorderThickness = new Thickness(6),
                GlassFrameThickness = new Thickness(0),
                CornerRadius = new CornerRadius(0),
                UseAeroCaptionButtons = false
            };

            WindowChrome.SetWindowChrome(this, chrome);
        }

        private void ApplyChromeVisuals()
        {
            bool maximized = WindowState == WindowState.Maximized;
            double inset = _opaque ? 0d : 18d;

            Thickness margin = maximized ? new Thickness(0) : new Thickness(inset);
            WindowShell.Margin = margin;
            ShadowLayer.Margin = margin;

            double corner = maximized || _opaque ? 0d : 20d;
            WindowShell.CornerRadius = new CornerRadius(corner);
            ShadowLayer.CornerRadius = new CornerRadius(corner + 2d);

            ShadowLayer.Visibility = maximized || _opaque ? Visibility.Collapsed : Visibility.Visible;

            MaximizeGlyph.Visibility = maximized ? Visibility.Collapsed : Visibility.Visible;
            RestoreGlyph.Visibility = maximized ? Visibility.Visible : Visibility.Collapsed;
            MaximizeButton.ToolTip = maximized ? "Restore down" : "Maximize";
        }

        // ---------------------------------------------------------------------
        // Title bar interaction
        // ---------------------------------------------------------------------

        private void OnTitleBarMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left || IsInteractiveSource(e.OriginalSource))
            {
                return;
            }

            if (e.ClickCount == 2)
            {
                ToggleMaximize();
                return;
            }

            if (WindowState == WindowState.Maximized)
            {
                // Win32-like behaviour: dragging a maximised window restores it first.
                WindowState = WindowState.Normal;
            }

            try
            {
                DragMove();
            }
            catch (InvalidOperationException)
            {
                // The mouse button was released before the drag could start.
            }
        }

        /// <summary>True when the click landed on a control that wants the mouse itself.</summary>
        private static bool IsInteractiveSource(object? source)
        {
            DependencyObject? current = source as DependencyObject;

            while (current is not null)
            {
                if (current is ButtonBase or TextBoxBase or ScrollBar or ListBoxItem)
                {
                    return true;
                }

                current = current is Visual or System.Windows.Media.Media3D.Visual3D
                    ? VisualTreeHelper.GetParent(current)
                    : LogicalTreeHelper.GetParent(current);
            }

            return false;
        }

        private void ToggleMaximize() =>
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

        private void OnMinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        private void OnMaximizeRestoreClick(object sender, RoutedEventArgs e) => ToggleMaximize();

        private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

        protected override void OnClosed(EventArgs e)
        {
            _viewModel.Dispose();
            base.OnClosed(e);
        }
    }
}
