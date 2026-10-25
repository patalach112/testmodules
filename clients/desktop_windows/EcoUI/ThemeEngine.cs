using System;
using System.Windows;
using System.Windows.Media;

namespace EcoUI
{
    /// <summary>Colour scheme of the design system.</summary>
    public enum EcoTheme
    {
        Light,
        Dark
    }

    /// <summary>
    /// Available accent ramps. The accent only ever changes the accent tokens -
    /// surfaces, borders and text keep their theme values.
    /// </summary>
    public enum EcoAccent
    {
        Blue,
        Indigo,
        Violet,
        Cyan,
        Teal,
        Green,
        Amber,
        Rose
    }

    /// <summary>
    /// Runtime theming for EcoUI.
    ///
    /// <para><b>How it works.</b> Every control in the library reads its colours from
    /// semantic tokens (<c>Eco.Surface.Panel</c>, <c>Eco.Text.Primary</c>, …) through
    /// <c>DynamicResource</c>. <see cref="Apply(EcoTheme, EcoAccent)"/> merges the
    /// matching token dictionary and the standard control styles into
    /// <see cref="Application.Resources"/>, then overwrites the accent tokens with a
    /// ramp derived from the requested accent. Because dynamic resource lookups are
    /// live, an already-open window repaints without being recreated.</para>
    ///
    /// <para><b>Deterministic, not magic.</b> The engine only touches dictionaries it
    /// created itself (tracked by reference), so host applications keep their own
    /// resources intact. Calling it twice is safe.</para>
    ///
    /// <code>
    /// // in App.OnStartup, before the first window is shown
    /// ThemeEngine.Apply(EcoTheme.Dark, EcoAccent.Indigo);
    /// </code>
    /// </summary>
    public static class ThemeEngine
    {
        private const string AssemblyName = "EcoUI";

        private static readonly Uri LightTokens = new Uri($"/{AssemblyName};component/Themes/Tokens.Light.xaml", UriKind.Relative);
        private static readonly Uri DarkTokens = new Uri($"/{AssemblyName};component/Themes/Tokens.Dark.xaml", UriKind.Relative);
        private static readonly Uri ControlStyles = new Uri($"/{AssemblyName};component/Themes/Controls.xaml", UriKind.Relative);

        private static ResourceDictionary? _tokenDictionary;
        private static ResourceDictionary? _styleDictionary;
        private static bool _applying;

        /// <summary>Raised after a theme or accent change has been applied.</summary>
        public static event EventHandler? ThemeChanged;

        /// <summary>Currently active colour scheme.</summary>
        public static EcoTheme CurrentTheme { get; private set; } = EcoTheme.Dark;

        /// <summary>Currently active accent ramp.</summary>
        public static EcoAccent CurrentAccent { get; private set; } = EcoAccent.Blue;

        /// <summary>Base colour of the active accent (handy for charts drawn in code).</summary>
        public static Color CurrentAccentColor => GetAccentColor(CurrentAccent);

        /// <summary>
        /// Applies a theme to <see cref="Application.Current"/>.
        /// Safe to call before any window exists; with no <see cref="Application"/>
        /// (unit tests, designers) the selection is recorded but nothing is merged.
        /// </summary>
        public static void Apply(EcoTheme theme, EcoAccent accent = EcoAccent.Blue) =>
            Apply(Application.Current, theme, accent);

        /// <summary>
        /// Applies a theme to a specific <see cref="Application"/> instance, for hosts
        /// that manage their own lifetime (tests, plugin hosts, design tools).
        /// </summary>
        public static void Apply(Application? application, EcoTheme theme, EcoAccent accent)
        {
            if (_applying)
            {
                return;
            }

            _applying = true;

            try
            {
                CurrentTheme = theme;
                CurrentAccent = accent;

                if (application is null)
                {
                    return;
                }

                ResourceDictionaryCollection merged = application.Resources.MergedDictionaries;

                // Swap only the dictionaries this engine owns: the host application's
                // own merged dictionaries keep their order and precedence.
                if (_tokenDictionary is not null)
                {
                    merged.Remove(_tokenDictionary);
                }

                _tokenDictionary = new ResourceDictionary
                {
                    Source = theme == EcoTheme.Dark ? DarkTokens : LightTokens
                };
                merged.Add(_tokenDictionary);

                // The standard control styles are theme-independent (they read tokens),
                // so they only ever need merging once.
                if (_styleDictionary is null)
                {
                    _styleDictionary = new ResourceDictionary { Source = ControlStyles };
                    merged.Add(_styleDictionary);
                }

                ApplyAccent(application.Resources, accent, theme);
            }
            finally
            {
                _applying = false;
            }

            ThemeChanged?.Invoke(null, EventArgs.Empty);
        }

