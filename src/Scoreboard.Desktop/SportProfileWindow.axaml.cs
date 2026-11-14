using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Scoreboard.Core;

namespace Scoreboard.Desktop;
public partial class SportProfileWindow : Window
{
    readonly SportCatalog _catalog;
    SportDefinition? _selected;
    bool _loading;
    // One editable row per sport value, so a profile decides what a sport's console starts with.
    readonly List<ControlRow> _rows = [];

    sealed record ControlRow(SportControl Control, TextBox ValueBox, CheckBox OnBox, FrameworkElement Element)
    {
        public int ReadDefault(int fallback)
        {
            if (Control.Kind == SportControlKind.Flag) return OnBox.IsChecked == true ? 1 : 0;
            return int.TryParse(ValueBox.Text.Trim(), out var value) ? Math.Clamp(value, Control.Min, Math.Max(Control.Min, Control.Max)) : fallback;
        }
    }

    /// <summary>True once this session saved, duplicated, reset or deleted a profile, so the app
    /// knows to refresh the sport picker and the console when the window closes.</summary>
    public bool Changed { get; private set; }

    public SportProfileWindow(SportCatalog catalog)
    {
        InitializeComponent();
        _catalog = catalog;
        RefreshList();
        Profiles.SelectedIndex = 0;
    }

    void RefreshList(string? selectId = null)
    {
        _loading = true;
        Profiles.ItemsSource = null;
        Profiles.ItemsSource = _catalog.All.Select(s => new ProfileItem(s, Label(s))).ToArray();
        _loading = false;
        if (selectId is not null)
            Profiles.SelectedItem = Profiles.Items.Cast<ProfileItem>().FirstOrDefault(i => i.Profile.Id == selectId);
        else if (Profiles.SelectedIndex < 0 && Profiles.Items.Count > 0)
            Profiles.SelectedIndex = 0;
    }

    string Label(SportDefinition sport)
    {
        var kind = _catalog.IsOverridden(sport.Id) ? "EDITED" : _catalog.IsBuiltIn(sport.Id) ? "PRESET" : "CUSTOM";
        return $"{kind}   ·   {sport.Name}";
    }

