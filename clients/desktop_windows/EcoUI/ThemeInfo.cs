using System.Windows;

// Tells WPF where to look for this library's default control themes.
//
// SourceAssembly: the theme dictionaries live in Themes/Generic.xaml inside this
// assembly, which is the standard location for a custom control library. Without
// this attribute the default styles for EcoWindow, EcoTerminal, EcoStatCard and
// EcoStatusBadge would never be found and the controls would render empty.
[assembly: ThemeInfo(
    ResourceDictionaryLocation.None,
    ResourceDictionaryLocation.SourceAssembly)]
