# EcoUI — a small, themeable WPF component library

`clients/desktop_windows` contains a reusable WPF UI toolkit and a tiny demo that
consumes it. There is **no business logic here**: no networking, no crypto, no
domain view models, no application shell. Just controls, design tokens and the
styles that tie them together.

```
clients/desktop_windows/
├── EcoUI.sln                     both projects, buildable with one command
├── Directory.Build.props         shared TFM (net8.0-windows), LangVersion, nullable
├── EcoUI/                        the library
│   ├── EcoWindow.cs              frameless window base with native behaviour
│   ├── ThemeEngine.cs            dark/light + accent ramps, runtime swapping
│   ├── ThemeInfo.cs              tells WPF where the default themes live
│   ├── Controls/
│   │   ├── EcoTerminal.cs        virtualised, bounded log viewer
│   │   ├── EcoStatCard.cs        metric tile (title / value / subtext / trend)
│   │   ├── EcoStatusBadge.cs     dot + optional label, five semantic states
│   │   └── EcoLogEntry.cs        display model for one terminal line
│   ├── Interop/NativeFrame.cs    DWM dark frame + rounded corners (cosmetic)
│   └── Themes/
│       ├── Generic.xaml          default templates for every Eco* control
│       ├── Controls.xaml         implicit styles for Button/TextBox/ComboBox/…
│       ├── Tokens.Dark.xaml      semantic tokens, dark
│       └── Tokens.Light.xaml     the same keys, light
└── EcoUI.Demo/                   ~85-line consumer that proves reusability
    ├── App.xaml(.cs)             one ThemeEngine.Apply call, nothing else
    └── MainWindow.xaml(.cs)      derives from EcoWindow, uses the controls
```

## Build & run

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) on
Windows.

```powershell
cd clients\desktop_windows
dotnet build                          # library + demo
dotnet run --project EcoUI.Demo       # launch the demo
```

## Using the library

1. Reference `EcoUI.csproj` from your app.
2. Call `ThemeEngine.Apply` **before** the first window is created (so
   `StaticResource` lookups in your XAML resolve):

```csharp
protected override void OnStartup(StartupEventArgs e)
{
    base.OnStartup(e);
    ThemeEngine.Apply(EcoTheme.Dark, EcoAccent.Indigo);
    new MainWindow().Show();
}
```

3. Derive your shell from `EcoWindow`:

```xml
<eco:EcoWindow x:Class="MyApp.MainWindow"
               xmlns:eco="clr-namespace:EcoUI;assembly=EcoUI"
               Title="My app" Width="900" Height="600">
    <Grid Margin="20">
        <controls:EcoStatCard Title="Requests" Value="1,284" Trend="+4.1%" TrendKind="Positive" />
    </Grid>
</eco:EcoWindow>
```

That is the whole integration. There is no base view model, no service locator and
no required directory structure.

## Controls

### `EcoWindow`