    void Profiles_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || Profiles.SelectedItem is not ProfileItem item) return;
        _selected = item.Profile;
        _loading = true;
        NameInput.Text = _selected.Name;
        UnitInput.Text = _selected.Unit;
        PeriodsInput.Text = _selected.Periods.ToString();
        MinutesInput.Text = (_selected.PeriodSeconds / 60).ToString();
        TimeoutsInput.Text = _selected.Timeouts.ToString();
        CountUpInput.IsChecked = _selected.CountUp;
        ShotClockInput.IsChecked = _selected.HasShotClock;
        ScoringInput.Text = string.Join(", ", _selected.ScoringOptions);
        BuildControlRows(_selected);
        _loading = false;
        ErrorText.Text = "";
        var builtIn = _catalog.IsBuiltIn(_selected.Id);
        BuiltInNote.Text = builtIn
            ? "Built-in preset — saving keeps your version as an override, so new games use these settings."
            : "Custom profile — saving updates it everywhere.";
        ResetButton.IsEnabled = _catalog.IsOverridden(_selected.Id);
        DeleteButton.IsEnabled = !builtIn;
    }

    void Save_Click(object sender, object e)
    {
        if (_selected is null) return;
        try
        {
            var profile = ReadInputs(_selected);
            var saved = _catalog.SaveProfile(profile);
            _selected = saved;
            Changed = true;
            RefreshList(saved.Id);
            ErrorText.Text = "Saved. New games use these settings.";
        }
        catch (Exception ex) when (ex is FormatException or OverflowException or ArgumentException or InvalidOperationException)
        {
            ErrorText.Text = ex.Message;
        }
    }

    SportDefinition ReadInputs(SportDefinition source)
    {
        var scores = ScoringInput.Text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToArray();
        if (scores.Length == 0) throw new FormatException("Enter at least one scoring value, for example 1, 2, 3.");
        return source with
        {
            Name = NameInput.Text.Trim(),
            Unit = UnitInput.Text.Trim(),
            Periods = int.Parse(PeriodsInput.Text),
            PeriodSeconds = checked(int.Parse(MinutesInput.Text) * 60),
            Timeouts = int.Parse(TimeoutsInput.Text),
            CountUp = CountUpInput.IsChecked == true,
            HasShotClock = ShotClockInput.IsChecked == true,
            ScoringOptions = scores,
            Controls = ReadControlRows()
        };
    }

    /// <summary>Fills the value list for one profile: label, starting number (or on/off), remove.</summary>
    void BuildControlRows(SportDefinition profile)
    {
        ControlsHost.Children.Clear();
        _rows.Clear();
        foreach (var control in profile.Controls)
        {
            var grid = new Grid { Margin = new Thickness(2, 0, 2, 3) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var label = new TextBlock
            {
                Text = control.Kind == SportControlKind.Flag ? $"{control.Label}  ·  on / off" : control.Label,
                FontSize = 12,
                VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(4, 0, 6, 0)
            };
            grid.Children.Add(label);

            var valueBox = new TextBox { Width = 68, Height = 30, MinHeight = 0, FontSize = 12, Margin = new Thickness(0, 1, 0, 1), TextAlignment = TextAlignment.Center };
            var onBox = new CheckBox { Content = "Starts on", FontSize = 11, VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0) };
            if (control.Kind == SportControlKind.Flag)
            {
                onBox.IsChecked = control.Default != 0;
                Grid.SetColumn(onBox, 1);
                grid.Children.Add(onBox);
            }
            else
            {
                valueBox.Text = control.Default.ToString();
                valueBox.ToolTip = $"Starting {control.Label.ToLowerInvariant()} for a new game (0–{control.Max})";
                Grid.SetColumn(valueBox, 1);
                grid.Children.Add(valueBox);
            }

            var remove = new Button
            {
                Content = "×",
                Width = 26,
                Height = 26,
                MinWidth = 0,
                MinHeight = 0,
                Padding = new Thickness(0),
                FontSize = 12,
                Margin = new Thickness(4, 0, 0, 0),
                ToolTip = $"Remove {control.Label} from this sport"
            };
            Grid.SetColumn(remove, 2);
            grid.Children.Add(remove);

            var row = new ControlRow(control, valueBox, onBox, grid);
            remove.Click += (_, _) =>
            {
                _rows.Remove(row);
                ControlsHost.Children.Remove(grid);
            };
            _rows.Add(row);
            ControlsHost.Children.Add(grid);
        }
        if (_rows.Count == 0)
            ControlsHost.Children.Add(new TextBlock
            {
                Text = "No sport values yet. Add one for anything this sport needs on the console.",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(0x5C, 0x6B, 0x7E)),
                Margin = new Thickness(4, 2, 4, 2),
                TextWrapping = TextWrapping.Wrap
            });
    }

    /// <summary>Reads every row back, keeping each control's id so existing bindings survive an edit.</summary>
    SportControl[] ReadControlRows()
    {
        var result = new List<SportControl>();
        foreach (var row in _rows)
        {
            var control = row.Control;
            var start = row.ReadDefault(control.Default);
            // Adding a starting value above the control's ceiling would clamp it away silently.
            var max = control.Kind == SportControlKind.Flag ? 1 : Math.Max(control.Max, start);
            result.Add(control with { Default = start, Max = max });
        }
        return result.ToArray();
    }

    /// <summary>Adds a sport value of the operator's own: a number to step, or an on/off switch.</summary>
    void AddValue_Click(object sender, object e)
    {
        if (_selected is null) return;
        var dialog = new Window
        {
            Title = "Add a sport value",
            Width = 430,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new SolidColorBrush(Color.FromRgb(0x17, 0x1C, 0x25)),
            Foreground = Brushes.White,
            FontFamily = new FontFamily("Segoe UI")
        };
        var panel = new StackPanel { Margin = new Thickness(18, 16, 18, 14) };
        panel.Children.Add(new TextBlock { Text = "WHAT SHOULD THE CONSOLE SHOW?", FontSize = 11, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0x93, 0xA0, 0xB2)), Margin = new Thickness(2, 0, 2, 8) });
        panel.Children.Add(new TextBlock { Text = "Label", FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(0x93, 0xA0, 0xB2)), Margin = new Thickness(2, 4, 2, 2) });
        var label = new TextBox { Text = "New value", Margin = new Thickness(2, 0, 2, 6) };
        panel.Children.Add(label);
        panel.Children.Add(new TextBlock { Text = "Type", FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(0x93, 0xA0, 0xB2)), Margin = new Thickness(2, 4, 2, 2) });
        var kind = new ComboBox { ItemsSource = new[] { "Number to step", "On / off switch" }, SelectedIndex = 0, Margin = new Thickness(2, 0, 2, 6) };
        panel.Children.Add(kind);
        panel.Children.Add(new TextBlock { Text = "Highest value (numbers only)", FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(0x93, 0xA0, 0xB2)), Margin = new Thickness(2, 4, 2, 2) });
        var max = new TextBox { Text = "99", Margin = new Thickness(2, 0, 2, 6) };
        panel.Children.Add(max);
        panel.Children.Add(new TextBlock { Text = "What a new game starts with", FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(0x93, 0xA0, 0xB2)), Margin = new Thickness(2, 4, 2, 2) });
        var start = new TextBox { Text = "0", Margin = new Thickness(2, 0, 2, 6) };
        panel.Children.Add(start);
        var hint = new TextBlock { Text = "It appears on the console under SPORT CONTROLS, can be given a keybind, and can be printed on the scorebug with Add sport data.", TextWrapping = TextWrapping.Wrap, FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(0x5C, 0x6B, 0x7E)), Margin = new Thickness(2, 6, 2, 10) };
        panel.Children.Add(hint);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Right };
        var cancel = new Button { Content = "Cancel", MinWidth = 84, Margin = new Thickness(4, 0, 0, 0) };
        cancel.Click += (_, _) => dialog.Close();
        var add = new Button { Content = "Add value", IsDefault = true, MinWidth = 110, Margin = new Thickness(6, 0, 0, 0), Background = new SolidColorBrush(Color.FromRgb(0x0B, 0x6B, 0x50)), BorderBrush = new SolidColorBrush(Color.FromRgb(0x14, 0xA6, 0x7C)), Foreground = Brushes.White };
        ThemeManager.SetSkipTheme(add, true);
        add.Click += (_, _) =>
        {
            var text = label.Text.Trim();
            if (text.Length == 0) { hint.Text = "Give the value a label so it can be read on the console."; return; }
            var flag = kind.SelectedIndex == 1;
            var highest = int.TryParse(max.Text.Trim(), out var parsedMax) ? Math.Clamp(parsedMax, 1, 999) : 99;
            var starts = int.TryParse(start.Text.Trim(), out var parsedStart) ? parsedStart : 0;
            var id = $"{_selected.Id}.{Guid.NewGuid().ToString("N")[..8]}";
            var control = new SportControl(id, text, flag ? SportControlKind.Flag : SportControlKind.Counter,
                Max: flag ? 1 : highest, Default: Math.Clamp(starts, 0, flag ? 1 : highest),
                OnText: flag ? text.ToUpperInvariant() : null);
            var next = new SportDefinition(_selected.Id, _selected.Name, _selected.Unit, _selected.Periods, _selected.PeriodSeconds,
                _selected.ScoringOptions, _selected.HasShotClock, _selected.CountUp, _selected.Timeouts)
            {
                Controls = [.. _selected.Controls, control]
            };
            _selected = next;
            BuildControlRows(next);
            dialog.DialogResult = true;
            dialog.Close();
        };
        buttons.Children.Add(cancel);
        buttons.Children.Add(add);
        panel.Children.Add(buttons);
        dialog.Content = panel;
        ThemeManager.Apply(dialog, PreferencesStore.Load().Theme);
        dialog.Loaded += (_, _) => { label.Focus(); label.SelectAll(); };
        dialog.ShowDialog();
    }

    /// <summary>
    /// Enter in a field settles that field instead of dismissing the whole profile page: Save is the
    /// window's default button, so without this a stray Enter threw away the edit being typed.
    /// </summary>
    void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || e.OriginalSource is not TextBox) return;
        e.Handled = true;
        Keyboard.ClearFocus();
        ErrorText.Text = "Value stored — press Save profile to keep it.";
    }

    /// <summary>
    /// Starts a custom sport with blank settings rather than a copy of an existing one, selects it,
    /// and leaves the cursor in the name so the operator can just type over it.
    /// </summary>
    void NewProfile_Click(object sender, object e)
    {
        try
        {
            var name = "New sport";
            for (var suffix = 2; _catalog.All.Any(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)); suffix++)
                name = $"New sport {suffix}";
            var profile = new SportDefinition(Guid.NewGuid().ToString("N"), name, "Period", 4, 12 * 60, [1, 2, 3], false, false, 3);
            var created = _catalog.AddOrUpdate(profile);
            Changed = true;
            RefreshList(created.Id);
            Profiles.SelectedItem = Profiles.Items.Cast<ProfileItem>().FirstOrDefault(item => item.Profile.Id == created.Id);
            ErrorText.Text = "New custom profile — set its name, period label, clock and scoring buttons, then Save profile.";
            NameInput.Focus();
            NameInput.SelectAll();
        }
        catch (Exception ex) { ErrorText.Text = ex.Message; }
    }

    void Duplicate_Click(object sender, object e)
    {
        if (_selected is null) return;
        try
        {
            var name = NameInput.Text.Trim();
            if (string.IsNullOrWhiteSpace(name) || _catalog.All.Any(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase) && s.Id != _selected.Id))
                name = "Copy of " + _selected.Name;
            _catalog.Duplicate(_selected.Id, name);
            Changed = true;
            var created = _catalog.Custom.Last();
            RefreshList(created.Id);
            Profiles.SelectedItem = Profiles.Items.Cast<ProfileItem>().FirstOrDefault(i => i.Profile.Id == created.Id);
            ErrorText.Text = "Created a custom copy.";
        }
        catch (Exception ex) { ErrorText.Text = ex.Message; }
    }

    void Reset_Click(object sender, object e)
    {
        if (_selected is null || !_catalog.IsOverridden(_selected.Id)) return;
        if (!ConfirmWindow.Ask(this, PreferencesStore.Load().Theme, "RESET TO PRESET",
                $"Discard your saved changes to ‘{_selected.Name}’ and restore the original preset?", "Reset to preset")) return;
        _catalog.ResetOverride(_selected.Id);
        Changed = true;
        RefreshList(_selected.Id);
        Profiles.SelectedItem = Profiles.Items.Cast<ProfileItem>().FirstOrDefault(i => i.Profile.Id == _selected?.Id);
        ErrorText.Text = "Restored the original preset.";
    }

    void Delete_Click(object sender, object e)
    {
        if (_selected is null || _catalog.IsBuiltIn(_selected.Id)) return;
        if (!ConfirmWindow.Ask(this, PreferencesStore.Load().Theme, "DELETE CUSTOM SPORT",
                $"Delete the custom sport ‘{_selected.Name}’? Existing games keep their saved settings.", "Delete sport", destructive: true)) return;
        _catalog.Delete(_selected.Id);
        Changed = true;
        _selected = null;
        RefreshList();
    }

    void Done_Click(object sender, object e) => Close();

    sealed record ProfileItem(SportDefinition Profile, string Text) { public override string ToString() => Text; }
}
