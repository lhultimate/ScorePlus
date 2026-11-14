using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Scoreboard.Core;
namespace Scoreboard.Desktop;
public partial class SettingsWindow : Window
{
    /// <summary>The longest switch delay the graphic accepts, in seconds.</summary>
    const double MaxDelay = 5;

    readonly UserPreferences _original;
    readonly UserPreferences _draft;
    bool _loading = true;
    bool _deleteArmed;
    /// <summary>Set once a value was saved without closing, so closing later keeps it.</summary>
    bool _committed;

    public SettingsWindow(UserPreferences prefs)
    {
        InitializeComponent();
        _original = prefs;
        _draft = prefs.Clone();
        ThemeSelector.ItemsSource = new[] { "System", "Dark", "Light" };
        ThemeSelector.SelectedItem = ThemeSelector.Items.Contains(_draft.Theme) ? _draft.Theme : "System";
        ShowHints.IsChecked = _draft.ShowHeadingHints;
        QuickPublishInput.IsChecked = _draft.QuickLayoutsPublish;
        DownPreviewInput.IsChecked = _draft.NextDownPreview;
        UppercaseDownInput.IsChecked = _draft.UppercaseDownWords;
        NewSeriesInput.IsChecked = _draft.NewSeriesLive;
        GameClockHornInput.IsChecked = _draft.HornOnGameClockZero;
        PlayClockHornInput.IsChecked = _draft.HornOnPlayClockZero;
        DelayInput.Text = _draft.ValueSwitchDelay.ToString("0.##", CultureInfo.CurrentCulture);
        AutosaveMinutesInput.Text = _draft.AutosaveMinutes.ToString(CultureInfo.InvariantCulture);
        ClockFormatInput.SelectedIndex = string.Equals(_draft.ClockFormat, "Compact", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        AutoRecordInput.IsChecked = _draft.AutoUpdateRecord;
        TieRuleInput.SelectedIndex = string.Equals(_draft.TieRule, "AllowTie", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        TiePeriodInput.Text = _draft.TieAllowPeriod.ToString(CultureInfo.InvariantCulture);
        TiePeriodInput.IsEnabled = TieRuleInput.SelectedIndex == 1;
        OvertimeFinalInput.IsChecked = _draft.ShowOvertimeFinal;
        AccentInput.Text = _draft.AccentColor;
        Accent2Input.Text = _draft.AccentColor2;
        PaintSwatches();
        RemoteEnabledInput.IsChecked = _draft.RemoteEnabled;
        RemotePasswordInput.Password = _draft.RemotePassword;
        RemotePortInput.Text = _draft.RemotePort.ToString(CultureInfo.InvariantCulture);
        CheckUpdatesInput.IsChecked = _draft.CheckForUpdates;
        UpdateFeedInput.Text = _draft.UpdateFeedUrl;
        _loading = false;
        UpdateAutosaveStatus();
        UpdateLockStatus();
        UpdateRemoteStatus();
        UpdateStatus.Text = AppInfo.IsInstalled
            ? $"Installed build {AppInfo.Release}. Updates are checked at launch when a feed is set."
            : "This copy runs from a folder, so it does not check for updates. Install ScorePlus to enable updates.";
    }

    // ---------- interface colours ----------

    void PaintSwatches()
    {
        AccentSwatch.Background = new System.Windows.Media.SolidColorBrush(UiPalette.Parse(_draft.AccentColor, System.Windows.Media.Color.FromRgb(0x38, 0xD7, 0xA3)));
        Accent2Swatch.Background = new System.Windows.Media.SolidColorBrush(UiPalette.Parse(_draft.AccentColor2, System.Windows.Media.Color.FromRgb(0x0B, 0x6B, 0x50)));
    }

    /// <summary>A typed hex value is used as soon as it is a real colour; half-typed text waits.</summary>
    void Accent_Changed(object sender, TextChangedEventArgs e)
    {
        if (_loading || AccentInput is null || Accent2Input is null) return;
        if (IsColor(AccentInput.Text)) _draft.AccentColor = AccentInput.Text.Trim();
        if (IsColor(Accent2Input.Text)) _draft.AccentColor2 = Accent2Input.Text.Trim();
        PaintSwatches();
    }

    static bool IsColor(string? text)
    {
        var value = (text ?? "").Trim();
        if (value.Length < 4 || value[0] != '#') return false;
        var digits = value[1..];
        return digits.Length is 3 or 4 or 6 or 8 && digits.All(Uri.IsHexDigit);
    }

    void AccentPick_Click(object sender, RoutedEventArgs e)
    {
        if (ColorPickerWindow.Pick(this, _draft.AccentColor) is not string picked) return;
        _draft.AccentColor = picked;
        AccentInput.Text = picked;
        PaintSwatches();
        ThemeManager.Apply(this, _draft.Theme);
    }

    void Accent2Pick_Click(object sender, RoutedEventArgs e)
    {
        if (ColorPickerWindow.Pick(this, _draft.AccentColor2) is not string picked) return;
        _draft.AccentColor2 = picked;
        Accent2Input.Text = picked;
        PaintSwatches();
        ThemeManager.Apply(this, _draft.Theme);
    }

    void ResetColors_Click(object sender, RoutedEventArgs e)
    {
        _draft.AccentColor = "#FF38D7A3";
        _draft.AccentColor2 = "#FF0B6B50";
        AccentInput.Text = _draft.AccentColor;
        Accent2Input.Text = _draft.AccentColor2;
        PaintSwatches();
        ThemeManager.Apply(this, _draft.Theme);
    }

    // ---------- remote control ----------

    /// <summary>Explains the lock state as the operator types, without revealing the stored password.</summary>
    void Lock_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading || OperatorLockStatus is null) return;
        OperatorLockPasswordInput.IsEnabled = ClearOperatorLockInput.IsChecked != true;
        OperatorLockConfirmInput.IsEnabled = ClearOperatorLockInput.IsChecked != true;
        UpdateLockStatus();
    }

