using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Scoreboard.Core;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Scoreboard.Desktop;

public sealed class UserPreferences
{
    /// <summary>Bumped when the shipped defaults change, so older files take the new defaults once.</summary>
    public int Schema { get; set; }

    /// <summary>"System" follows Windows' app theme; "Dark" and "Light" pin one.</summary>
    public string Theme { get; set; } = "System";
    public bool ShowHeadingHints { get; set; }

    /// <summary>The operator interface's accent: tab outlines, selected rows, live badges.</summary>
    public string AccentColor { get; set; } = "#FF38D7A3";

    /// <summary>The filled actions — Update Output, confirm buttons, an on toggle.</summary>
    public string AccentColor2 { get; set; } = "#FF0B6B50";

    /// <summary>Off until the operator turns the phone remote on and gives it a password.</summary>
    public bool RemoteEnabled { get; set; }
    public int RemotePort { get; set; } = 8080;
    public string RemotePassword { get; set; } = "";

    /// <summary>
    /// A SHA-256 of the operator lock password, never the password itself. Empty means the lock
    /// button asks the operator to create one; a value means it asks to confirm before locking and
    /// to enter it again before the desk is released.
    /// </summary>
    public string OperatorLockPasswordHash { get; set; } = "";

    /// <summary>
    /// An optional override for where the installed app looks for releases. Empty means the
    /// built-in GitHub feed (prereleases included); a URL here points at a manifest on another host.
    /// </summary>
    public string UpdateFeedUrl { get; set; } = "";
    public bool CheckForUpdates { get; set; } = true;

    /// <summary>
    /// Next down prints the new down alone ("2nd Down") until the distance is set, instead of
    /// showing the previous series' distance beside it.
    /// </summary>
    public bool NextDownPreview { get; set; }

    /// <summary>
    /// Off by default: print the down line's words in full caps — "1st & GOAL", "2nd DOWN" —
    /// while the ordinal and the distance keep their normal case.
    /// </summary>
    public bool UppercaseDownWords { get; set; }

    /// <summary>New series goes straight to air instead of waiting for Update Output.</summary>
    public bool NewSeriesLive { get; set; }

    /// <summary>The pen width the operator last used, kept across shows.</summary>
    public double AnnotationWidth { get; set; } = 3;

    /// <summary>Seconds before annotation ink fades; zero keeps it until Clear.</summary>
    public double AnnotationFadeSeconds { get; set; } = 1;

    /// <summary>
    /// How long the graphic holds an old value before printing the new one, in seconds. 0 is
    /// instant. This is a show-wide timing choice, not a property of one element.
    /// </summary>
    public double ValueSwitchDelay { get; set; }

    /// <summary>
    /// Minutes between recovery points. Five is the default, 0 turns autosave off, and every
    /// positive value is a whole number of minutes.
    /// </summary>
    public int AutosaveMinutes { get; set; } = 5;

    /// <summary>
    /// How clocks print. "Padded" is 10:00, 09:59 and 0:59; "Compact" is 10:00, 9:59 and :59.
    /// </summary>
    public string ClockFormat { get; set; } = "Padded";

    /// <summary>
    /// Off by default: when the final period or full time expires, add the result to each team's
    /// win-loss-tie record. A side with no record is left alone.
    /// </summary>
    public bool AutoUpdateRecord { get; set; }

    /// <summary>The sport the operator last ran, so a launcher "New Game" opens the game they expect.</summary>
    public string LastSport { get; set; } = "football";

    /// <summary>
    /// The four quick-layout buttons above the sound meter. Each one points at a layout on any
    /// display, so the operator can reach a scorebug, a lower third or a sponsor board in one press.
    /// </summary>
    public List<QuickLayoutBinding> QuickLayouts { get; set; } = [];

    /// <summary>Twelve quick-media buttons above the quick layouts. Each one points at a library path.</summary>
    public List<QuickMediaBinding> QuickMedia { get; set; } = [];

