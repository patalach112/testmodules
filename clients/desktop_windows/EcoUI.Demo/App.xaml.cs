using System.Windows;

namespace EcoUI.Demo
{
    /// <summary>
    /// Demo entry point. The entire theming setup is one call to
    /// <see cref="ThemeEngine.Apply(EcoTheme, EcoAccent)"/>.
    /// </summary>
    public partial class App : Application
    {
        /// <inheritdoc />
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            ThemeEngine.Apply(EcoTheme.Dark, EcoAccent.Indigo);

            MainWindow window = new MainWindow();
            MainWindow = window;
            window.Show();
        }
    }
}
