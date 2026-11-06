using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Scoreboard.Core;

namespace Scoreboard.Desktop;

/// <summary>
/// One side of the roster desk: the roster that team draws from, its players with a search box,
/// and the editor for the highlighted player — number, name, position and every stat column.
/// Selecting a player puts them on the graphic, which is what the studio's player bindings read.
/// </summary>
public partial class RosterPanel : UserControl
{
    GameEngine? _engine;
    bool _home;
    Roster? _roster;
    RosterPlayer? _player;
    bool _syncing;
    string _signature = "";
    string _choiceSignature = "";
    string _search = "";
    // How many player rows the list is showing, for the "x of y players" line.
    int _shownCount;
    // Sections the operator has folded shut this session, by group name.
    readonly HashSet<string> _collapsed = new(StringComparer.OrdinalIgnoreCase);
    // The theme this desk is painted with, so its own dialogs match it.
    string _theme = "Dark";

    sealed class PlayerRow(RosterPlayer player, bool onAir, Brush background, Brush foreground)
    {
        public RosterPlayer Player { get; } = player;
        public bool OnAir { get; } = onAir;
        public string NumberLabel => Player.Number > 0 ? Player.Number.ToString() : "";
        public string Title => string.IsNullOrWhiteSpace(Player.Name) ? "(no name)" : Player.Name;
        public string Position => Player.Position;
        public string Badge => OnAir ? "● ON AIR" : "";
        public Brush RowBackground { get; } = background;
        public Brush RowForeground { get; } = foreground;
    }

    public RosterPanel()
    {
        InitializeComponent();
        // A panel can be used on its own (an export box, the checks) before any window applies a
        // theme, so it paints itself with the saved theme's palette straight away.
        RosterPalette.Apply(PreferencesStore.Load().Theme, Resources);
        Loaded += (_, _) => Refresh(force: true);
    }

    /// <summary>True for the home side's desk, false for the visitors'.</summary>
    public bool IsHome => _home;

    /// <summary>Repaints the panel and its rows for the operator theme.</summary>
    public void ApplyTheme(string theme)
    {
        _theme = ThemeManager.Resolve(theme);
        RosterPalette.Apply(theme, Resources);
        _signature = "";
        Refresh(force: true);
    }

    /// <summary>Which half this panel edits. Called once by the dock when it builds the tab.</summary>
    public void Attach(GameEngine engine, bool home)
    {
        if (_engine is not null) _engine.Changed -= EngineChanged;
        _engine = engine;
        _home = home;
        _engine.Changed += EngineChanged;
        _signature = "";
        Refresh(force: true);
    }

    /// <summary>Rebuilds the panel from the library, for a window that just showed the tab.</summary>
    public void RefreshView()
    {
        _signature = "";
        Refresh(force: true);
    }

    public void SetHintVisibility(bool show) =>
        HintText.Visibility = show ? Visibility.Visible : Visibility.Collapsed;

    void EngineChanged() => Refresh();

    // ---------- refresh ----------

    void Refresh(bool force = false)
    {
        if (_engine is null) return;
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(() => Refresh(force)); return; }
        if (_syncing) return;
        // A hidden tab costs nothing on every clock tick; opening the tab forces a rebuild.
        if (!force && !IsVisible) return;

        var rosterId = _home ? _engine.State.HomeRosterId : _engine.State.AwayRosterId;
        _roster = RosterStore.Book.Find(rosterId);
        // Every player on the roster carries the team's stat columns, so nobody names a stat first.
        if (_roster is not null && _roster.EnsureColumns(PlayerStatPresets.For(_engine.State.SportId))) RosterStore.Save();
        UpdateRosterSummary();

        var selected = _home ? _engine.State.HomePlayer : _engine.State.AwayPlayer;
        if (_roster is not null && _player is not null && _roster.Find(_player.Id) is null) _player = null;
        if (_player is null) _player = _roster?.Find(selected?.PlayerId) ?? _roster?.Players.FirstOrDefault();