    /// <summary>
    /// Off by default: a quick button only stages its layout. When this is on, the same press also
    /// sends it to air, which is faster but updates output without a second confirmation.
    /// </summary>
    public bool QuickLayoutsPublish { get; set; }

    /// <summary>Controller input and its connection details.</summary>
    public HardwareSettings Hardware { get; set; } = new();

    /// <summary>The built-in horn selected on the console, or Custom. The buzzer ships as the default.</summary>
    public string HornSound { get; set; } = nameof(Desktop.HornSound.Buzzer);

    /// <summary>Audio file used when <see cref="HornSound"/> is Custom.</summary>
    public string HornCustomPath { get; set; } = "";

    /// <summary>Off by default: sound the selected horn when the game clock expires.</summary>
    public bool HornOnGameClockZero { get; set; }

    /// <summary>Off by default: sound the selected horn when the play clock expires.</summary>
    public bool HornOnPlayClockZero { get; set; }

    public List<MediaEntry> Media { get; set; } = [];

    /// <summary>Saved media playlists: an ordered run of clips that can repeat.</summary>
    public List<MediaPlaylist> Playlists { get; set; } = [];

    /// <summary>Folders created before they hold a clip, so an empty folder stays on the shelf.</summary>
    public List<string> MediaFolders { get; set; } = [];

    /// <summary>Older preference files stored plain paths; they are migrated into <see cref="Media"/> on load.</summary>
    public List<string> MediaFiles { get; set; } = [];

    public UserPreferences Clone() => new()
    {
        Schema = Schema,
        Theme = Theme,
        ShowHeadingHints = ShowHeadingHints,
        AccentColor = AccentColor,
        AccentColor2 = AccentColor2,
        RemoteEnabled = RemoteEnabled,
        RemotePort = RemotePort,
        RemotePassword = RemotePassword,
        OperatorLockPasswordHash = OperatorLockPasswordHash,
        UpdateFeedUrl = UpdateFeedUrl,
        CheckForUpdates = CheckForUpdates,
        ValueSwitchDelay = ValueSwitchDelay,
        AutosaveMinutes = AutosaveMinutes,
        ClockFormat = ClockFormat,
        AutoUpdateRecord = AutoUpdateRecord,
        NextDownPreview = NextDownPreview,
        UppercaseDownWords = UppercaseDownWords,
        NewSeriesLive = NewSeriesLive,
        AnnotationWidth = AnnotationWidth,
        AnnotationFadeSeconds = AnnotationFadeSeconds,
        LastSport = LastSport,
        QuickLayouts = QuickLayouts.Select(binding => new QuickLayoutBinding { Label = binding.Label, DisplayId = binding.DisplayId, LayoutId = binding.LayoutId }).ToList(),
        QuickMedia = QuickMedia.Select(binding => new QuickMediaBinding { Label = binding.Label, Path = binding.Path }).ToList(),
        QuickLayoutsPublish = QuickLayoutsPublish,
        Hardware = Hardware.Clone(),
        HornSound = HornSound,
        HornCustomPath = HornCustomPath,
        HornOnGameClockZero = HornOnGameClockZero,
        HornOnPlayClockZero = HornOnPlayClockZero,
        Media = [.. Media],
        Playlists = Playlists.Select(p => new MediaPlaylist { Name = p.Name, Loop = p.Loop, Paths = p.Paths.ToList() }).ToList(),
        MediaFolders = [.. MediaFolders],
        MediaFiles = [.. MediaFiles]
    };
}

/// <summary>Hashes and checks the operator lock without ever storing the password in clear text.</summary>
public static class OperatorLock
{
    public static string Hash(string? password) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(password ?? string.Empty)));

    public static bool Matches(string? password, string? hash) =>
        !string.IsNullOrWhiteSpace(hash)
        && CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(Hash(password)),
            Encoding.ASCII.GetBytes(hash.Trim()));
}

/// <summary>One of the four quick-layout buttons: a name the operator sees, and where it points.</summary>
/// <summary>One quick-media button: a library path and the label the operator sees.</summary>
public sealed class QuickMediaBinding
{
    public string Label { get; set; } = "";
    public string Path { get; set; } = "";

