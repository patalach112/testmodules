using System.Windows;
using EcoUI.Controls;

namespace EcoUI.Demo
{
    /// <summary>
    /// The demo shell. It derives from <see cref="EcoWindow"/>, so the frameless
    /// chrome, drag/snap/minimise/maximise behaviour and theming come from the
    /// library rather than from this file.
    /// </summary>
    public partial class MainWindow : EcoWindow
    {
        /// <summary>Creates the demo window and writes a few sample terminal lines.</summary>
        public MainWindow()
        {
            InitializeComponent();

            Terminal.Append("EcoUI demo started", EcoLogLevel.Success);
            Terminal.Append("Frameless window, native snap and resize.", EcoLogLevel.Info);
            Terminal.Append("Controls are styled entirely by the theme tokens.", EcoLogLevel.Debug);
            Terminal.Append("This line is a warning for colour contrast.", EcoLogLevel.Warning);
        }

        /// <summary>
        /// One call is all a theme switch needs: every control reads its colours
        /// through DynamicResource lookups.
        /// </summary>
        private void OnSwitchThemeClick(object sender, RoutedEventArgs e) => ThemeEngine.ToggleTheme();
    }
}
