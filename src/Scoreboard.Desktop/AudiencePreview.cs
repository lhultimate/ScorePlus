using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Scoreboard.Core;

namespace Scoreboard.Desktop;

/// <summary>
/// A second thumbnail under the program preview that mirrors what an audience window is showing
/// right now: the on-air layout of one display, painted from the published game values.
/// It is a monitor, not a control — it never stages or publishes anything. Only the display it
/// looks at can be changed, so an operator can check a second screen without touching it.
/// </summary>
public sealed class AudiencePreview : Grid
{
    static readonly Brush Accent = Freeze(Color.FromRgb(56, 215, 163));
    static readonly Brush Muted = Freeze(Color.FromRgb(147, 160, 178));
    static Brush Freeze(Color color) { var brush = new SolidColorBrush(color); brush.Freeze(); return brush; }

    readonly ComboBox _displayPicker = new() { FontSize = 11, MinHeight = 26, Margin = new Thickness(0, 0, 0, 4) };
    readonly ContentControl _host = new();
    readonly ScorebugCatalog _catalog;
    readonly GameEngine _publishedEngine;
    readonly MediaPlayback? _playback;
    ScorebugCanvasView? _view;
    string? _displayId;
    bool _sync;

    public AudiencePreview(ScorebugCatalog catalog, GameEngine publishedEngine, MediaPlayback? playback, string? displayId = null)
    {
        _catalog = catalog;
        _publishedEngine = publishedEngine;
        _playback = playback;
        // The backdrop belongs to the artwork; the operator theme must not repaint it.
        ThemeManager.SetSkipTheme(this, true);
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
        var live = new TextBlock { Text = "● LIVE", Foreground = Accent, FontSize = 9, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(live, Dock.Right);
        header.Children.Add(live);
        var title = new StackPanel { Orientation = Orientation.Horizontal };
        title.Children.Add(new Border { Width = 3, Height = 12, CornerRadius = new CornerRadius(2), Background = Accent, Margin = new Thickness(0, 0, 6, 0) });
        title.Children.Add(new TextBlock { Text = "AUDIENCE VIEW", FontWeight = FontWeights.Bold, FontSize = 12, Foreground = Muted, VerticalAlignment = VerticalAlignment.Center });
        header.Children.Add(title);
        Children.Add(header);

        _displayPicker.SelectionChanged += Display_Changed;
        _displayPicker.ToolTip = "Which display this monitor looks at. It never changes what is on air.";
        SetRow(_displayPicker, 1);
        Children.Add(_displayPicker);

        SetRow(_host, 2);
        Children.Add(_host);
        _catalog.Changed += CatalogChanged;
        SyncDisplays(displayId);
    }

    /// <summary>The display currently being monitored.</summary>
    public string? DisplayId => _displayId;

    /// <summary>The artwork on air for the monitored display.</summary>
    public ScorebugLayout? Layout => _view?.Layout;

    /// <summary>Points the monitor at a display without publishing anything.</summary>
    public void SelectDisplay(string? displayId)
    {
        if (string.Equals(_displayId, displayId, StringComparison.OrdinalIgnoreCase)) return;
        _displayId = displayId;
        SyncDisplays(displayId);
    }

    /// <summary>Re-creates the on-air visual, for a new game or a fresh layout.</summary>
    public void Refresh()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(Refresh); return; }
        _view?.Rebuild();
    }

    void CatalogChanged()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(CatalogChanged); return; }
        SyncDisplays(_displayId);
    }

    /// <summary>Fills the display list and builds the view for the chosen display.</summary>
    void SyncDisplays(string? wanted)
    {
        _sync = true;
        try
        {
            var names = _catalog.Displays.Select(display => display.Name).ToArray();
            if (_displayPicker.ItemsSource is not IEnumerable<string> existing || !existing.SequenceEqual(names))
                _displayPicker.ItemsSource = names;
            var resolved = _catalog.FindDisplay(wanted) ?? _catalog.Displays.FirstOrDefault();
            if (resolved is null) { _displayPicker.SelectedIndex = -1; return; }
            _displayId = resolved.Id;
            var index = _catalog.Displays.IndexOf(resolved);
            if (_displayPicker.SelectedIndex != index) _displayPicker.SelectedIndex = index;
            if (_view is null || !_host.Content.Equals(_view)) BuildView();
            else _view.Rebuild();
        }
        finally { _sync = false; }
    }

    /// <summary>
    /// The view reads the published engine, so it follows scores and clocks exactly like the
    /// audience window and never the staged game values.
    /// </summary>
    void BuildView()
    {
        // Muted so it never doubles the clip's sound, but marked as on-air so a replay that is only
        // staged in the preview stays invisible here until Start replay puts it on air.
        _view = new ScorebugCanvasView(_catalog, _publishedEngine, playback: _playback, muteVideo: true,
            layoutProvider: () => _catalog.PublishedCompositeOf(_displayId), onAirVideo: true, displayId: _displayId);
        _host.Content = _view;
    }

    void Display_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_sync || _displayPicker.SelectedIndex < 0 || _displayPicker.SelectedIndex >= _catalog.Displays.Count) return;
        _displayId = _catalog.Displays[_displayPicker.SelectedIndex].Id;
        _view?.Rebuild();
    }
}