    public bool IsSet => !string.IsNullOrWhiteSpace(Path);
}

public sealed class QuickLayoutBinding
{
    public string Label { get; set; } = "";
    public string DisplayId { get; set; } = "";
    public string LayoutId { get; set; } = "";

    public bool IsSet => !string.IsNullOrWhiteSpace(DisplayId) && !string.IsNullOrWhiteSpace(LayoutId);
}

public static class PreferencesStore
{
    static string? _overridePath;

    /// <summary>Test hook: aim storage at a throwaway file so a suite can never rewrite the operator's real preferences.</summary>
    public static void UsePath(string? path) => _overridePath = path;

    static string FilePath => _overridePath ?? AppPaths.Combine("preferences.json");

    public static UserPreferences Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new();
            var loaded = JsonSerializer.Deserialize<UserPreferences>(File.ReadAllText(FilePath)) ?? new();
            Migrate(loaded);
            ApplyClockFormat(loaded);
            return loaded;
        }
        catch { return new(); }
    }

    static void Migrate(UserPreferences preferences)
    {
        preferences.Media ??= [];
        preferences.Playlists ??= [];
        preferences.MediaFolders ??= [];
        preferences.MediaFiles ??= [];
        preferences.QuickLayouts ??= [];
        preferences.QuickMedia ??= [];
        preferences.Hardware ??= new HardwareSettings();
        if (string.IsNullOrWhiteSpace(preferences.HornSound)) preferences.HornSound = nameof(Desktop.HornSound.Buzzer);
        if (preferences.Media.Count == 0 && preferences.MediaFiles.Count > 0)
            preferences.Media = preferences.MediaFiles.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => new MediaEntry(p)).ToList();
        preferences.MediaFiles.Clear();

        // Files written before the schema existed carried the old light default, and schema 1 files
        // pinned the old dark default. Take the shipped defaults once — follow Windows, no heading
        // tips — then leave later choices alone.
        if (preferences.Schema < 2)
        {
            preferences.Theme = "System";
            preferences.ShowHeadingHints = false;
            preferences.Schema = 2;
            try { Save(preferences); } catch { }
        }
        // a1.6 ships the buzzer as the console's horn. A file still carrying the old air-horn
        // default takes the new one once; a deliberate Siren or Custom choice is left alone.
        if (preferences.Schema < 3)
        {
            if (string.Equals(preferences.HornSound, nameof(Desktop.HornSound.AirHorn), StringComparison.OrdinalIgnoreCase))
                preferences.HornSound = nameof(Desktop.HornSound.Buzzer);
            preferences.Schema = 3;
            try { Save(preferences); } catch { }
        }
        // A negative interval is not a setting; the shipped default is five minutes. Zero is a real
        // choice and means never autosave.
        if (preferences.AutosaveMinutes < 0) preferences.AutosaveMinutes = 5;
        // Annotation settings are real choices, but a malformed or out-of-range file must not make
        // the toolbar unusable. Zero fade is Permanent and is preserved.
        if (!double.IsFinite(preferences.AnnotationWidth)) preferences.AnnotationWidth = 3;
        preferences.AnnotationWidth = Math.Clamp(preferences.AnnotationWidth, 1, 24);
        if (!double.IsFinite(preferences.AnnotationFadeSeconds) || preferences.AnnotationFadeSeconds < 0)
            preferences.AnnotationFadeSeconds = 1;
        preferences.AnnotationFadeSeconds = Math.Clamp(preferences.AnnotationFadeSeconds, 0, 10);
        if (preferences.AnnotationFadeSeconds is > 0 and < 0.5) preferences.AnnotationFadeSeconds = 0.5;
        if (preferences.ClockFormat is not ("Padded" or "Compact")) preferences.ClockFormat = "Padded";
        if (preferences.Theme is not ("System" or "Dark" or "Light")) preferences.Theme = "System";
    }

    /// <summary>Pushes the saved clock shape into the binder every graphic, panel and remote reads.</summary>
    public static void ApplyClockFormat(UserPreferences preferences)
    {
        GameEngine.ClockDisplay = string.Equals(preferences.ClockFormat, "Compact", StringComparison.OrdinalIgnoreCase)
            ? ClockStyle.Compact
            : ClockStyle.Padded;
        GameEngine.AutoUpdateRecordEnabled = preferences.AutoUpdateRecord;
        GameEngine.UppercaseDownWordsEnabled = preferences.UppercaseDownWords;
    }

    public static void Save(UserPreferences preferences)
    {
        ApplyClockFormat(preferences);
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(preferences, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, FilePath, true);
    }
}