        var signature = string.Join('|',
            RosterStore.Version, rosterId, _roster?.Name, _player?.Id, _search,
            selected?.PlayerId, RosterStore.All().Count,
            string.Join(",", _roster?.Players.Select(p => $"{p.Id}:{p.Number}:{p.Name}:{p.Position}:{p.Group}:{p.Stats.Count}") ?? []));
        if (signature == _signature)
        {
            UpdateOnAir(selected);
            return;
        }
        _signature = signature;

        _syncing = true;
        try
        {
            BuildRosterChoices(rosterId);
            BuildPlayerList(selected);
            SyncEditor();
            BuildStats();
            BuildPresetChoices();
        }
        finally { _syncing = false; }

        UpdateOnAir(selected);
        UpdateButtons();
        UpdateCount(selected);
    }

    void UpdateOnAir(SelectedPlayer? selected)
    {
        var onAir = selected is not null && _roster is not null && _player is not null && selected.PlayerId == _player.Id;
        OnAirLabel.Text = selected is null
            ? "NO PLAYER ON THE GRAPHIC"
            : $"ON AIR · {selected.Number switch { > 0 => $"#{selected.Number} ", _ => "" }}{selected.Name}";
        OnAirLabel.SetResourceReference(ForegroundProperty, selected is null ? "RosterMutedBrush" : "RosterAccentBrush");
        SelectButton.IsEnabled = _player is not null && !onAir;
        SelectButton.Content = onAir ? "Selected" : "Select player";
        // Clear takes the current player off the graphic; with nobody there it has nothing to do.
        ClearPlayerButton.IsEnabled = selected is not null;
    }

    void UpdateButtons()
    {
        var hasRoster = _roster is not null;
        AddPlayerButton.IsEnabled = hasRoster;
        RenameRosterButton.IsEnabled = DeleteRosterButton.IsEnabled = hasRoster;
        RemovePlayerButton.IsEnabled = hasRoster && _player is not null;
        var hasPlayer = hasRoster && _player is not null;
        NumberInput.IsEnabled = NameInput.IsEnabled = PositionInput.IsEnabled = GroupInput.IsEnabled = hasPlayer;
        AddStatButton.IsEnabled = StatPresetBox.IsEnabled = hasPlayer;
    }

    /// <summary>Which roster is loaded, how big it is, and how to change it — at a glance.</summary>
    void UpdateRosterSummary()
    {
        if (_roster is null)
        {
            RosterSummary.Text = "no roster loaded";
            RosterSummary.ToolTip = "Press New roster… in the header to create one, or Import… to add a .spr file";
            return;
        }
        var players = _roster.Players.Count;
        var columns = _roster.StatNames().Count;
        RosterSummary.Text = $"{players} player{(players == 1 ? "" : "s")} · {columns} stat column{(columns == 1 ? "" : "s")}";
        RosterSummary.ToolTip = $"{_roster.Name} · {players} player{(players == 1 ? "" : "s")} · {columns} stat column{(columns == 1 ? "" : "s")} — pick another roster in the list above";
    }

    void UpdateCount(SelectedPlayer? selected)
    {
        var total = _roster?.Players.Count ?? 0;
        var shown = _shownCount;
        CountText.Text = shown == total
            ? $"{total} player{(total == 1 ? "" : "s")}"
            : $"{shown} of {total} players";
    }

    void BuildRosterChoices(string? rosterId)
    {
        var all = RosterStore.All();
        var choiceSignature = string.Join("|", all.Select(r => $"{r.Id}:{r.Name}"));
        if (choiceSignature != _choiceSignature)
        {
            _choiceSignature = choiceSignature;
            RosterBox.ItemsSource = all.ToList();
        }
        var index = _roster is null ? -1 : all.ToList().FindIndex(r => r.Id == _roster.Id);
        RosterBox.SelectedIndex = index;
        RosterBox.ToolTip = _roster is null
            ? "No roster yet — press New roster… in the header"
            : $"{_roster.Name} · {_roster.Players.Count} player{(_roster.Players.Count == 1 ? "" : "s")}";
    }

    void BuildPlayerList(SelectedPlayer? selected)
    {
        PlayerListHost.Children.Clear();
        _shownCount = 0;
        var (background, foreground) = RosterPalette.Highlight(
            _home ? _engine?.State.HomePrimaryColor : _engine?.State.AwayPrimaryColor, Resources);
        var rows = (_roster?.Players ?? []).Where(Matches).ToList();
        // Players read in sections: one foldable header per group, the ungrouped players last. A
        // roster nobody has grouped stays one plain list, exactly as it was before groups existed.
        var groups = rows
            .GroupBy(player => (player.Group ?? "").Trim(), StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key.Length == 0 ? 1 : 0)
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var anyGroup = groups.Any(group => group.Key.Length > 0);
        foreach (var group in groups)
        {
            if (anyGroup) PlayerListHost.Children.Add(GroupHeader(group.Key, group.Count()));
            if (anyGroup && group.Key.Length > 0 && _collapsed.Contains(group.Key)) continue;
            foreach (var player in group)
            {
                PlayerListHost.Children.Add(PlayerListRow(player, selected, background, foreground));
                _shownCount++;
            }
        }
        if (_player is null && rows.Count > 0) _player = rows[0];
        if (rows.Count == 0)
        {
            var empty = new TextBlock
            {
                Text = _roster is null
                    ? "Choose a roster above, or create one with New roster…"
                    : _roster.Players.Count == 0
                        ? "No players in this roster yet — press Add player"
                        : "No players match this search. Clear it to see the roster again.",
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(12, 14, 12, 0)
            };
            empty.SetResourceReference(ForegroundProperty, "RosterMutedBrush");
            PlayerListHost.Children.Add(empty);
        }
    }

    /// <summary>One foldable squad header: the group, how many players it holds, and its state.</summary>
    Border GroupHeader(string group, int count)
    {
        var open = group.Length == 0 || !_collapsed.Contains(group);
        var button = new Button
        {
            Content = $"{(open ? "▾" : "▸")}  {(group.Length == 0 ? "UNGROUPED" : group.ToUpperInvariant())}  ·  {count}",
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(12, 5, 12, 5),
            MinHeight = 26,
            Margin = new Thickness(0),
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Cursor = group.Length == 0 ? Cursors.Arrow : Cursors.Hand,
            IsEnabled = group.Length > 0,
            ToolTip = group.Length == 0
                ? "Players with no group set"
                : open ? $"Collapse {group}" : $"Open {group}"
        };
        button.SetResourceReference(ForegroundProperty, open ? "RosterTextBrush" : "RosterMutedBrush");
        button.Click += (_, _) =>
        {
            if (group.Length == 0) return;
            if (!_collapsed.Add(group)) _collapsed.Remove(group);
            _signature = "";
            Refresh(force: true);
        };
        var border = new Border { BorderThickness = new Thickness(0, 0, 0, 1), Child = button };
        border.SetResourceReference(Border.BackgroundProperty, "RosterSurfaceAltBrush");
        border.SetResourceReference(Border.BorderBrushProperty, "RosterBorderBrush");
        return border;
    }

    /// <summary>One player line: jersey, name, position and whether they are on the graphic.</summary>
    Border PlayerListRow(RosterPlayer player, SelectedPlayer? selected, Brush highlight, Brush highlightText)
    {
        var onAir = selected is not null && selected.PlayerId == player.Id;
        var text = onAir ? highlightText : (Brush)FindResource("RosterTextBrush");
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        grid.Children.Add(new TextBlock
        {
            Text = player.Number > 0 ? player.Number.ToString() : "",
            FontWeight = FontWeights.Bold,
            FontSize = 13,
            Foreground = text,
            TextAlignment = TextAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0)
        });
        var name = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(player.Name) ? "(no name)" : player.Name,
            Foreground = text,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(name, 1);
        grid.Children.Add(name);
        var position = new TextBlock
        {
            Text = player.Position,
            Foreground = text,
            Opacity = 0.75,
            FontSize = 11,
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(position, 2);
        grid.Children.Add(position);
        var badge = new TextBlock
        {
            Text = onAir ? "● ON AIR" : "",
            Foreground = text,
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(badge, 3);
        grid.Children.Add(badge);

        var row = new Border
        {
            Background = onAir ? highlight : Brushes.Transparent,
            Padding = new Thickness(10, 7, 10, 7),
            Cursor = Cursors.Hand,
            Child = grid,
            ToolTip = string.IsNullOrWhiteSpace(player.Group)
                ? "Click to edit this player"
                : $"{player.Group} · click to edit this player"
        };
        // Select on press: a row can be rebuilt while the button is down (the engine raises Changed
        // for the clock), and the matching up event then lands on a row that never saw the press.
        row.PreviewMouseLeftButtonDown += (_, e) => { SelectRow(player); e.Handled = true; };
        return row;
    }

    void SelectRow(RosterPlayer player)
    {
        // Even a click on the already-highlighted row refreshes the editor: imported rosters can
        // share an id, and an early return made every row look dead.
        _player = player;
        _signature = "";
        Refresh();
    }

    bool Matches(RosterPlayer player)
    {
        if (_search.Length == 0) return true;
        return player.Name.Contains(_search, StringComparison.OrdinalIgnoreCase)
            || player.Position.Contains(_search, StringComparison.OrdinalIgnoreCase)
            || (player.Group ?? "").Contains(_search, StringComparison.OrdinalIgnoreCase)
            || player.Number.ToString().Contains(_search, StringComparison.Ordinal);
    }

    void SyncEditor()
    {
        if (!NumberInput.IsKeyboardFocused) NumberInput.Text = _player?.Number.ToString() ?? "";
        if (!NameInput.IsKeyboardFocused) NameInput.Text = _player?.Name ?? "";
        if (!PositionInput.IsKeyboardFocused) PositionInput.Text = _player?.Position ?? "";
        if (!GroupInput.IsKeyboardFocused) GroupInput.Text = _player?.Group ?? "";
        PlayerInfoText.Text = _player is null
            ? "highlight a player on the left"
            : $"{_player.Record} · {(_player.Position.Length == 0 ? "no position" : _player.Position)}";
    }

    /// <summary>One row per stat column: right-aligned name, a dark value well, and a remove box.</summary>
    void BuildStats()
    {
        StatsHost.Children.Clear();
        StatsCountText.Text = "";
        StatsTitle.Text = _player is null ? "STATS" : $"STATS — {_player.Record.ToUpperInvariant()}";
        if (_player is null) return;
        StatsCountText.Text = _player.Stats.Count == 1 ? "1 stat" : $"{_player.Stats.Count} stats";
        foreach (var stat in _player.Stats.ToArray())
        {
            // One compact cell per column: name, value well and a remove box on one line, two
            // cells per row, so a full stat sheet fills the desk instead of scrolling.
            var cell = new Border
            {
                Margin = new Thickness(0, 0, 6, 3),
                Padding = new Thickness(6, 2, 3, 2),
                CornerRadius = new CornerRadius(5),
                BorderThickness = new Thickness(1)
            };
            cell.SetResourceReference(BackgroundProperty, "RosterSurfaceAltBrush");
            cell.SetResourceReference(BorderBrushProperty, "RosterBorderBrush");

            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(78) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(15) });

            var label = new TextBlock
            {
                Text = stat.Name,
                FontSize = 10,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 5, 0),
                ToolTip = stat.Name
            };
            label.SetResourceReference(ForegroundProperty, "RosterMutedBrush");
            row.Children.Add(label);

            var value = new TextBox
            {
                Text = stat.Display,
                FontSize = 12,
                MinHeight = 23,
                Padding = new Thickness(4, 0, 4, 0),
                TextAlignment = TextAlignment.Right,
                Tag = stat.Name,
                BorderThickness = new Thickness(1),
                ToolTip = $"{stat.Name} for {(_player.Number > 0 ? $"#{_player.Number} " : "")}{_player.Name}"
            };
            value.SetResourceReference(BackgroundProperty, "RosterInputBrush");
            value.SetResourceReference(ForegroundProperty, "RosterTextBrush");
            value.SetResourceReference(BorderBrushProperty, "RosterBorderBrush");
            value.SetResourceReference(TextBox.CaretBrushProperty, "RosterTextBrush");
            value.LostKeyboardFocus += StatValue_LostFocus;
            value.KeyDown += StatValue_KeyDown;
            Grid.SetColumn(value, 1);
            row.Children.Add(value);

            var remove = new Button
            {
                Content = "×",
                FontSize = 10,
                Width = 15,
                Height = 20,
                Padding = new Thickness(0),
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                Tag = stat.Name,
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = $"Remove {stat.Name} from every player on this roster"
            };
            remove.SetResourceReference(ForegroundProperty, "RosterDangerBrush");
            remove.Click += RemoveStat_Click;
            Grid.SetColumn(remove, 2);
            row.Children.Add(remove);

            cell.Child = row;
            StatsHost.Children.Add(cell);
        }
        if (_player.Stats.Count == 0)
        {
            var empty = new TextBlock
            {
                Text = "This player has no stats yet. Add one below.",
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap
            };
            empty.SetResourceReference(ForegroundProperty, "RosterMutedBrush");
            StatsHost.Children.Add(empty);
        }
    }

    /// <summary>The stat picker: the sport's columns first, then anything this roster already uses.</summary>
    void BuildPresetChoices()
    {
        if (_engine is null || _roster is null) { StatPresetBox.ItemsSource = null; return; }
        var names = PlayerStatPresets.For(_engine.State.SportId)
            .Concat(_roster.StatNames())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var previous = StatPresetBox.Text;
        StatPresetBox.ItemsSource = names;
        StatPresetBox.Text = names.Contains(previous, StringComparer.OrdinalIgnoreCase) ? previous : names.FirstOrDefault() ?? "";
    }

    // ---------- writing to the library ----------

    void SaveAndReload()
    {
        RosterStore.Save();
        _engine?.ReloadSelectedPlayer(_home);
        _signature = "";
        Refresh();
    }

    // ---------- roster library ----------

    void Roster_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || _engine is null) return;
        if (RosterBox.SelectedItem is not Roster roster) return;
        var current = _home ? _engine.State.HomeRosterId : _engine.State.AwayRosterId;
        if (roster.Id == current) return;
        _player = null;
        _engine.AssignRoster(_home, roster.Id);
        _signature = "";
        Refresh();
    }

    /// <summary>Imports a .spr roster into the library, or a .spp player into this roster.</summary>
    /// <summary>The tab header's Import… button, which belongs to whichever side is on the desk.</summary>
    public void ImportFromDialog() => Import_Click(this, new RoutedEventArgs());

    /// <summary>The tab header's Export… button, for this side's roster or highlighted player.</summary>
    public void ExportToDialog() => Export_Click(this, new RoutedEventArgs());

    void Import_Click(object sender, RoutedEventArgs e)
    {
        if (_engine is null) return;
        var dialog = new OpenFileDialog
        {
            Title = "Import roster or player",
            Filter = "ScorePlus files (*.spr;*.spp)|*.spr;*.spp|ScorePlus Roster (*.spr)|*.spr|ScorePlus Player (*.spp)|*.spp|All files (*.*)|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        var path = dialog.FileName;
        if (path.EndsWith(RosterFiles.PlayerExtension, StringComparison.OrdinalIgnoreCase)) ImportPlayer(path);
        else ImportRoster(path);
    }

    void ImportRoster(string path)
    {
        if (_engine is null) return;
        var imported = RosterFiles.LoadRoster(path);
        if (imported is null)
        {
            NoticeWindow.Tell(Window.GetWindow(this),
                "That file is not a roster",
                "The file you picked is not a ScorePlus Roster (.spr) file, or it is damaged.", warning: true);
            return;
        }
        imported.EnsureColumns(PlayerStatPresets.For(_engine.State.SportId));
        RosterStore.Book.Rosters.Add(imported);
        RosterStore.Save();
        _engine.AssignRoster(_home, imported.Id);
        _player = imported.Players.FirstOrDefault();
        _engine.ReloadSelectedPlayer(_home);
        _signature = ""; _choiceSignature = "";
        Refresh(force: true);
        RosterSummary.Text = $"imported {System.IO.Path.GetFileName(path)}";
    }

    void ImportPlayer(string path)
    {
        if (_engine is null) return;
        if (_roster is null)
        {
            NoticeWindow.Tell(Window.GetWindow(this),
                "No roster to import into",
                "Choose or create a roster first, then import the player into it.");
            return;
        }
        var imported = RosterFiles.LoadPlayer(path);
        if (imported is null)
        {
            NoticeWindow.Tell(Window.GetWindow(this),
                "That file is not a player",
                "The file you picked is not a ScorePlus Player (.spp) file, or it is damaged.", warning: true);
            return;
        }
        _roster.Players.Add(imported);
        _roster.EnsureColumns(PlayerStatPresets.For(_engine.State.SportId));
        _player = imported;
        RosterStore.Save();
        _engine.ReloadSelectedPlayer(_home);
        _signature = "";
        Refresh(force: true);
        RosterSummary.Text = $"imported {System.IO.Path.GetFileName(path)}";
    }

    /// <summary>What the Export… button writes: the whole roster, or just the highlighted player.</summary>
    public enum ExportTarget { Roster, Player }

    /// <summary>
    /// Writes this roster as a .spr file, or the highlighted player as a .spp file. The target is a
    /// question up front: the save dialog's "file type" list used to be the only way to reach a
    /// player, which read as though players could not be exported at all.
    /// </summary>
    void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_engine is null || _roster is null)
        {
            NoticeWindow.Tell(Window.GetWindow(this),
                "No roster to export",
                "Load or create a roster before exporting. New roster… in the header makes one.");
            return;
        }
        var choice = AskExportTarget(Window.GetWindow(this));
        if (choice is null) return;
        var asPlayer = choice == ExportTarget.Player;
        if (asPlayer && _player is null)
        {
            NoticeWindow.Tell(Window.GetWindow(this),
                "No player highlighted",
                "Click the player you want in the list first, then export again.");
            return;
        }
        var dialog = new SaveFileDialog
        {
            Title = asPlayer ? "Export player" : "Export roster",
            Filter = asPlayer ? "ScorePlus Player (*.spp)|*.spp" : "ScorePlus Roster (*.spr)|*.spr",
            DefaultExt = asPlayer ? "spp" : "spr",
            AddExtension = true,
            FileName = asPlayer && _player is not null ? SafeFileName(_player.Name) : SafeFileName(_roster.Name)
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        try
        {
            if (asPlayer) RosterFiles.SavePlayer(dialog.FileName, _player!, _roster.Name);
            else RosterFiles.SaveRoster(dialog.FileName, _roster);
        }
        catch (Exception error)
        {
            NoticeWindow.Tell(Window.GetWindow(this),
                "The file could not be written",
                error.Message, warning: true);
            return;
        }
        RosterSummary.Text = $"exported {(asPlayer ? "player" : "roster")} · {System.IO.Path.GetFileName(dialog.FileName)}";
        RosterSummary.ToolTip = dialog.FileName;
    }

    /// <summary>
    /// Asks whether to write the roster or the highlighted player, in the desk's own theme. The
    /// player button names exactly who would be written, so nobody has to guess what is highlighted.
    /// </summary>
    ExportTarget? AskExportTarget(Window? owner)
    {
        ExportTarget? chosen = null;
        var window = BuildExportWindow(owner, target => chosen = target);
        return window.ShowDialog() == true ? chosen : null;
    }

    /// <summary>
    /// The export box itself: one button per target, each naming what it writes. Built separately
    /// from the ask so the choices can be inspected without a modal dialog.
    /// </summary>
    public Window BuildExportWindow(Window? owner, Action<ExportTarget> choose)
    {
        var window = new Window
        {
            Title = "Export",
            Width = 620,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            FontFamily = new FontFamily("Segoe UI")
        };
        if (owner is not null) window.Owner = owner;
        var root = new StackPanel { Margin = new Thickness(26, 24, 26, 20) };
        root.Children.Add(new TextBlock
        {
            Text = $"Export the whole {(_roster!.Name.Length > 0 ? _roster.Name : "roster")} with every player, or just the player highlighted in the list.",
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var playerLabel = _player is null
            ? "Player (highlight one first)"
            : $"Player · {(_player.Number > 0 ? $"#{_player.Number} " : "")}{_player.Name} (.spp)";
        var rosterButton = new Button { Content = "Whole roster (.spr)", IsDefault = true, MinHeight = 34, Padding = new Thickness(13, 6, 13, 6), Margin = new Thickness(4, 0, 0, 0) };
        var playerButton = new Button
        {
            Content = playerLabel,
            IsEnabled = _player is not null,
            MinHeight = 34,
            Padding = new Thickness(13, 6, 13, 6),
            Margin = new Thickness(4, 0, 0, 0)
        };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinHeight = 34, Padding = new Thickness(13, 6, 13, 6), Margin = new Thickness(4, 0, 0, 0) };
        // A dialog closes by setting DialogResult; a modeless copy of the box has to be closed instead.
        void Finish(ExportTarget? target)
        {
            if (target is { } value) choose(value);
            if (System.Windows.Interop.ComponentDispatcher.IsThreadModal) window.DialogResult = true;
            else window.Close();
        }
        rosterButton.Click += (_, _) => Finish(ExportTarget.Roster);
        playerButton.Click += (_, _) => Finish(ExportTarget.Player);
        cancel.Click += (_, _) => Finish(null);
        buttons.Children.Add(rosterButton);
        buttons.Children.Add(playerButton);
        buttons.Children.Add(cancel);
        root.Children.Add(buttons);
        window.Content = root;
        ThemeManager.Apply(window, _theme);
        return window;
    }

    static string SafeFileName(string name)
    {
        var cleaned = new string(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '-' : c).ToArray()).Trim();
        return cleaned.Length == 0 ? "roster" : cleaned;
    }

    /// <summary>Creates a roster for this side. Also the green header's New roster button.</summary>
    public void CreateRoster()
    {
        if (_engine is null) return;
        var name = TextPromptWindow.Ask(Window.GetWindow(this), "New roster", "Roster name",
            _home ? "Home roster" : "Visitors roster");
        if (name is null) return;
        var roster = RosterStore.Book.Add(name);
        RosterStore.Save();
        _engine.AssignRoster(_home, roster.Id);
        _player = roster.Players.FirstOrDefault();
        _engine.ReloadSelectedPlayer(_home);
        _signature = ""; _choiceSignature = "";
        Refresh(force: true);
    }

    void RenameRoster_Click(object sender, RoutedEventArgs e)
    {
        if (_engine is null || _roster is null) return;
        var name = TextPromptWindow.Ask(Window.GetWindow(this), "Rename roster", "Roster name", _roster.Name);
        if (name is null || name == _roster.Name) return;
        _roster.Name = name;
        SaveAndReload();
    }

    void DeleteRoster_Click(object sender, RoutedEventArgs e)
    {
        if (_engine is null || _roster is null) return;
        var owner = Window.GetWindow(this);
        if (owner is null) return;
        // The app's own confirmation, not a system message box: it follows the operator theme.
        if (!ConfirmWindow.Ask(owner, _theme, "DELETE ROSTER",
                $"Delete the roster \"{_roster.Name}\" and its {_roster.Players.Count} player{(_roster.Players.Count == 1 ? "" : "s")}? Saved games keep their own copy, and the roster library is shared by every panel.",
                "Delete roster", destructive: true)) return;
        var id = _roster.Id;
        RosterStore.Book.Remove(id);
        RosterStore.Save();
        if (_engine.State.HomeRosterId == id) _engine.AssignRoster(true, null);
        if (_engine.State.AwayRosterId == id) _engine.AssignRoster(false, null);
        _player = null;
        _signature = "";
        Refresh();
    }

    // ---------- players ----------

    void Search_Changed(object sender, TextChangedEventArgs e)
    {
        if (_syncing) return;
        _search = SearchInput.Text.Trim();
        _signature = "";
        Refresh();
    }

    void AddPlayer_Click(object sender, RoutedEventArgs e)
    {
        if (_engine is null || _roster is null) return;
        _player = _roster.AddPlayer();
        _roster.EnsureColumns(PlayerStatPresets.For(_engine.State.SportId));
        SaveAndReload();
        NameInput.Focus();
        NameInput.SelectAll();
    }

    void RemovePlayer_Click(object sender, RoutedEventArgs e)
    {
        if (_engine is null || _roster is null || _player is null) return;
        var wasSelected = (_home ? _engine.State.HomePlayer : _engine.State.AwayPlayer)?.PlayerId == _player.Id;
        if (!_roster.RemovePlayer(_player.Id)) return;
        _player = _roster.Players.FirstOrDefault();
        if (wasSelected) _engine.SelectPlayer(_home, _player?.Id);
        SaveAndReload();
    }

    void Select_Click(object sender, RoutedEventArgs e)
    {
        if (_engine is null || _player is null) return;
        _engine.SelectPlayer(_home, _player.Id);
        Refresh();
    }

    /// <summary>Takes this side's player off the graphic, leaving the row highlighted for editing.</summary>
    void ClearPlayer_Click(object sender, RoutedEventArgs e)
    {
        if (_engine is null) return;
        _engine.ClearSelectedPlayer(_home);
        _signature = "";
        Refresh(force: true);
    }

    void Player_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        CommitPlayer();
        Keyboard.ClearFocus();
        e.Handled = true;
    }

    void Player_LostFocus(object sender, KeyboardFocusChangedEventArgs e) => CommitPlayer();

    void CommitPlayer()
    {
        if (_syncing || _engine is null || _roster is null || _player is null) return;
        var changed = false;
        if (int.TryParse(NumberInput.Text.Trim(), out var number))
        {
            number = Math.Clamp(number, 0, 999);
            if (_player.Number != number) { _player.Number = number; changed = true; }
        }
        var name = NameInput.Text.Trim();
        if (name != _player.Name) { _player.Name = name; changed = true; }
        var position = PositionInput.Text.Trim();
        if (position != _player.Position) { _player.Position = position; changed = true; }
        var group = GroupInput.Text.Trim();
        if (group != (_player.Group ?? "")) { _player.Group = group; changed = true; }
        if (!changed) return;
        RosterStore.Save();
        _engine.ReloadSelectedPlayer(_home);
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => { _signature = ""; Refresh(); }));
    }

    // ---------- stats ----------

    void AddStat_Click(object sender, RoutedEventArgs e)
    {
        if (_engine is null || _roster is null || _player is null) return;
        var name = (StatPresetBox.Text ?? "").Trim();
        if (name.Length == 0) { StatPresetBox.Focus(); return; }
        // A stat is a column: every player on this roster gets it, and the highlighted player keeps
        // whatever value is already in the box (a fresh column starts at zero).
        foreach (var player in _roster.Players)
            if (!player.Stats.Any(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)))
                player.Stats.Add(new GameStat(Guid.NewGuid().ToString("N"), name));
        SaveAndReload();
        StatPresetBox.Text = "";
    }

    void RemoveStat_Click(object sender, RoutedEventArgs e)
    {
        if (_roster is null || sender is not Button { Tag: string name }) return;
        foreach (var player in _roster.Players)
            player.Stats.RemoveAll(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
        SaveAndReload();
    }

    void StatValue_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        CommitStat(sender);
        Keyboard.ClearFocus();
        e.Handled = true;
    }

    void StatValue_LostFocus(object sender, KeyboardFocusChangedEventArgs e) => CommitStat(sender);

    void CommitStat(object sender)
    {
        if (_syncing || _engine is null || _player is null) return;
        if (sender is not TextBox { Tag: string name } box) return;
        var text = box.Text.Trim();
        var stat = _player.Stats.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
        if (stat is null) return;
        if (stat.Display == text) return;
        // Player stats are counts; a non-number is rejected and the stored value comes back.
        if (!int.TryParse(text, out var value)) { box.Text = stat.Display; return; }
        _player.SetStat(name, Math.Max(0, value));
        RosterStore.Save();
        _engine.ReloadSelectedPlayer(_home);
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => { _signature = ""; Refresh(); }));
    }
}