    void UpdateLockStatus()
    {
        if (OperatorLockStatus is null) return;
        if (ClearOperatorLockInput.IsChecked == true)
        {
            OperatorLockStatus.Text = "The lock password will be removed when you save.";
            return;
        }
        var first = OperatorLockPasswordInput.Password;
        var second = OperatorLockConfirmInput.Password;
        if (first.Length == 0 && second.Length == 0)
        {
            OperatorLockStatus.Text = string.IsNullOrWhiteSpace(_draft.OperatorLockPasswordHash)
                ? "No lock password is set. Type it twice to add one."
                : "A lock password is set. Leave both boxes empty to keep it, or type a new one twice to replace it.";
            return;
        }
        if (first != second)
        {
            OperatorLockStatus.Text = "The two passwords do not match yet.";
            return;
        }
        OperatorLockStatus.Text = "The new lock password is ready to save.";
    }

    /// <summary>Applies the typed lock password, refusing to save a mismatch. Blank keeps the old one.</summary>
    bool ApplyLockPassword()
    {
        if (ClearOperatorLockInput.IsChecked == true)
        {
            _draft.OperatorLockPasswordHash = "";
            return true;
        }
        var first = OperatorLockPasswordInput.Password;
        var second = OperatorLockConfirmInput.Password;
        if (first.Length == 0 && second.Length == 0) return true;
        if (first != second)
        {
            OperatorLockStatus.Text = "The two lock passwords do not match. They were not saved.";
            return false;
        }
        _draft.OperatorLockPasswordHash = OperatorLock.Hash(first);
        return true;
    }