/// <summary>
/// Applies operator chrome colors (panels, inputs, buttons) for the selected theme.
/// Graphics that carry their own designed colors — the scorebug canvas and audience output —
/// are deliberately skipped so a theme switch never recolors on-air artwork.
/// </summary>
public static class ThemeManager
{
    // The last accent pair a window applied, kept so a preference read that fails mid-session does
    // not repaint every accent control in the stock green.
    static (Color Accent, Color Secondary)? _lastAccent;
    public static readonly DependencyProperty SkipThemeProperty =
        DependencyProperty.RegisterAttached("SkipTheme", typeof(bool), typeof(ThemeManager), new PropertyMetadata(false));

    public static void SetSkipTheme(DependencyObject element, bool value) => element.SetValue(SkipThemeProperty, value);
    public static bool GetSkipTheme(DependencyObject element) => (bool)element.GetValue(SkipThemeProperty);

    public static Color PanelColor(string theme) => Resolve(theme) == "Light" ? Color.FromRgb(255, 255, 255) : Color.FromRgb(23, 28, 37);

    /// <summary>
    /// Turns the stored choice into the theme to paint with. "System" reads Windows' app theme
    /// (Settings → Personalisation → Colours), so the operator surface can follow the desktop.
    /// </summary>
    public static string Resolve(string? theme)
    {
        if (string.Equals(theme, "Light", StringComparison.OrdinalIgnoreCase)) return "Light";
        if (!string.Equals(theme, "System", StringComparison.OrdinalIgnoreCase)) return "Dark";
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int light && light != 0 ? "Light" : "Dark";
        }
        catch { return "Dark"; }
    }

    public static void Apply(Window window, string theme)
    {
        var light = Resolve(theme) == "Light";
        var background = light ? "#FFF3F5F8" : "#FF000000";
        var panel = light ? "#FFFFFFFF" : "#FF080808";
        var surface = light ? "#FFE5EAF0" : "#FF141414";
        var foreground = light ? "#FF18202B" : "#FFF4F7FB";
        var muted = light ? "#FF586576" : "#FFB0B0B0";
        var input = light ? "#FFFFFFFF" : "#FF050505";
        var border = light ? "#FFB9C3CE" : "#FF383838";

        window.Background = Brush(background);
        window.Foreground = Brush(foreground);
        // The shared control styles read these. Setting them on the window means a button built
        // later — a stepper, a rebuilt sport row, a dialog's extra action — still matches the theme
        // instead of falling back to the startup shades.
        window.Resources["OperatorPanelBrush"] = Brush(panel);
        window.Resources["OperatorSurfaceBrush"] = Brush(surface);
        window.Resources["OperatorInputBrush"] = Brush(input);
        window.Resources["OperatorTextBrush"] = Brush(foreground);
        window.Resources["OperatorBorderBrush"] = Brush(border);

        // The operator's own accent pair: the outline colour and the filled-action colour. A failed
        // read keeps the last accent that worked rather than falling back to the stock green, which
        // made a saved accent look like it had reset itself while the app was busy.
        UserPreferences? preferences = null;
        try { preferences = PreferencesStore.Load(); } catch { }
        var accent = preferences is not null
            ? UiPalette.Accent(preferences)
            : _lastAccent?.Accent ?? UiPalette.Accent(new UserPreferences());
        var secondary = preferences is not null
            ? UiPalette.Secondary(preferences)
            : _lastAccent?.Secondary ?? UiPalette.Secondary(new UserPreferences());
        _lastAccent = (accent, secondary);
        window.Resources["Accent"] = UiPalette.Frozen(accent);
        window.Resources["AccentFillBrush"] = UiPalette.Frozen(UiPalette.Soft(accent));
        window.Resources["AccentActionBrush"] = UiPalette.Frozen(secondary);

        void ApplyTheme()
        {
            SetDescendants(window.Content as DependencyObject, panel, surface, foreground, input, border);
            ApplyAccent(window, accent, secondary);
        }
        window.Loaded += (_, _) => ApplyTheme();
        if (window.IsLoaded) window.Dispatcher.BeginInvoke(ApplyTheme);
        // A control built after the window loaded starts with the shipped green: the studio rebuilds
        // its panels when an element is moved, a property row is opened, a list is refilled. Painting
        // each newcomer as it loads keeps the operator's accent on what they are actually looking at
        // instead of letting those rebuilt pieces snap back to the default colour.
        window.AddHandler(FrameworkElement.LoadedEvent, new RoutedEventHandler((_, args) =>
        {
            if (args.OriginalSource is DependencyObject element) PaintAccent(element, accent, secondary);
        }), handledEventsToo: true);
    }

    static Brush Brush(string color) => (Brush)new BrushConverter().ConvertFromString(color)!;

    static void SetDescendants(DependencyObject? root, string panel, string surface, string foreground, string input, string border)
    {
        if (root is null) return;
        // Never repaint designed artwork: scorebug views and the studio canvas keep their own colors.
        if (root is ScorebugCanvasView) return;
        if (root is FrameworkElement { Name: "DesignCanvas" }) return;
        if (GetSkipTheme(root)) return;

        if (root is Border borderElement && borderElement.Background is SolidColorBrush) borderElement.Background = Brush(panel);
        if (root is Panel panelElement && panelElement.Background is SolidColorBrush) panelElement.Background = Brush(panel);
        if (root is TextBlock text && (text.Foreground is not SolidColorBrush solid || !PreserveTextColor(solid)))
            text.Foreground = Brush(foreground);
        if (root is ComboBoxItem comboItem)
        {
            comboItem.Foreground = Brush(foreground);
            comboItem.Background = Brush(surface);
        }
        if (root is Control control)
        {
            control.Foreground = Brush(foreground);
            switch (control)
            {
                case TextBox: control.Background = Brush(input); control.BorderBrush = Brush(border); break;
                case Button: control.Background = Brush(surface); control.BorderBrush = Brush(border); break;
                case ListBox: control.Background = Brush(input); control.BorderBrush = Brush(border); break;
                case ComboBox:
                    control.Background = Brush(input);
                    control.BorderBrush = Brush(border);
                    // Leave ItemContainerStyle alone. The application's ComboBox style already owns
                    // the dark popup and item template; replacing it here discarded that template and
                    // is what made the motion makers' drop-downs fall back to the white system list.
                    break;
                case ToggleButton: control.Background = Brush(surface); control.BorderBrush = Brush(border); break;
            }
        }
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            SetDescendants(VisualTreeHelper.GetChild(root, i), panel, surface, foreground, input, border);
    }

    static bool PreserveTextColor(SolidColorBrush brush) =>
        brush.Color == Color.FromRgb(56, 215, 163) || brush.Color == Color.FromRgb(255, 209, 102) || brush.Color == Color.FromRgb(255, 107, 107);

    /// <summary>
    /// Repaints the accent shades after the theme pass. Every operator window is built with the
    /// shipped green, so a chosen colour is applied by swapping those exact shades — outlines and
    /// active-tab borders to the accent, filled actions to the secondary. Designed artwork (the
    /// scorebug canvas, the studio artboard) is never touched.
    /// </summary>
    static void ApplyAccent(DependencyObject? root, Color accent, Color secondary)
    {
        if (root is null) return;
        if (root is ScorebugCanvasView) return;
        if (root is FrameworkElement { Name: "DesignCanvas" }) return;
        PaintAccent(root, accent, secondary);
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            ApplyAccent(VisualTreeHelper.GetChild(root, i), accent, secondary);
    }

    /// <summary>Paints one element (not its children) with the operator's accent, for a newcomer.</summary>
    static void PaintAccent(DependencyObject root, Color accent, Color secondary)
    {
        if (root is ScorebugCanvasView) return;
        if (root is FrameworkElement { Name: "DesignCanvas" }) return;

        Color? Map(Color color)
        {
            if (color.R == 56 && color.G == 215 && color.B == 163) return accent;
            if (color.R == 18 && color.G == 60 && color.B == 51) return UiPalette.Soft(accent);
            if (color.R == 13 && color.G == 42 && color.B == 34) return UiPalette.Soft(accent);
            if (color.R == 11 && color.G == 107 && color.B == 80) return secondary;
            if (color.R == 20 && color.G == 166 && color.B == 124) return UiPalette.Lift(secondary);
            return null;
        }

        switch (root)
        {
            case Border border when border.Background is SolidColorBrush background && Map(background.Color) is Color bordered:
                border.Background = UiPalette.Frozen(bordered);
                break;
            case Panel panelElement when panelElement.Background is SolidColorBrush panelBackground && Map(panelBackground.Color) is Color filled:
                panelElement.Background = UiPalette.Frozen(filled);
                break;
            case System.Windows.Shapes.Shape shape when shape.Fill is SolidColorBrush fill && Map(fill.Color) is Color shapeColor:
                shape.Fill = UiPalette.Frozen(shapeColor);
                break;
            case TextBlock text when text.Foreground is SolidColorBrush textColor && Map(textColor.Color) is Color mappedText:
                text.Foreground = UiPalette.Frozen(mappedText);
                break;
        }
        if (root is Control control)
        {
            if (control.Background is SolidColorBrush controlBackground && Map(controlBackground.Color) is Color controlFill)
                control.Background = UiPalette.Frozen(controlFill);
            if (control.BorderBrush is SolidColorBrush controlBorder && Map(controlBorder.Color) is Color controlEdge)
                control.BorderBrush = UiPalette.Frozen(controlEdge);
        }
    }
}