        /// <summary>Switches between <see cref="EcoTheme.Light"/> and <see cref="EcoTheme.Dark"/>.</summary>
        public static void ToggleTheme() =>
            Apply(CurrentTheme == EcoTheme.Dark ? EcoTheme.Light : EcoTheme.Dark, CurrentAccent);

        /// <summary>Base colour of an accent ramp.</summary>
        public static Color GetAccentColor(EcoAccent accent) => accent switch
        {
            EcoAccent.Indigo => Color.FromRgb(0x63, 0x66, 0xF1),
            EcoAccent.Violet => Color.FromRgb(0x8B, 0x5C, 0xF6),
            EcoAccent.Cyan => Color.FromRgb(0x0E, 0xA5, 0xE9),
            EcoAccent.Teal => Color.FromRgb(0x14, 0xB8, 0xA6),
            EcoAccent.Green => Color.FromRgb(0x22, 0xC5, 0x5E),
            EcoAccent.Amber => Color.FromRgb(0xF5, 0x9E, 0x0B),
            EcoAccent.Rose => Color.FromRgb(0xF4, 0x3F, 0x5E),
            _ => Color.FromRgb(0x3B, 0x82, 0xF6)
        };

        // ---------------------------------------------------------------------
        // Internals
        // ---------------------------------------------------------------------

        /// <summary>
        /// Writes the five accent tokens as direct application resources. Direct
        /// entries take precedence over every merged dictionary, which is what makes an
        /// accent change override the token file without rewriting it.
        /// </summary>
        private static void ApplyAccent(ResourceDictionary resources, EcoAccent accent, EcoTheme theme)
        {
            Color baseColor = GetAccentColor(accent);

            // Hover and pressed move towards white on dark surfaces and towards black
            // on light ones - that is what keeps the feedback readable in both modes.
            Color hover = theme == EcoTheme.Dark
                ? Blend(baseColor, Colors.White, 0.20)
                : Blend(baseColor, Colors.Black, 0.12);

            Color pressed = theme == EcoTheme.Dark
                ? Blend(baseColor, Colors.Black, 0.16)
                : Blend(baseColor, Colors.Black, 0.26);

            Color soft = theme == EcoTheme.Dark
                ? Blend(baseColor, Color.FromRgb(0x15, 0x17, 0x1C), 0.82)
                : Blend(baseColor, Colors.White, 0.88);

            Color onAccent = Luminance(baseColor) > 0.62 ? Colors.Black : Colors.White;

            resources["Eco.Accent"] = FrozenBrush(baseColor);
            resources["Eco.Accent.Hover"] = FrozenBrush(hover);
            resources["Eco.Accent.Pressed"] = FrozenBrush(pressed);
            resources["Eco.Accent.Soft"] = FrozenBrush(soft);
            resources["Eco.OnAccent"] = FrozenBrush(onAccent);
        }

        private static SolidColorBrush FrozenBrush(Color color)
        {
            SolidColorBrush brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        /// <summary>Linear blend; <paramref name="amount"/> is the weight of <paramref name="to"/>.</summary>
        private static Color Blend(Color from, Color to, double amount)
        {
            double t = Math.Clamp(amount, 0d, 1d);
            return Color.FromRgb(
                (byte)Math.Round(from.R + ((to.R - from.R) * t)),
                (byte)Math.Round(from.G + ((to.G - from.G) * t)),
                (byte)Math.Round(from.B + ((to.B - from.B) * t)));
        }

        /// <summary>Rec. 709 luminance of an sRGB colour, 0..1.</summary>
        private static double Luminance(Color color)
        {
            double r = color.R / 255d;
            double g = color.G / 255d;
            double b = color.B / 255d;
            return (0.2126d * r) + (0.7152d * g) + (0.0722d * b);
        }
    }
}
