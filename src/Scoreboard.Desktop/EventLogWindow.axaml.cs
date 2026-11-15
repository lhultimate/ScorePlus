using Avalonia.Interactivity;
using Avalonia;
using Scoreboard.Core;

namespace Scoreboard.Desktop;

/// <summary>
/// The event log used to sit under the preview. It now lives in its own window so the dock can be
/// all media, and it stays live against the same engine.
/// </summary>
public partial class EventLogWindow : Window
{
    readonly GameEngine _engine;
    int _count = -1;
    bool _frozen;

    public EventLogWindow(GameEngine engine, string theme, bool showHints)
    {
        InitializeComponent();
        _engine = engine;
        LogHint.Visibility = showHints ? Visibility.Visible : Visibility.Collapsed;
        ThemeManager.Apply(this, theme);
        _engine.Changed += Refresh;
        Refresh();
    }

    public void SetHintVisibility(bool show) => LogHint.Visibility = show ? Visibility.Visible : Visibility.Collapsed;

    void Refresh()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(Refresh); return; }
        if (_frozen || _engine.State.Events.Count == _count) return;
        _count = _engine.State.Events.Count;
        LogList.ItemsSource = _engine.State.Events.AsEnumerable().Reverse()
            .Select(x => $"{x.At:HH:mm:ss}  {x.Type,-12} {x.Description}").ToArray();
    }

    void Clear_Click(object sender, RoutedEventArgs e)
    {
        _frozen = true;
        LogList.ItemsSource = null;
        _count = _engine.State.Events.Count;
    }

    protected override void OnClosed(EventArgs e)
    {
        _engine.Changed -= Refresh;
        base.OnClosed(e);
    }
}