/// <summary>
/// The operator's two interface colours, with the shades derived from them that the chrome needs.
/// A colour pasted into Preferences is validated here, so a typo can never leave a window grey.
/// </summary>
public static class UiPalette
{
    public static Color Accent(UserPreferences preferences) => Parse(preferences.AccentColor, Color.FromRgb(0x38, 0xD7, 0xA3));
    public static Color Secondary(UserPreferences preferences) => Parse(preferences.AccentColor2, Color.FromRgb(0x0B, 0x6B, 0x50));

    public static Color Parse(string? value, Color fallback)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(value) && value[0] == '#')
            {
                var parsed = (Color)ColorConverter.ConvertFromString(value)!;
                // A fully transparent accent would erase the outline it is meant to draw.
                return parsed.A < 0x40 ? Color.FromArgb(0xFF, parsed.R, parsed.G, parsed.B) : parsed;
            }
        }
        catch { }
        return fallback;
    }

    /// <summary>A deep shade of the accent, for an active tab or a badge behind it.</summary>
    public static Color Soft(Color color) => Mix(color, Color.FromRgb(0, 0, 0), 0.82);

    /// <summary>A brighter edge around a filled action.</summary>
    public static Color Lift(Color color) => Mix(color, Color.FromRgb(255, 255, 255), 0.22);

    public static Color Mix(Color a, Color b, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        return Color.FromRgb(
            (byte)Math.Round(a.R * (1 - amount) + b.R * amount),
            (byte)Math.Round(a.G * (1 - amount) + b.G * amount),
            (byte)Math.Round(a.B * (1 - amount) + b.B * amount));
    }

    public static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