`WindowStyle=None` without `AllowsTransparency`, so the window stays hardware
accelerated. Drag, Aero Snap (edges and corners), double-click maximise, the
maximise animation and DPI-correct resize borders all come from
[`WindowChrome`](https://learn.microsoft.com/dotnet/desktop/wpf/windows/windowchrome) —
the real non-client code paths rather than a `DragMove()` imitation.

| Property | Type | Default | Purpose |
|---|---|---|---|
| `TitleBarHeight` | `double` | `44` | Title bar height *and* the native caption hit area |
| `IsTitleBarVisible` | `bool` | `true` | Hide the title bar (caption height becomes 0) |
| `TitleBarContent` | `object` | `null` | Extra title bar content (search box, tabs, …) |
| `ShowMinimizeButton` / `ShowMaximizeButton` | `bool` | `true` | Which buttons to show |
| `IsMaximized` | `bool` (read-only) | `false` | Drives the restore glyph |

Template parts: `PART_MinimizeButton`, `PART_MaximizeButton`, `PART_CloseButton`.
Restyle the window freely — as long as those three names survive, the buttons keep
working.

The DWM helper only adjusts *appearance* (dark frame attribute, Windows 11 rounded
corners, un-rounded when maximised). Every call is best-effort and guarded.

### `EcoTerminal`

A bounded, virtualised console view.

```xml
<controls:EcoTerminal x:Name="Log" MaxEntries="500" />
```

```csharp
Log.Append("Service started", EcoLogLevel.Success);   // callable from any thread
Log.Clear();
```

| Property | Type | Default | Purpose |
|---|---|---|---|
| `MaxEntries` | `int` | `1000` | Ring-buffer size; oldest lines are dropped |
| `AutoScroll` | `bool` | `true` | Follow the tail (detaches when the user scrolls up) |
| `ShowTimestamps` | `bool` | `true` | Show the time gutter |
| `Entries` | `ReadOnlyObservableCollection<EcoLogEntry>` | — | Bindable view of the buffer |

`Append` marshals background calls onto the control's dispatcher, so producers do
not need to know that a UI exists. The template renders through a
`VirtualizingStackPanel` with recycling: cost scales with the viewport, not with
the buffer.

### `EcoStatCard`

| Property | Type | Purpose |
|---|---|---|
| `Title` | `string` | Small label above the value |
| `Value` | `string` | Headline value (pre-formatted by you) |
| `Subtext` | `string` | Supporting line |
| `Trend` | `string` | Chip text, e.g. `+8.2%`; hidden while empty |
| `TrendKind` | `EcoTrendKind` | `None`, `Positive`, `Negative`, `Neutral` |

### `EcoStatusBadge`

| Property | Type | Default | Purpose |
|---|---|---|---|
| `Status` | `EcoStatus` | `Idle` | `Idle`, `Info`, `Success`, `Warning`, `Error` |
| `Text` | `string` | `""` | Label next to the dot |
| `ShowText` | `bool` | `true` | Hide the label, keep the dot |

## Theming

Colours are *semantic tokens*, not hues, so both themes are interchangeable:

| Group | Keys |
|---|---|
| Surfaces | `Eco.Surface.Window` · `.Panel` · `.Raised` · `.Sunken` · `.Hover` · `.Pressed` |
| Borders | `Eco.Border` · `Eco.Border.Strong` · `Eco.Border.Subtle` |
| Text | `Eco.Text.Primary` · `.Secondary` · `.Muted` · `.Inverse` |
| Accent | `Eco.Accent` · `.Hover` · `.Pressed` · `.Soft` · `Eco.OnAccent` |
| Status | `Eco.Success/.Soft` · `Warning/.Soft` · `Error/.Soft` · `Info/.Soft` · `Idle/.Soft` |
| Type | `Eco.Font.Ui` · `Eco.Font.Display` · `Eco.Font.Mono` |

`ThemeEngine` merges `Tokens.<theme>.xaml` and `Controls.xaml` into
`Application.Resources`, then writes the five accent keys directly so the selected
accent outranks the token file. All controls use `DynamicResource`, so
`ThemeEngine.Apply(...)` / `ThemeEngine.ToggleTheme()` repaint open windows live.

```csharp
ThemeEngine.Apply(EcoTheme.Light, EcoAccent.Teal);
ThemeEngine.ToggleTheme();                 // dark ↔ light, same accent
ThemeEngine.ThemeChanged += (_, _) => { }; // react (e.g. redraw custom charts)
ThemeEngine.CurrentAccentColor;            // Color, for code-drawn visuals
```

Accents: `Blue`, `Indigo`, `Violet`, `Cyan`, `Teal`, `Green`, `Amber`, `Rose`.
Hover/pressed shades are derived per theme (lighter on dark, darker on light), and
`Eco.OnAccent` flips to dark text automatically for bright accents such as Amber.

To override a token for your own product, set it *after* `Apply`:

```csharp
Application.Current.Resources["Eco.Accent"] = new SolidColorBrush(Color.FromRgb(0x0F, 0x9D, 0x58));
```

## Standard control styles

Merging `Controls.xaml` gives implicit styles for `Button`, `TextBox`,
`ComboBox` (+ `ComboBoxItem`), `CheckBox`, `ScrollBar`, `Thumb` and `RepeatButton`,
so a plain `<Button Content="Save" />` already matches the theme. Named variants:

* `Eco.PrimaryButton` — filled with the accent ramp, one per surface
* `Eco.SubtleButton` — borderless, for toolbars
* `Eco.DangerButton` — error ramp
* `Eco.Card`, `Eco.Divider`
* `Eco.Text.Heading`, `.Subtitle`, `.Caption`, `.Label`, `.Mono`

The ComboBox template is for non-editable usage (`IsEditable="False"`, the
default); if you need an editable combo box, provide your own template.

## Design notes

* **No `AllowsTransparency`.** Layered windows lose hardware acceleration. Rounded
  corners come from DWM on Windows 11 instead of a fake rounded border.
* **`WindowChrome` for behaviour.** Native snap and resize are correct across DPI
  and multi-monitor setups precisely because the OS still owns them.
* **No custom converters.** Visibility and colour decisions use triggers on typed
  properties (`HasTrend`, `Status`, `TrendKind`), which keeps bindings trivial and
  theme-aware.
* **No reflection, no code generation, no third-party dependencies.** Only
  `PresentationFramework`.
* **Bounded by construction.** The terminal caps its buffer; the window and
  controls hold no static state.

## Verification

The WPF layer cannot be compiled on Linux, so this tree is validated statically:

* all 10 C# files parse cleanly against the Roslyn grammar;
* every XAML/props/csproj/manifest file is well-formed XML;
* every `StaticResource` resolves and is declared before use, every
  `DynamicResource` key exists (or is written at runtime by `ThemeEngine`);
* `x:Name` namescopes are consistent, and every `TargetName`/`ElementName` resolves;
* every attribute on an `eco:`/`controls:` element is a real property of that type,
  and every enum literal (`Status="Success"`, `x:Static …EcoLogLevel.Error`) exists;
* every `[TemplatePart]` and `GetTemplateChild` string matches an `x:Name`, and
  every `DependencyProperty` has a matching CLR property;
* the demo's XAML root matches its code-behind base type (`EcoWindow`);
* `Tokens.Dark.xaml` and `Tokens.Light.xaml` declare the same key set.

Run `dotnet build` on Windows to compile and exercise it.

## License

Follows the repository's license.
