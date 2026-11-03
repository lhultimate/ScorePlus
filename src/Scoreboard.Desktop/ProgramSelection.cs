using Scoreboard.Core;

namespace Scoreboard.Desktop;

/// <summary>
/// One window's view of the program: which display its preview shows and which layout it has
/// staged for every display. Selection is deliberately per window, so a second control panel can
/// line up a lower third while the first one works on the main scorebug; only Update Output sends
/// anything to an audience.
/// </summary>
public sealed class ProgramSelection
{
    // Which layout id this window has chosen, per display. Stored by id so inserting or deleting a
    // layout elsewhere never silently changes what is staged here.
    readonly Dictionary<string, string> _staged = new(StringComparer.OrdinalIgnoreCase);
    readonly ScorebugCatalog _catalog;
    string _displayId;
    // The catalog's active layout as this window last saw it. Quick-layout activation changes it;
    // ordinary studio edits raise Changed too, but must not rewrite another panel's staged choice.
    (string DisplayId, int Index)? _seenActive;

    public ProgramSelection(ScorebugCatalog catalog, string? displayId = null)
    {
        _catalog = catalog;
        _displayId = (catalog.FindDisplay(displayId) ?? catalog.Displays[0]).Id;
        _catalog.Changed += Sync;
        Sync();
    }

    /// <summary>Raised when the preview's display or staged layout changes in this window.</summary>
    public event Action? Changed;

    public ScorebugCatalog Catalog => _catalog;

    public IReadOnlyList<ScorebugDisplay> Displays => _catalog.Displays;

    public ScorebugDisplay Display => _catalog.FindDisplay(_displayId) ?? _catalog.Displays[0];

    public string DisplayId => Display.Id;

    public int LayoutIndex => IndexFor(Display.Id);

    public ScorebugLayout Layout => Display.Layouts[LayoutIndex];

    /// <summary>"2 · Lower third" — the layout this window would publish.</summary>
    public string Label => $"{LayoutIndex + 1} · {Layout.Name}";

    /// <summary>The display's staged layout index in this window.</summary>
    public int IndexFor(string displayId)
    {
        var display = _catalog.FindDisplay(displayId);
        if (display is null) return 0;
        if (_staged.TryGetValue(display.Id, out var layoutId))
        {
            var index = display.Layouts.FindIndex(layout => string.Equals(layout.Id, layoutId, StringComparison.OrdinalIgnoreCase));
            if (index >= 0) return index;
        }
        return Math.Clamp(display.PublishedIndex, 0, Math.Max(0, display.Layouts.Count - 1));
    }

    /// <summary>Stages one display's layout without following links, used while syncing.</summary>
    void Stage(string displayId, int index)
    {
        var display = _catalog.FindDisplay(displayId);
        if (display is null || display.Layouts.Count == 0) return;
        _staged[display.Id] = display.Layouts[Math.Clamp(index, 0, display.Layouts.Count - 1)].Id;
    }

    public void SelectDisplay(string? displayId)
    {
        var display = _catalog.FindDisplay(displayId);
        if (display is null || string.Equals(display.Id, _displayId, StringComparison.OrdinalIgnoreCase)) return;
        _displayId = display.Id;
        Changed?.Invoke();
    }

    /// <summary>
    /// Stages a layout for the previewed display. A layout that is linked to another display takes
    /// that display's linked layout with it, so one Update Output changes both screens.
    /// </summary>
    public void SelectLayout(int index)
    {
        var display = Display;
        if (display.Layouts.Count == 0) return;
        var next = Math.Clamp(index, 0, display.Layouts.Count - 1);
        Stage(display.Id, next);
        FollowLinks(display.Id, next);
        Changed?.Invoke();
    }

    /// <summary>Follows this display's link chain, once per display, so a link loop cannot spin.</summary>
    void FollowLinks(string displayId, int layoutIndex)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { displayId };
        var current = (DisplayId: displayId, LayoutIndex: layoutIndex);
        // A chain of links is legitimate (main → interview → name strap); a loop is not.
        for (var hop = 0; hop < 8; hop++)
        {
            var link = _catalog.LinkTarget(current.DisplayId, current.LayoutIndex);
            if (link is null || !visited.Add(link.DisplayId)) return;
            Stage(link.DisplayId, link.LayoutIndex);
            current = (link.DisplayId, link.LayoutIndex);
        }
    }

    /// <summary>Moves the previewed display's layout forward or back, wrapping around.</summary>
    public void Shift(int delta)
    {
        var display = Display;
        if (display.Layouts.Count == 0) return;
        var next = (IndexFor(display.Id) + delta + display.Layouts.Count) % display.Layouts.Count;
        SelectLayout(next);
    }

    /// <summary>True when this window holds a layout that is not the one on air for that display.</summary>
    public bool HasStagedChanges => _catalog.Displays.Any(display =>
        display.Layouts.Count > 0 && IndexFor(display.Id) != Math.Clamp(display.PublishedIndex, 0, display.Layouts.Count - 1));

    /// <summary>Puts every staged layout on air, one display at a time.</summary>
    public void PublishStaged()
    {
        foreach (var display in _catalog.Displays)
        {
            if (display.Layouts.Count == 0) continue;
            var index = IndexFor(display.Id);
            if (index != Math.Clamp(display.PublishedIndex, 0, display.Layouts.Count - 1))
                _catalog.PublishLayout(display.Id, index);
        }
    }

    /// <summary>Drops choices for displays or layouts that no longer exist.</summary>
    public void Sync()
    {
        foreach (var stale in _staged.Keys.Where(id => _catalog.FindDisplay(id) is null).ToArray()) _staged.Remove(stale);
        if (_catalog.FindDisplay(_displayId) is null) _displayId = _catalog.Displays[0].Id;
        // A quick-layout press changes the catalog's active layout without going through this
        // window's SelectLayout, so follow that one event. Ordinary studio edits also raise
        // Changed, and must not overwrite a panel that deliberately staged a different layout.
        var active = _catalog.ActiveDisplay;
        var activeIndex = Math.Clamp(active.ActiveIndex, 0, Math.Max(0, active.Layouts.Count - 1));
        var activeChanged = _seenActive is { } seen
            && (!string.Equals(seen.DisplayId, active.Id, StringComparison.OrdinalIgnoreCase) || seen.Index != activeIndex);
        if (activeChanged && active.Layouts.Count > 0)
            _staged[active.Id] = active.Layouts[activeIndex].Id;
        _seenActive = (active.Id, activeIndex);
        // A layout deleted in the studio must not leave this window pointing at nothing.
        foreach (var display in _catalog.Displays)
            if (_staged.TryGetValue(display.Id, out var layoutId) &&
                !display.Layouts.Any(layout => string.Equals(layout.Id, layoutId, StringComparison.OrdinalIgnoreCase)))
                _staged.Remove(display.Id);
        Changed?.Invoke();
    }

    public void Dispose() => _catalog.Changed -= Sync;
}
