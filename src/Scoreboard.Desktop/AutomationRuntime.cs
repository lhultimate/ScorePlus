using System.Text;
using System.Text.Json;
using System.Windows.Media;
using Scoreboard.Core;

namespace Scoreboard.Desktop;

/// <summary>
/// Evaluates a display's node rules against the published game and returns a temporary layout.
/// A rule marked BypassOutputUpdate reads the live game instead, so it can react immediately
/// without the operator pressing Update Output. Nothing in the saved design is changed: the
/// runtime layers the matched actions over a copy, and the studio still shows the designed colour.
/// </summary>
public sealed class AutomationRuntime : IDisposable
{
    readonly ScorebugCatalog _catalog;
    readonly GameEngine _engine;
    readonly GameEngine? _liveEngine;
    readonly Dictionary<string, Dictionary<string, VisualPatch>> _overrides = new(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<string> _matched = new(StringComparer.OrdinalIgnoreCase);
    // Where each rule is in its trigger → wait → after-wait flow. A rule without wait-until
    // conditions is only ever Idle or AfterWait, which keeps the old flat behaviour intact.
    readonly Dictionary<string, RuleFlow> _flows = new(StringComparer.OrdinalIgnoreCase);
    // Which scheduled rules have already run, and in which minute, so an at-time rule fires once.
    readonly Dictionary<string, DateTime> _fired = new(StringComparer.OrdinalIgnoreCase);
    readonly System.Windows.Threading.DispatcherTimer _schedule = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly Dictionary<ScorebugLayout, (long Version, ScorebugLayout Effective)> _cache = new(ReferenceEqualityComparer.Instance);
    long _version;
    long _catalogVersion;
    bool _evaluating;
    bool _disposed;

    public AutomationRuntime(ScorebugCatalog catalog, GameEngine engine, GameEngine? liveEngine = null)
    {
        _catalog = catalog;
        _engine = engine;
        _liveEngine = liveEngine;
        _catalog.Changed += CatalogChanged;
        _engine.Changed += Evaluate;
        // Bypass rules follow the working game, not just the copy currently on air.
        if (liveEngine is not null && !ReferenceEquals(liveEngine, engine)) liveEngine.Changed += Evaluate;
        _schedule.Tick += Schedule_Tick;
        _schedule.Start();
        Evaluate();
    }

    /// <summary>Raised when actions begin or stop applying, or when a layout switch changed.</summary>
    public event Action? Changed;

    /// <summary>
    /// Runs the actions that change the show rather than one element's artwork: a macro, a media
    /// item on or off air. The show wires this to its macro runner and media session; without it
    /// those actions still match and report, they just have nothing to run them.
    /// </summary>
    public Action<AutomationAction>? RunCommand { get; set; }

    /// <summary>Re-evaluates once a second, so an at-time rule fires even while the game is still.</summary>
    void Schedule_Tick(object? sender, EventArgs e)
    {
        if (_disposed) { _schedule.Stop(); return; }
        if (!_catalog.Displays.Any(display => (display.Automations ?? []).Any(rule =>
                rule.Enabled && AutomationRules.NormalizeTime(rule.AtTime).Length > 0)))
            return;
        Evaluate();
    }

    /// <summary>True when a scheduled rule's time has arrived and it has not run for it yet.</summary>
    bool Scheduled(string key, AutomationRule rule)
    {
        var at = AutomationRules.NormalizeTime(rule.AtTime);
        if (at.Length == 0) return false;
        var now = DateTime.Now;
        if (!AutomationRules.TimeMatches(at, now)) return false;
        // The schedule is part of the identity: changing 19:30 to 20:00 must be able to fire again
        // in the same session, and a daily rule still remembers the last day it ran.
        var fireKey = $"{key}|{at}";
        if (!_fired.TryGetValue(fireKey, out var last)) return true;
        if (!rule.AtTimeDaily) return false;
        return last.Date != now.Date;
    }

    public int ActiveRuleCount => _matched.Count;

    /// <summary>True when this exact rule is matching on the current evaluation.</summary>
    public bool IsRuleActive(string displayId, string ruleId) =>
        _matched.Contains(Key(displayId, ruleId));

    /// <summary>A test button in the editor can preview a draft rule without saving it.</summary>
    public bool Matches(AutomationRule rule) =>
        rule.Enabled && rule.Matches(rule.BypassOutputUpdate ? _liveEngine?.State ?? _engine.State : _engine.State);

    /// <summary>
    /// The layout a view should draw. When no rule is touching the display this is the exact
    /// layout it was given; otherwise a copy carries the transient colour, position and visibility.
    /// </summary>
    public ScorebugLayout Effective(string? displayId, ScorebugLayout layout)
    {
        var resolved = _catalog.FindDisplay(displayId)?.Id ?? _catalog.ActiveDisplay.Id;
        if (!_overrides.TryGetValue(resolved, out var patches) || patches.Count == 0) return layout;
        if (_cache.TryGetValue(layout, out var cached) && cached.Version == _version + _catalogVersion)
            return cached.Effective;
        var copy = JsonSerializer.Deserialize<ScorebugLayout>(JsonSerializer.Serialize(layout));
        if (copy is null) return layout;
        var byId = copy.Elements.ToDictionary(element => element.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var (id, patch) in patches)
        {
            if (!byId.TryGetValue(id, out var element)) continue;
            patch.Apply(element);
        }
        _cache[layout] = (_version + _catalogVersion, copy);
        return copy;
    }

    /// <summary>Forces an evaluation after the operator edits a graph.</summary>
    public void Refresh() => Evaluate();

    void CatalogChanged()
    {
        _catalogVersion++;
        _cache.Clear();
        Evaluate();
    }

    void Evaluate()
    {
        if (_disposed || _evaluating) return;
        _evaluating = true;
        try
        {
            var next = new Dictionary<string, Dictionary<string, VisualPatch>>(StringComparer.OrdinalIgnoreCase);
            var flows = new Dictionary<string, RuleFlow>(StringComparer.OrdinalIgnoreCase);
            var matched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var switchTargets = new List<(string DisplayId, string LayoutId)>();
            var publishedState = _engine.State;
            var liveState = _liveEngine?.State ?? publishedState;
            foreach (var display in _catalog.Displays)
            {
                var patches = new Dictionary<string, VisualPatch>(StringComparer.OrdinalIgnoreCase);
                foreach (var rule in display.Automations ?? [])
                {
                    if (!rule.Enabled) continue;
                    var key = Key(display.Id, rule.Id);
                    var state = rule.BypassOutputUpdate ? liveState : publishedState;
                    // A scheduled rule triggers on its clock as well as on its conditions, so
                    // "at 19:30 switch to the opening layout" needs no game change to fire.
                    var scheduled = Scheduled(key, rule);
                    if (scheduled) _fired[$"{key}|{AutomationRules.NormalizeTime(rule.AtTime)}"] = DateTime.Now;
                    var trigger = scheduled || rule.Matches(state);
                    // A rule with no wait-until conditions never enters a wait step, so it keeps the
                    // flat trigger/action behaviour it had before wait steps existed.
                    var waits = rule.Conditions?.Any(condition => condition.WaitUntil) == true;
                    var waitMet = waits && rule.MatchesWait(state);
                    var previous = _flows.TryGetValue(key, out var flow) ? flow : RuleFlow.Idle;
                    var nextFlow = previous;
                    var onMatch = false;
                    var afterWait = false;
                    var elseBranch = false;
                    var enterOnMatch = false;
                    var enterAfterWait = false;
                    var enterElse = false;

                    switch (previous)
                    {
                        case RuleFlow.Waiting when waitMet:
                            // The wait can be met by the same edit that ends the trigger, as in
                            // "show the flag layout, wait until the flag is gone, show the program".
                            onMatch = true;
                            afterWait = true;
                            elseBranch = !trigger;
                            enterAfterWait = true;
                            nextFlow = trigger ? RuleFlow.AfterWait : RuleFlow.Idle;
                            break;
                        case RuleFlow.Waiting when trigger:
                            onMatch = true;
                            nextFlow = RuleFlow.Waiting;
                            break;
                        case RuleFlow.Waiting:
                            elseBranch = true;
                            enterElse = true;
                            nextFlow = RuleFlow.Idle;
                            break;
                        case RuleFlow.AfterWait when trigger:
                            onMatch = true;
                            afterWait = true;
                            nextFlow = RuleFlow.AfterWait;
                            break;
                        case RuleFlow.AfterWait:
                            elseBranch = true;
                            enterElse = true;
                            nextFlow = RuleFlow.Idle;
                            break;
                        default:
                            if (trigger)
                            {
                                onMatch = true;
                                enterOnMatch = true;
                                // With no wait step, "after wait" is immediate and is never stranded.
                                afterWait = !waits;
                                enterAfterWait = !waits;
                                nextFlow = waits && !waitMet ? RuleFlow.Waiting : RuleFlow.AfterWait;
                            }
                            else
                            {
                                elseBranch = true;
                                nextFlow = RuleFlow.Idle;
                            }
                            break;
                    }

                    if (trigger) matched.Add(key);
                    flows[key] = nextFlow;
                    ApplyPatches(rule, patches, onMatch, afterWait, elseBranch, state);
                    if (enterOnMatch || enterAfterWait || enterElse)
                    {
                        CollectSwitches(rule, display.Id, switchTargets, enterOnMatch, enterAfterWait, enterElse);
                        CollectCommands(rule, enterOnMatch, enterAfterWait, enterElse);
                    }
                }
                if (patches.Count > 0) next[display.Id] = patches;
            }

            var visualChange = !string.Equals(Signature(_overrides), Signature(next), StringComparison.Ordinal);
            var ruleChange = !_matched.SetEquals(matched);
            _matched.Clear();
            foreach (var key in matched) _matched.Add(key);
            _flows.Clear();
            foreach (var (key, flow) in flows) _flows[key] = flow;
            _overrides.Clear();
            foreach (var (displayId, patches) in next) _overrides[displayId] = patches;
            if (visualChange) _version++;
            if (visualChange) _cache.Clear();
            // A decimal column on any element (in the design or in a rule's live patch) is the only
            // reason the engine has to wake the show ten times a second.
            // Only ever turned on: a staged layout can use decimals before it is saved, and the
            // studio canvas latches the same flag, so clearing it here would make the column stutter.
            _engine.ClockDecimalsInUse |= ClockDecimalsUsed();

            // A switch is edge-triggered per phase. A rule that stays matched must not fight the
            // operator every tick; it fires when its phase becomes active, and can fire again after
            // the trigger drops and the rule starts over.
            foreach (var (displayId, layoutId) in switchTargets)
            {
                var display = _catalog.FindDisplay(displayId);
                if (display is null) continue;
                var index = display.Layouts.FindIndex(layout =>
                    string.Equals(layout.Id, layoutId, StringComparison.OrdinalIgnoreCase));
                if (index < 0) continue;
                var unchanged = display.PublishedIndex == index;
                _catalog.PublishLayout(display.Id, index);
                if (!unchanged) visualChange = true;
            }

            if (visualChange || ruleChange) Changed?.Invoke();
        }
        finally
        {
            _evaluating = false;
        }
    }

    /// <summary>
    /// Layers the actions for the phases that are live this tick. Else is applied first so a tick
    /// that satisfies the wait while ending the trigger still leaves the after-wait result on top.
    /// </summary>
    static void ApplyPatches(AutomationRule rule, Dictionary<string, VisualPatch> patches,
        bool onMatch, bool afterWait, bool elseBranch, GameState state)
    {
        foreach (var phase in new[]
                 {
                     AutomationActionPhase.Else, AutomationActionPhase.OnMatch, AutomationActionPhase.AfterWait
                 })
        {
            var applies = phase switch
            {
                AutomationActionPhase.OnMatch => onMatch,
                AutomationActionPhase.AfterWait => afterWait,
                AutomationActionPhase.Else => elseBranch,
                _ => false
            };
            if (!applies) continue;
            foreach (var action in rule.Actions ?? [])
            {
                // Layout, macro, media and clock actions belong to the show, not to an element.
                if (action.Phase != phase || AutomationRules.IsCommandAction(action.Kind)) continue;
                if (string.IsNullOrWhiteSpace(action.ElementId)) continue;
                if (!patches.TryGetValue(action.ElementId, out var patch))
                    patches[action.ElementId] = patch = new VisualPatch();
                patch.Apply(action, state);
            }
        }
    }

    static void CollectSwitches(AutomationRule rule, string displayId,
        List<(string DisplayId, string LayoutId)> targets, bool onMatch, bool afterWait, bool elseBranch)
    {
        foreach (var action in rule.Actions ?? [])
        {
            if (action.Kind != AutomationActionKind.SwitchLayout) continue;
            if (string.IsNullOrWhiteSpace(action.LayoutId)) continue;
            var fires = action.Phase switch
            {
                AutomationActionPhase.OnMatch => onMatch,
                AutomationActionPhase.AfterWait => afterWait,
                AutomationActionPhase.Else => elseBranch,
                _ => false
            };
            if (fires) targets.Add((displayId, action.LayoutId));
        }
    }

    /// <summary>Runs the phase-appropriate command actions exactly when their phase becomes live.</summary>
    void CollectCommands(AutomationRule rule, bool onMatch, bool afterWait, bool elseBranch)
    {
        foreach (var action in rule.Actions ?? [])
        {
            if (!AutomationRules.IsCommandAction(action.Kind)) continue;
            if (action.Kind == AutomationActionKind.SwitchLayout) continue;
            var fires = action.Phase switch
            {
                AutomationActionPhase.OnMatch => onMatch,
                AutomationActionPhase.AfterWait => afterWait,
                AutomationActionPhase.Else => elseBranch,
                _ => false
            };
            if (!fires) continue;
            RunCommand?.Invoke(action);
        }
    }

    /// <summary>True when any element prints a decimal column, designed or patched by a rule.</summary>
    bool ClockDecimalsUsed() =>
        _catalog.Displays.Any(display => display.Layouts.Any(layout =>
            layout.Elements.Any(element => element.ClockDecimals > 0)))
        || _overrides.Values.Any(patches => patches.Values.Any(patch => patch.ClockDecimals > 0));

    static string Key(string displayId, string ruleId) => $"{displayId}|{ruleId}";

    /// <summary>A compact, stable signature of the active patches, without serializing whole layouts.</summary>
    static string Signature(Dictionary<string, Dictionary<string, VisualPatch>> patches)
    {
        var text = new StringBuilder();
        foreach (var display in patches.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            text.Append(display.Key).Append('{');
            foreach (var patch in display.Value.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
            {
                text.Append(patch.Key).Append(':');
                patch.Value.AppendSignature(text);
                text.Append(';');
            }
            text.Append('}');
        }
        return text.ToString();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _schedule.Stop();
        _catalog.Changed -= CatalogChanged;
        _engine.Changed -= Evaluate;
        if (_liveEngine is not null && !ReferenceEquals(_liveEngine, _engine)) _liveEngine.Changed -= Evaluate;
        _cache.Clear();
        _overrides.Clear();
        _matched.Clear();
    }

    enum RuleFlow
    {
        Idle,
        Waiting,
        AfterWait
    }

    sealed class VisualPatch
    {
        public string? TextColor { get; set; }
        public string? BackgroundColor { get; set; }
        public string? BorderColor { get; set; }
        public string? Content { get; set; }
        public double? Opacity { get; set; }
        public double? X { get; set; }
        public double? Y { get; set; }
        public double? DeltaX { get; set; }
        public double? DeltaY { get; set; }
        public double? Width { get; set; }
        public double? Height { get; set; }
        public bool? Visible { get; set; }
        public int? ClockDecimals { get; set; }

        public void AppendSignature(StringBuilder text)
        {
            text.Append(TextColor).Append('|').Append(BackgroundColor).Append('|').Append(BorderColor)
                .Append('|').Append(Content).Append('|').Append(Opacity).Append('|').Append(X)
                .Append('|').Append(Y).Append('|').Append(DeltaX).Append('|').Append(DeltaY)
                .Append('|').Append(Width).Append('|').Append(Height).Append('|').Append(Visible)
                .Append('|').Append(ClockDecimals);
        }

        public void Apply(AutomationAction action, GameState state)
        {
            var number = double.IsFinite(action.Number) ? action.Number : 0;
            var number2 = double.IsFinite(action.Number2) ? action.Number2 : 0;
            var colour = AutomationRules.ResolveColor(action, state);
            switch (action.Kind)
            {
                case AutomationActionKind.SetTextColor when IsColor(colour):
                    TextColor = colour;
                    break;
                case AutomationActionKind.SetBackgroundColor when IsColor(colour):
                    BackgroundColor = colour;
                    break;
                case AutomationActionKind.SetBorderColor when IsColor(colour):
                    BorderColor = colour;
                    break;
                case AutomationActionKind.SetOpacity:
                    Opacity = Math.Clamp(number, 0, 1);
                    break;
                case AutomationActionKind.MoveBy:
                    DeltaX = (DeltaX ?? 0) + number;
                    DeltaY = (DeltaY ?? 0) + number2;
                    break;
                case AutomationActionKind.MoveTo:
                    X = number;
                    Y = number2;
                    break;
                case AutomationActionKind.Resize:
                    Width = Math.Max(1, number);
                    Height = Math.Max(1, number2);
                    break;
                case AutomationActionKind.Show:
                    Visible = true;
                    break;
                case AutomationActionKind.Hide:
                    Visible = false;
                    break;
                case AutomationActionKind.SetText:
                    Content = action.Value;
                    break;
                case AutomationActionKind.SetClockDecimals:
                    ClockDecimals = GameEngine.ClampDecimals((int)Math.Round(number));
                    break;
            }
        }

        public void Apply(ScorebugElement element)
        {
            if (TextColor is { } foreground)
            {
                element.Foreground = foreground;
                element.ForegroundSource = ScorebugColorSource.Custom;
                element.TextGradientPaint = null;
                element.TextTexture = null;
            }
            if (BackgroundColor is { } background)
            {
                element.Background = background;
                element.BackgroundSource = ScorebugColorSource.Custom;
                element.BackgroundGradient = false;
                element.BackgroundGradientPaint = null;
                element.BackgroundTexture = null;
                element.TransparentBackground = false;
            }
            if (BorderColor is { } border)
            {
                element.BorderColor = border;
                element.BorderSource = ScorebugColorSource.Custom;
            }
            if (Content is { } content) element.Content = content;
            if (Opacity is { } opacity) element.Opacity = opacity;
            if (X is { } x) element.X = x;
            if (Y is { } y) element.Y = y;
            if (DeltaX is { } deltaX) element.X += deltaX;
            if (DeltaY is { } deltaY) element.Y += deltaY;
            if (Width is { } width) element.Width = width;
            if (Height is { } height) element.Height = height;
            if (Visible is { } visible) element.Visible = visible;
            // A stale rule may still point at a score or a down. A decimal column belongs only to a
            // timing clock, so never let a whole-number element pretend to carry one.
            if (ClockDecimals is { } decimals && AutomationRules.HasClockToken(element.Content)) element.ClockDecimals = decimals;
        }

        static bool IsColor(string? value)
        {
            var text = (value ?? "").Trim();
            if (text.Length < 4 || text[0] != '#') return false;
            try
            {
                _ = (Color)ColorConverter.ConvertFromString(text);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