    void Remote_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading || RemoteEnabledInput is null) return;
        _draft.RemoteEnabled = RemoteEnabledInput.IsChecked == true;
        _draft.RemotePassword = RemotePasswordInput.Password;
        if (int.TryParse(RemotePortInput.Text.Trim(), out var port) && port is >= 1024 and <= 65535) _draft.RemotePort = port;
        UpdateRemoteStatus();
    }

    void Remote_Changed(object sender, TextChangedEventArgs e) => Remote_Changed(sender, (RoutedEventArgs)e);

    void UpdateRemoteStatus()
    {
        if (!_draft.RemoteEnabled)
        {
            RemoteStatus.Text = "Remote is off. Tick the box and set a password to serve it.";
            return;
        }
        if (string.IsNullOrWhiteSpace(_draft.RemotePassword))
        {
            RemoteStatus.Text = "Add a password — the remote stays off until it has one.";
            return;
        }
        var urls = RemoteControlServer.Urls(_draft.RemotePort);
        // The network address is the one a phone types; localhost is listed too, for this computer.
        var lan = urls.Count > 1 ? urls[^1] : urls[0];
        RemoteStatus.Text = $"Ready on port {_draft.RemotePort}. On this computer http://localhost:{_draft.RemotePort} · on a phone on the same Wi-Fi {lan}";
    }

    void OpenRemote_Click(object sender, RoutedEventArgs e)
    {
        // The address on the network, not localhost: the operator usually copies it from the URL
        // bar onto a phone, and a localhost URL is useless there.
        var urls = RemoteControlServer.Urls(_draft.RemotePort);
        var url = urls.Count > 1 ? urls[^1] : urls[0];
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { RemoteStatus.Text = $"Could not open a browser: {ex.Message}"; }
    }

    // ---------- updates ----------

    void Update_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading || CheckUpdatesInput is null || UpdateFeedInput is null) return;
        _draft.CheckForUpdates = CheckUpdatesInput.IsChecked == true;
        _draft.UpdateFeedUrl = UpdateFeedInput.Text.Trim();
    }

    void Update_Changed(object sender, TextChangedEventArgs e) => Update_Changed(sender, (RoutedEventArgs)e);

    async void CheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        if (!AppInfo.IsInstalled)
        {
            UpdateStatus.Text = "This copy runs from a folder, so it does not update itself. Use the installer, then updates work here.";
            return;
        }
        var feed = UpdateFeed.Resolve(UpdateFeedInput.Text);
        UpdateStatus.Text = string.IsNullOrWhiteSpace(UpdateFeedInput.Text)
            ? "Checking the built-in feed…"
            : "Checking…";
        var manifest = await UpdateChecker.CheckAsync(feed);
        UpdateStatus.Text = manifest is null
            ? $"You are on the newest version ({AppInfo.Release})."
            : $"Version {manifest.Label ?? manifest.Version} is ready. {(string.IsNullOrWhiteSpace(manifest.Notes) ? "" : manifest.Notes)}";
    }

    void Theme_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading && ThemeSelector.SelectedItem is string theme) _draft.Theme = theme;
    }

    void Controls_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading || ShowHints is null) return;
        _draft.ShowHeadingHints = ShowHints.IsChecked == true;
    }

    void DownPreview_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading || DownPreviewInput is null) return;
        _draft.NextDownPreview = DownPreviewInput.IsChecked == true;
    }

    void NewSeries_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading || NewSeriesInput is null) return;
        _draft.NewSeriesLive = NewSeriesInput.IsChecked == true;
    }

    /// <summary>A quick button either stages or publishes; the choice is saved with everything else.</summary>
    void QuickLayout_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading || QuickPublishInput is null) return;
        _draft.QuickLayoutsPublish = QuickPublishInput.IsChecked == true;
    }

    void UppercaseDown_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading || UppercaseDownInput is null) return;
        _draft.UppercaseDownWords = UppercaseDownInput.IsChecked == true;
    }

    void ClockHorn_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading || GameClockHornInput is null || PlayClockHornInput is null) return;
        _draft.HornOnGameClockZero = GameClockHornInput.IsChecked == true;
        _draft.HornOnPlayClockZero = PlayClockHornInput.IsChecked == true;
    }

    /// <summary>
    /// Any value the operator types is used as-is inside 0–5, so 0.35 s is a real answer rather
    /// than the nearest item in a fixed list. Half-typed text ("", "0.") leaves the draft alone.
    /// </summary>
    void Delay_Changed(object sender, TextChangedEventArgs e)
    {
        if (_loading || DelayInput is null) return;
        if (!double.TryParse(DelayInput.Text.Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out var seconds)) return;
        _draft.ValueSwitchDelay = Math.Clamp(seconds, 0, MaxDelay);
    }

    void ClockFormat_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ClockFormatInput is null) return;
        _draft.ClockFormat = ClockFormatInput.SelectedIndex == 1 ? "Compact" : "Padded";
    }

    void AutoRecord_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading || AutoRecordInput is null) return;
        _draft.AutoUpdateRecord = AutoRecordInput.IsChecked == true;
    }

    void TieRule_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || TieRuleInput is null || TiePeriodInput is null) return;
        _draft.TieRule = TieRuleInput.SelectedIndex == 1 ? "AllowTie" : "PlayUntilWinner";
        TiePeriodInput.IsEnabled = TieRuleInput.SelectedIndex == 1;
    }

    void TiePeriod_Changed(object sender, TextChangedEventArgs e)
    {
        if (_loading || TiePeriodInput is null) return;
        if (int.TryParse(TiePeriodInput.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var period))
            _draft.TieAllowPeriod = Math.Clamp(period, 1, 99);
    }

    void OvertimeFinal_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading || OvertimeFinalInput is null) return;
        _draft.ShowOvertimeFinal = OvertimeFinalInput.IsChecked == true;
    }

    void Autosave_Changed(object sender, TextChangedEventArgs e)
    {
        if (_loading || AutosaveMinutesInput is null) return;
        if (!int.TryParse(AutosaveMinutesInput.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes)) return;
        _draft.AutosaveMinutes = Math.Max(0, minutes);
        UpdateAutosaveStatus();
    }

    void UpdateAutosaveStatus()
    {
        if (AutosaveStatus is null) return;
        var minutes = _draft.AutosaveMinutes;
        AutosaveStatus.Text = minutes <= 0
            ? "Autosave is off. Existing recovery points are kept."
            : minutes == 1
                ? "A recovery point is written every minute while you work."
                : $"A recovery point is written every {minutes} minutes while you work.";
    }

    /// <summary>Clears every recovery point; a second click confirms, so one tap cannot wipe them.</summary>
    void DeleteAutosaves_Click(object sender, RoutedEventArgs e)
    {
        var remaining = AutosaveStore.List().Count;
        if (remaining == 0)
        {
            AutosaveStatus.Text = "There are no autosaves to delete.";
            DeleteAutosavesButton.Content = "Delete all autosaves";
            _deleteArmed = false;
            return;
        }
        if (!_deleteArmed)
        {
            _deleteArmed = true;
            DeleteAutosavesButton.Content = $"Delete {remaining} autosave{(remaining == 1 ? "" : "s")}?";
            AutosaveStatus.Text = "Click again to remove every autosave on this computer.";
            return;
        }
        var deleted = AutosaveStore.DeleteAll();
        _deleteArmed = false;
        DeleteAutosavesButton.Content = "Delete all autosaves";
        AutosaveStatus.Text = deleted == 0
            ? "No autosaves could be deleted."
            : $"Deleted {deleted} autosave{(deleted == 1 ? "" : "s")}. Saved .spg games are untouched.";
    }

    /// <summary>
    /// Enter while typing in a preference field saves what was typed and keeps the page open.
    /// Save is the window's default button, so without this a stray Enter mid-edit saved and
    /// dismissed the whole page. Enter anywhere else still reaches its own control.
    /// </summary>
    void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || e.OriginalSource is not (TextBox or PasswordBox)) return;
        e.Handled = true;
        if (!Commit()) return;
        _committed = true;
        // Show the value that was actually stored, so "0.50" or "7" read back as what stuck.
        if (DelayInput is not null)
            DelayInput.Text = _draft.ValueSwitchDelay.ToString("0.##", CultureInfo.CurrentCulture);
        if (AutosaveMinutesInput is not null)
            AutosaveMinutesInput.Text = _draft.AutosaveMinutes.ToString(CultureInfo.InvariantCulture);
        // The page was painted with the theme it opened with; follow a theme picked this way.
        ThemeManager.Apply(this, _draft.Theme);
    }

    /// <summary>Writes the draft over the live preferences and stores them.</summary>
    bool Commit()
    {
        if (!ApplyLockPassword()) return false;
        _original.Theme = _draft.Theme;
        _original.ShowHeadingHints = _draft.ShowHeadingHints;
        _original.QuickLayoutsPublish = _draft.QuickLayoutsPublish;
        _original.NextDownPreview = _draft.NextDownPreview;
        _original.UppercaseDownWords = _draft.UppercaseDownWords;
        _original.NewSeriesLive = _draft.NewSeriesLive;
        _original.HornOnGameClockZero = _draft.HornOnGameClockZero;
        _original.HornOnPlayClockZero = _draft.HornOnPlayClockZero;
        _original.ValueSwitchDelay = Math.Clamp(_draft.ValueSwitchDelay, 0, MaxDelay);
        _original.AutosaveMinutes = Math.Max(0, _draft.AutosaveMinutes);
        _original.ClockFormat = _draft.ClockFormat;
        _original.AutoUpdateRecord = _draft.AutoUpdateRecord;
        _original.TieRule = _draft.TieRule;
        _original.TieAllowPeriod = Math.Clamp(_draft.TieAllowPeriod, 1, 99);
        _original.ShowOvertimeFinal = _draft.ShowOvertimeFinal;
        _original.AccentColor = _draft.AccentColor;
        _original.AccentColor2 = _draft.AccentColor2;
        _original.RemotePort = _draft.RemotePort;
        _original.RemotePassword = _draft.RemotePassword;
        _original.OperatorLockPasswordHash = _draft.OperatorLockPasswordHash;
        // A remote with no password is refused rather than served openly on the network.
        _original.RemoteEnabled = _draft.RemoteEnabled && !string.IsNullOrWhiteSpace(_draft.RemotePassword);
        _original.CheckForUpdates = _draft.CheckForUpdates;
        _original.UpdateFeedUrl = _draft.UpdateFeedUrl;
        PreferencesStore.Save(_original);
        GameEngine.DownPreviewEnabled = _original.NextDownPreview;
        GameEngine.NewSeriesLiveEnabled = _original.NewSeriesLive;
        DisplayTiming.Apply(_original);
        return true;
    }

    void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!Commit()) return;
        // DialogResult is only valid for a modal dialog; the window may also be shown modelessly.
        if (System.Windows.Interop.ComponentDispatcher.IsThreadModal) DialogResult = true;
        Close();
    }

    /// <summary>
    /// Without an Enter save, Cancel reports false and MainWindow leaves the live theme untouched.
    /// After one, the values are already stored, so closing reports true and the owner repaints.
    /// </summary>
    void Done_Click(object sender, RoutedEventArgs e)
    {
        if (_committed && System.Windows.Interop.ComponentDispatcher.IsThreadModal)
        {
            DialogResult = true; // this closes the dialog itself
            return;
        }
        Close();
    }
}
