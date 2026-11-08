using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.Win32;
using Scoreboard.Core;

namespace Scoreboard.Desktop;

/// <summary>
/// The node editor for a display's automation rules. The graph is deliberately three columns:
/// a trigger on the left, conditions in the middle, and the actions they fire on the right.
/// Every edit is saved immediately and evaluated against the published state, so the audience
/// window is a live test bed while the operator works.
/// </summary>
public sealed class AutomationWindow : Window
{
    static readonly Brush PanelBrush = Frozen(Color.FromRgb(21, 29, 39));
    static readonly Brush PanelBrushSoft = Frozen(Color.FromRgb(17, 24, 33));
    static readonly Brush EdgeBrush = Frozen(Color.FromRgb(45, 57, 72));
    static readonly Brush TextBrush = Frozen(Color.FromRgb(223, 231, 240));
    static readonly Brush MutedBrush = Frozen(Color.FromRgb(147, 160, 178));
    static readonly Brush AccentBrush = Frozen(Color.FromRgb(56, 215, 163));
    static readonly Brush ConditionBrush = Frozen(Color.FromRgb(98, 168, 255));
    static readonly Brush ActionBrush = Frozen(Color.FromRgb(255, 184, 107));
    static readonly Brush DangerBrush = Frozen(Color.FromRgb(255, 91, 91));

    readonly ScorebugCatalog _catalog;
    readonly GameEngine _engine;
    readonly GameEngine? _liveEngine;
    readonly string _theme;
    readonly string _displayId;
    readonly AutomationRuntime _runtime;
    readonly List<AutomationRule> _rules = [];
    readonly ListBox _ruleList = new();
    readonly Canvas _graph = new();
    readonly TextBlock _graphHint = new();
    readonly TextBlock _status = new();
    readonly TextBlock _displayLabel = new();
    AutomationRule? _selected;
    bool _building;
    bool _refreshingList;

    public AutomationWindow(ScorebugCatalog catalog, GameEngine engine, string theme, string? displayId, AutomationRuntime runtime, GameEngine? liveEngine = null)
    {
        _catalog = catalog;
        _engine = engine;
        _liveEngine = liveEngine;
        _theme = theme;
        _runtime = runtime;
        _displayId = (catalog.FindDisplay(displayId) ?? catalog.ActiveDisplay).Id;
        _rules.AddRange((catalog.FindDisplay(_displayId)?.Automations ?? []).Select(rule => rule.Copy()));
        _selected = _rules.FirstOrDefault();

        Title = "Node rules";
        Width = 1280;
        Height = 780;
        MinWidth = 900;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(12, 17, 23));
        FontFamily = new FontFamily("Segoe UI");
        Content = BuildRoot();
        ThemeManager.Apply(this, theme);
        Loaded += (_, _) =>
        {
            RefreshRuleList();
            RefreshGraph();
        };
    }

    /// <summary>The local draft graph, exposed so tests can add a rule without driving the list box.</summary>
    public IReadOnlyList<AutomationRule> Rules => _rules;

    public AutomationRule? SelectedRule => _selected;

    public string DisplayId => _displayId;

    Grid BuildRoot()
    {
        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(252) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var rail = new Border
        {
            Background = PanelBrushSoft,
            BorderBrush = EdgeBrush,
            BorderThickness = new Thickness(0, 0, 1, 0),
            Padding = new Thickness(14, 16, 14, 12)
        };
        var railStack = new DockPanel();
        var header = new StackPanel();
        header.Children.Add(new TextBlock { Text = "NODE RULES", FontWeight = FontWeights.Bold, FontSize = 12, Foreground = AccentBrush });
        _displayLabel.Text = "Program";
        _displayLabel.FontSize = 18;
        _displayLabel.FontWeight = FontWeights.Bold;
        _displayLabel.Foreground = TextBrush;
        _displayLabel.Margin = new Thickness(0, 3, 0, 3);
        header.Children.Add(_displayLabel);
        header.Children.Add(new TextBlock
        {
            Text = "Conditions decide when actions fire. A condition marked “Wait until” holds the rule after it matches, then its “After wait” actions run.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 10,
            Foreground = MutedBrush,
            Margin = new Thickness(0, 0, 0, 12)
        });
        DockPanel.SetDock(header, Dock.Top);
        railStack.Children.Add(header);

        var io = new UniformGrid { Columns = 2, Margin = new Thickness(0, 0, 0, 4) };
        io.Children.Add(SmallButton("Export…", (_, _) => ExportRules()));
        io.Children.Add(SmallButton("Import…", (_, _) => ImportRules()));
        DockPanel.SetDock(io, Dock.Bottom);
        railStack.Children.Add(io);

        var buttons = new UniformGrid { Columns = 3, Margin = new Thickness(0, 0, 0, 8) };
        buttons.Children.Add(SmallButton("New", (_, _) => AddRule()));
        buttons.Children.Add(SmallButton("Copy", (_, _) => DuplicateRule()));
        buttons.Children.Add(SmallButton("Delete", (_, _) => DeleteRule()));
        DockPanel.SetDock(buttons, Dock.Bottom);
        railStack.Children.Add(buttons);

        var test = SmallButton("Test against current game", (_, _) => TestRules());
        test.Margin = new Thickness(0, 0, 0, 8);
        DockPanel.SetDock(test, Dock.Bottom);
        railStack.Children.Add(test);

        _ruleList.DisplayMemberPath = "Name";
        _ruleList.SelectionChanged += RuleList_SelectionChanged;
        _ruleList.Margin = new Thickness(0, 0, 0, 8);
        _ruleList.FontSize = 12;
        _ruleList.BorderBrush = EdgeBrush;
        _ruleList.Background = PanelBrush;
        railStack.Children.Add(_ruleList);
        rail.Child = railStack;
        root.Children.Add(rail);

        var workspace = new Grid();
        workspace.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        workspace.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        workspace.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var toolbar = new DockPanel { Margin = new Thickness(16, 12, 16, 8) };
        var addButtons = new StackPanel { Orientation = Orientation.Horizontal };
        addButtons.Children.Add(ToolbarButton("+ Condition", (_, _) => AddCondition()));
        addButtons.Children.Add(ToolbarButton("+ Action", (_, _) => AddAction()));
        addButtons.Children.Add(ToolbarButton("+ Rule", (_, _) => AddRule()));
        toolbar.Children.Add(addButtons);
        var close = new Button
        {
            Content = "Done",
            MinHeight = 32,
            Padding = new Thickness(14, 4, 14, 4),
            HorizontalAlignment = HorizontalAlignment.Right
        };
        close.Click += (_, _) => Close();
        DockPanel.SetDock(close, Dock.Right);
        toolbar.Children.Add(close);
        workspace.Children.Add(toolbar);

        _graph.Width = 1040;
        _graph.Height = 560;
        _graph.Background = new SolidColorBrush(Color.FromRgb(10, 15, 21));
        _graph.ClipToBounds = false;
        var scroll = new ScrollViewer
        {
            Content = _graph,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(16, 0, 16, 0),
            Background = _graph.Background
        };
        Grid.SetRow(scroll, 1);
        workspace.Children.Add(scroll);

        var footer = new Border
        {
            Background = PanelBrushSoft,
            BorderBrush = EdgeBrush,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(16, 7, 16, 7)
        };
        _status.Text = "Every edit saves immediately. Rules run against the published values, not the staged preview.";
        _status.Foreground = MutedBrush;
        _status.FontSize = 10;
        footer.Child = _status;
        Grid.SetRow(footer, 2);
        workspace.Children.Add(footer);

        _graphHint.Text = "No rules yet. Select “+ Rule” to create one.";
        _graphHint.Foreground = MutedBrush;
        _graphHint.FontSize = 15;
        _graphHint.HorizontalAlignment = HorizontalAlignment.Center;
        _graphHint.VerticalAlignment = VerticalAlignment.Center;
        workspace.Children.Add(_graphHint);
        Grid.SetRow(_graphHint, 1);

        Grid.SetColumn(workspace, 1);
        root.Children.Add(workspace);
        return root;
    }

    Button SmallButton(string text, RoutedEventHandler click)
    {
        var button = new Button { Content = text, FontSize = 10, Padding = new Thickness(5, 4, 5, 4), Margin = new Thickness(2) };
        button.Click += click;
        return button;
    }

    Button ToolbarButton(string text, RoutedEventHandler click)
    {
        var button = new Button { Content = text, FontSize = 11, MinHeight = 30, Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 0, 6, 0) };
        button.Click += click;
        return button;
    }

    public AutomationRule AddRule() => CreateRule(withExample: true);

    AutomationRule CreateRule(bool withExample)
    {
        var rule = new AutomationRule { Name = $"Rule {_rules.Count + 1}" };
        if (withExample)
        {
            var layout = _catalog.FindDisplay(_displayId)?.Layouts.FirstOrDefault();
            var target = layout?.Elements.FirstOrDefault(element => element.Kind == ScorebugElementKind.Text) ?? layout?.Elements.FirstOrDefault();
            rule.Conditions.Add(new AutomationCondition
            {
                Field = AutomationField.PlayClock,
                Operator = AutomationOperator.Less,
                Value = 5
            });
            rule.Actions.Add(new AutomationAction
            {
                Kind = AutomationActionKind.SetTextColor,
                LayoutId = layout?.Id ?? "",
                ElementId = target?.Id ?? "",
                Value = "#FFFF3B30"
            });
        }
        _rules.Add(rule);
        _selected = rule;
        Persist();
        RefreshRuleList();
        RefreshGraph();
        return rule;
    }

    public void DuplicateRule()
    {
        if (_selected is null) return;
        var copy = _selected.Copy();
        copy.Id = Guid.NewGuid().ToString("N");
        copy.Name = $"{_selected.Name} copy";
        _rules.Insert(_rules.IndexOf(_selected) + 1, copy);
        _selected = copy;
        Persist();
        RefreshRuleList();
        RefreshGraph();
    }

    public void DeleteRule()
    {
        if (_selected is null) return;
        var index = _rules.IndexOf(_selected);
        _rules.Remove(_selected);
        _selected = _rules.Count == 0 ? null : _rules[Math.Clamp(index, 0, _rules.Count - 1)];
        Persist();
        RefreshRuleList();
        RefreshGraph();
    }

    public void AddCondition()
    {
        if (_selected is null) CreateRule(withExample: false);
        if (_selected is null) return;
        _selected.Conditions.Add(new AutomationCondition { Field = AutomationField.PlayClock, Operator = AutomationOperator.Less, Value = 5 });
        Persist();
        RefreshGraph();
    }

    public void AddAction()
    {
        if (_selected is null) CreateRule(withExample: false);
        if (_selected is null) return;
        var layout = _catalog.FindDisplay(_displayId)?.Layouts.FirstOrDefault();
        var target = layout?.Elements.FirstOrDefault();
        _selected.Actions.Add(new AutomationAction
        {
            Kind = AutomationActionKind.SetTextColor,
            LayoutId = layout?.Id ?? "",
            ElementId = target?.Id ?? "",
            Value = "#FFFF3B30"
        });
        Persist();
        RefreshGraph();
    }

    void SaveSelected() => Persist();

    void ExportRules()
    {
        var display = _catalog.FindDisplay(_displayId);
        var dialog = new SaveFileDialog
        {
            Title = "Export node rules",
            Filter = "ScorePlus Rule (*.spr)|*.spr",
            DefaultExt = RuleFiles.Extension,
            AddExtension = true,
            FileName = $"{SafeName(display?.Name ?? "Program")} rules"
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            RuleFiles.Save(dialog.FileName, _rules);
            _status.Text = $"Exported {_rules.Count} rule{(_rules.Count == 1 ? "" : "s")} to {System.IO.Path.GetFileName(dialog.FileName)}.";
        }
        catch (Exception ex)
        {
            NoticeWindow.Tell(this, "Rules not exported", ex.Message, warning: true);
        }
    }

    void ImportRules()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Import node rules",
            Filter = "ScorePlus Rule (*.spr)|*.spr|All files (*.*)|*.*",
            DefaultExt = RuleFiles.Extension,
            CheckFileExists = true,
            Multiselect = true
        };
        if (dialog.ShowDialog(this) != true) return;

        var imported = new List<AutomationRule>();
        foreach (var path in dialog.FileNames)
        {
            var batch = RuleFiles.Load(path);
            if (batch is null) continue;
            foreach (var rule in batch)
            {
                // A second import must create independent rules, never overwrite an existing one.
                rule.Id = Guid.NewGuid().ToString("N");
                _rules.Add(rule);
                imported.Add(rule);
            }
        }
        if (imported.Count == 0)
        {
            NoticeWindow.Tell(this, "No rules found", "Those files are not ScorePlus Rule (.spr) bundles, or they are damaged.", warning: true);
            return;
        }
        _selected = imported[0];
        Persist();
        RefreshRuleList();
        RefreshGraph();
        _status.Text = $"Imported {imported.Count} rule{(imported.Count == 1 ? "" : "s")}; saved with “{_catalog.FindDisplay(_displayId)?.Name}”.";
    }

    static string SafeName(string name)
    {
        foreach (var invalid in System.IO.Path.GetInvalidFileNameChars()) name = name.Replace(invalid, '-');
        return string.IsNullOrWhiteSpace(name) ? "Program" : name.Trim();
    }

    void Persist()
    {
        if (_building) return;
        _catalog.UpdateAutomations(_displayId, _rules);
        _status.Text = $"{_rules.Count} rule{(_rules.Count == 1 ? "" : "s")} · saved with “{_catalog.FindDisplay(_displayId)?.Name}” · {_runtime.ActiveRuleCount} active right now";
    }

    void RefreshRuleList()
    {
        _refreshingList = true;
        try
        {
            var selected = _selected;
            _ruleList.ItemsSource = null;
            _ruleList.ItemsSource = _rules.ToArray();
            _ruleList.SelectedItem = selected;
        }
        finally { _refreshingList = false; }
        _displayLabel.Text = _catalog.FindDisplay(_displayId)?.Name ?? "Display";
    }

    void RuleList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshingList) return;
        if (_ruleList.SelectedItem is not AutomationRule rule) return;
        if (ReferenceEquals(rule, _selected)) return;
        _selected = rule;
        RefreshGraph();
    }

    void RefreshGraph()
    {
        if (_building) return;
        _building = true;
        try
        {
            _graph.Children.Clear();
            _graphHint.Visibility = _selected is null ? Visibility.Visible : Visibility.Collapsed;
            if (_selected is null) return;
            var layout = _catalog.FindDisplay(_displayId)?.Layouts.FirstOrDefault();
            var conditionHeight = 286;
            var actionHeight = 312;
            var top = 28;
            var triggerX = 26;
            var conditionX = 356;
            var actionX = 730;
            var triggerWidth = 270;
            var conditionWidth = 294;
            var actionWidth = 300;
            var conditionCenters = new List<double>();
            var actionCenters = new List<double>();
            var triggerCenter = top + 94;

            if (_selected.Conditions.Count == 0)
            {
                AddGraphLabel(triggerX + 14, top + 144, "No conditions: this rule always matches.", MutedBrush, 10);
            }

            for (var index = 0; index < _selected.Conditions.Count; index++)
            {
                var y = top + index * conditionHeight;
                var card = BuildConditionCard(_selected.Conditions[index], index);
                AddCard(card, conditionX, y, conditionWidth);
                conditionCenters.Add(y + 128);
            }
            for (var index = 0; index < _selected.Actions.Count; index++)
            {
                var y = top + index * actionHeight;
                var card = BuildActionCard(_selected.Actions[index], index);
                AddCard(card, actionX, y, actionWidth);
                actionCenters.Add(y + 138);
            }

            var trigger = BuildTriggerCard(_selected);
            AddCard(trigger, triggerX, top, triggerWidth);

            // Trigger to the condition bus. With no conditions the same bus feeds the actions.
            var conditionBus = conditionX - 28;
            if (conditionCenters.Count > 0)
            {
                DrawFan(triggerX + triggerWidth, triggerCenter, conditionBus, conditionX, conditionCenters, EdgeBrush);
                var actionBus = actionX - 26;
                DrawBus(conditionX + conditionWidth, conditionCenters, actionBus, actionX, actionCenters, ActionBrush);
            }
            else
            {
                DrawFan(triggerX + triggerWidth, triggerCenter, actionX - 30, actionX, actionCenters, ActionBrush);
            }

            if (_selected.Actions.Count == 0)
                AddGraphLabel(actionX + 30, top + 40, "No actions yet. Add one from the toolbar.", MutedBrush, 10);
            if (layout is not null && _selected.Actions.All(action => action.Kind != AutomationActionKind.SwitchLayout)
                && _selected.Actions.Count > 0)
                AddGraphLabel(actionX + 10, top + Math.Max(1, _selected.Actions.Count) * actionHeight + 12,
                    $"Display: {_catalog.FindDisplay(_displayId)?.Name} · current layout: {layout.Name}", MutedBrush, 10);

            _graph.Height = Math.Max(560, top + Math.Max(
                Math.Max(1, _selected.Conditions.Count) * conditionHeight,
                Math.Max(1, _selected.Actions.Count) * actionHeight) + 40);
        }
        finally { _building = false; }
    }

    Border BuildTriggerCard(AutomationRule rule)
    {
        var card = Card("TRIGGER", AccentBrush);
        var panel = (StackPanel)card.Child;
        var name = Box(rule.Name, 14, bold: true);
        name.TextChanged += (_, _) =>
        {
            if (_building) return;
            rule.Name = string.IsNullOrWhiteSpace(name.Text) ? "Untitled rule" : name.Text.Trim();
            SaveSelected();
            RefreshRuleList();
        };
        panel.Children.Add(name);
        var enabled = new CheckBox { Content = "Enabled", IsChecked = rule.Enabled, Foreground = TextBrush, FontSize = 11, Margin = new Thickness(0, 9, 0, 2) };
        enabled.Checked += (_, _) => { rule.Enabled = true; Persist(); };
        enabled.Unchecked += (_, _) => { rule.Enabled = false; Persist(); };
        panel.Children.Add(enabled);
        var bypass = new CheckBox
        {
            Content = "Bypass having to update output",
            IsChecked = rule.BypassOutputUpdate,
            Foreground = TextBrush,
            FontSize = 10,
            Margin = new Thickness(0, 1, 0, 2),
            ToolTip = "Use the live game state and apply this rule to the output immediately, without pressing Update Output."
        };
        bypass.Checked += (_, _) => { rule.BypassOutputUpdate = true; Persist(); RefreshGraph(); };
        bypass.Unchecked += (_, _) => { rule.BypassOutputUpdate = false; Persist(); RefreshGraph(); };
        panel.Children.Add(bypass);
        var any = new CheckBox
        {
            Content = rule.MatchAny ? "Match any condition" : "Match all conditions",
            IsChecked = rule.MatchAny,
            Foreground = MutedBrush,
            FontSize = 10,
            Margin = new Thickness(0, 3, 0, 6)
        };
        any.Checked += (_, _) => { rule.MatchAny = true; any.Content = "Match any condition"; Persist(); };
        any.Unchecked += (_, _) => { rule.MatchAny = false; any.Content = "Match all conditions"; Persist(); };
        panel.Children.Add(any);
        var active = MatchesNow(rule);
        panel.Children.Add(new TextBlock
        {
            Text = active ? "● active on the current game state" : "○ waiting for its conditions",
            Foreground = active ? AccentBrush : MutedBrush,
            FontSize = 10,
            Margin = new Thickness(0, 2, 0, 0)
        });
        var hasWait = rule.Conditions.Any(condition => condition.WaitUntil);
        panel.Children.Add(new TextBlock
        {
            Text = hasWait
                ? "When matched actions run on the match. The rule then waits for its wait-until condition and runs After wait actions; Else actions run while it is not matching."
                : rule.BypassOutputUpdate
                    ? "Reads the live game and applies immediately. It does not wait for Update Output."
                    : "Runs when the published game changes. Layout switches happen once per match, not every tick.",
            Foreground = MutedBrush,
            FontSize = 9,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0)
        });
        return card;
    }

    Border BuildConditionCard(AutomationCondition condition, int index)
    {
        var card = Card(condition.WaitUntil ? $"CONDITION {index + 1} · WAIT UNTIL" : $"CONDITION {index + 1}", ConditionBrush);
        var panel = (StackPanel)card.Child;
        var fieldChoices = AutomationRules.Fields.Select(option => new FieldChoice(option.Field, option.Label)).ToArray();
        var field = new ComboBox
        {
            ItemsSource = fieldChoices,
            DisplayMemberPath = "Label",
            SelectedItem = fieldChoices.FirstOrDefault(option => option.Field == condition.Field) ?? fieldChoices[0],
            FontSize = 11,
            MinHeight = 28,
            Margin = new Thickness(0, 0, 0, 5)
        };
        field.SelectionChanged += (_, _) =>
        {
            if (_building || field.SelectedItem is not FieldChoice choice) return;
            condition.Field = choice.Field;
            var allowed = AutomationRules.OperatorsFor(choice.Field);
            if (!allowed.Any(option => option.Operator == condition.Operator)) condition.Operator = allowed[0].Operator;
            Persist();
            RefreshGraph();
        };
        panel.Children.Add(field);

        var operatorChoices = AutomationRules.OperatorsFor(condition.Field)
            .Select(option => new OperatorChoice(option.Operator, option.Label)).ToArray();
        var op = new ComboBox
        {
            ItemsSource = operatorChoices,
            DisplayMemberPath = "Label",
            SelectedItem = operatorChoices.FirstOrDefault(option => option.Operator == condition.Operator) ?? operatorChoices[0],
            FontSize = 11,
            MinHeight = 28,
            Margin = new Thickness(0, 0, 0, 5)
        };
        op.SelectionChanged += (_, _) =>
        {
            if (_building || op.SelectedItem is not OperatorChoice choice) return;
            condition.Operator = choice.Operator;
            Persist();
        };
        panel.Children.Add(op);

        if (AutomationRules.IsTextField(condition.Field))
        {
            panel.Children.Add(FieldLabel("Value"));
            var text = Box(condition.Text, 11);
            text.TextChanged += (_, _) =>
            {
                if (_building) return;
                condition.Text = text.Text.Trim();
                Persist();
            };
            panel.Children.Add(text);
        }
        else if (AutomationRules.IsNumericField(condition.Field))
        {
            if (condition.Field == AutomationField.CustomStat)
            {
                panel.Children.Add(FieldLabel("Stat name"));
                var key = Box(condition.Key, 11);
                key.TextChanged += (_, _) =>
                {
                    if (_building) return;
                    condition.Key = key.Text.Trim();
                    Persist();
                };
                panel.Children.Add(key);
            }
            panel.Children.Add(FieldLabel("Value"));
            var value = Box(condition.Value.ToString("0.##"), 11);
            value.TextChanged += (_, _) =>
            {
                if (_building || !double.TryParse(value.Text, out var number)) return;
                condition.Value = number;
                Persist();
            };
            panel.Children.Add(value);
        }
        else
        {
            panel.Children.Add(new TextBlock
            {
                Text = AutomationRules.BooleanHint(condition.Field),
                Foreground = MutedBrush,
                FontSize = 10,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0)
            });
        }

        var waitUntil = new CheckBox
        {
            Content = "Wait until this is met",
            IsChecked = condition.WaitUntil,
            Foreground = TextBrush,
            FontSize = 10,
            Margin = new Thickness(0, 7, 0, 0),
            ToolTip = "Hold the rule after its trigger matches until this condition becomes true, then run the actions marked After wait."
        };
        waitUntil.Checked += (_, _) => { condition.WaitUntil = true; Persist(); RefreshGraph(); };
        waitUntil.Unchecked += (_, _) => { condition.WaitUntil = false; Persist(); RefreshGraph(); };
        panel.Children.Add(waitUntil);
        if (condition.WaitUntil)
            panel.Children.Add(new TextBlock
            {
                Text = "The rule holds after its trigger until this becomes true.",
                Foreground = MutedBrush,
                FontSize = 9,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 3, 0, 0)
            });

        var remove = new Button { Content = "Remove condition", FontSize = 9, Padding = new Thickness(7, 2, 7, 2), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 7, 0, 0) };
        remove.Click += (_, _) =>
        {
            _selected?.Conditions.Remove(condition);
            Persist();
            RefreshGraph();
        };
        panel.Children.Add(remove);
        return card;
    }

    Border BuildActionCard(AutomationAction action, int index)
    {
        var card = Card($"ACTION {index + 1}", ActionBrush);
        var panel = (StackPanel)card.Child;
        var actionChoices = AutomationRules.Actions.Select(option => new ActionChoice(option.Kind, option.Label)).ToArray();
        var kind = new ComboBox
        {
            ItemsSource = actionChoices,
            DisplayMemberPath = "Label",
            SelectedItem = actionChoices.FirstOrDefault(option => option.Kind == action.Kind) ?? actionChoices[0],
            FontSize = 11,
            MinHeight = 28,
            Margin = new Thickness(0, 0, 0, 5)
        };
        kind.SelectionChanged += (_, _) =>
        {
            if (_building || kind.SelectedItem is not ActionChoice choice) return;
            action.Kind = choice.Kind;
            if (choice.Kind == AutomationActionKind.SetText
                && (string.IsNullOrWhiteSpace(action.Value) || action.Value.StartsWith('#')))
                action.Value = "NEW TEXT";
            if (choice.Kind == AutomationActionKind.SwitchLayout
                && string.IsNullOrWhiteSpace(action.LayoutId))
                action.LayoutId = _catalog.FindDisplay(_displayId)?.Layouts.FirstOrDefault()?.Id ?? "";
            Persist();
            RefreshGraph();
        };
        panel.Children.Add(kind);

        var phaseChoices = AutomationRules.ActionPhases
            .Select(option => new PhaseChoice(option.Phase, option.Label)).ToArray();
        var phase = new ComboBox
        {
            ItemsSource = phaseChoices,
            DisplayMemberPath = "Label",
            SelectedItem = phaseChoices.FirstOrDefault(option => option.Phase == action.Phase) ?? phaseChoices[0],
            FontSize = 11,
            MinHeight = 28,
            Margin = new Thickness(0, 0, 0, 5),
            ToolTip = "When this action runs: on the match, after the wait-until step, or while the rule is not matching."
        };
        phase.SelectionChanged += (_, _) =>
        {
            if (_building || phase.SelectedItem is not PhaseChoice choice) return;
            action.Phase = choice.Phase;
            Persist();
        };
        panel.Children.Add(FieldLabel("When"));
        panel.Children.Add(phase);

        var display = _catalog.FindDisplay(_displayId);
        if (action.Kind == AutomationActionKind.SwitchLayout)
        {
            var targets = (display?.Layouts ?? []).Select(layout => new LayoutChoice(layout.Id, layout.Name)).ToArray();
            var target = new ComboBox
            {
                ItemsSource = targets,
                DisplayMemberPath = "Label",
                SelectedItem = targets.FirstOrDefault(choice => string.Equals(choice.Id, action.LayoutId, StringComparison.OrdinalIgnoreCase)),
                FontSize = 11,
                MinHeight = 28,
                Margin = new Thickness(0, 0, 0, 5)
            };
            target.SelectionChanged += (_, _) =>
            {
                if (_building || target.SelectedItem is not LayoutChoice choice) return;
                action.LayoutId = choice.Id;
                Persist();
            };
            panel.Children.Add(FieldLabel("Layout to switch to"));
            panel.Children.Add(target);
            panel.Children.Add(new TextBlock
            {
                Text = "This fires the first time the rule matches. It does not keep forcing the layout while the condition stays true.",
                Foreground = MutedBrush,
                FontSize = 9,
                TextWrapping = TextWrapping.Wrap
            });
        }
        else
        {
            var elements = ElementChoices(display);
            var target = new ComboBox
            {
                ItemsSource = elements,
                DisplayMemberPath = "Label",
                SelectedItem = elements.FirstOrDefault(choice => string.Equals(choice.ElementId, action.ElementId, StringComparison.OrdinalIgnoreCase)),
                FontSize = 11,
                MinHeight = 28,
                Margin = new Thickness(0, 0, 0, 5)
            };
            target.SelectionChanged += (_, _) =>
            {
                if (_building || target.SelectedItem is not ElementChoice choice) return;
                action.ElementId = choice.ElementId;
                action.LayoutId = choice.LayoutId;
                Persist();
            };
            panel.Children.Add(FieldLabel("Element"));
            panel.Children.Add(target);

            if (action.Kind is AutomationActionKind.SetTextColor or AutomationActionKind.SetBackgroundColor or AutomationActionKind.SetBorderColor)
            {
                panel.Children.Add(FieldLabel("Colour"));
                var row = new DockPanel();
                var swatch = new Border
                {
                    Width = 22, Height = 22, CornerRadius = new CornerRadius(4),
                    Background = ColorBrush(action.Value), BorderBrush = EdgeBrush,
                    BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 5, 0),
                    ToolTip = action.Value
                };
                DockPanel.SetDock(swatch, Dock.Left);
                row.Children.Add(swatch);
                var choose = new Button { Content = "Choose…", FontSize = 9, Padding = new Thickness(6, 2, 6, 2) };
                DockPanel.SetDock(choose, Dock.Right);
                choose.Click += (_, _) =>
                {
                    var chosen = ColorPickerWindow.Pick(this, action.Value);
                    if (chosen is null) return;
                    action.Value = chosen;
                    Persist();
                    RefreshGraph();
                };
                row.Children.Add(choose);
                var hex = Box(action.Value, 11);
                hex.TextChanged += (_, _) =>
                {
                    if (_building) return;
                    action.Value = hex.Text.Trim();
                    swatch.Background = ColorBrush(action.Value);
                    swatch.ToolTip = action.Value;
                    Persist();
                };
                row.Children.Add(hex);
                panel.Children.Add(row);
            }
            else if (action.Kind == AutomationActionKind.SetText)
            {
                panel.Children.Add(FieldLabel("Text or template"));
                var text = Box(action.Value, 11);
                text.TextChanged += (_, _) =>
                {
                    if (_building) return;
                    action.Value = text.Text;
                    Persist();
                };
                panel.Children.Add(text);
            }
            else if (action.Kind == AutomationActionKind.SetOpacity)
            {
                panel.Children.Add(FieldLabel("Opacity (0–1)"));
                var number = NumberBox(action.Number, value => { action.Number = value; Persist(); });
                panel.Children.Add(number);
            }
            else if (action.Kind is AutomationActionKind.MoveBy or AutomationActionKind.MoveTo)
            {
                panel.Children.Add(FieldLabel("X"));
                panel.Children.Add(NumberBox(action.Number, value => { action.Number = value; Persist(); }));
                panel.Children.Add(FieldLabel("Y"));
                panel.Children.Add(NumberBox(action.Number2, value => { action.Number2 = value; Persist(); }));
            }
            else if (action.Kind == AutomationActionKind.Resize)
            {
                panel.Children.Add(FieldLabel("Width"));
                panel.Children.Add(NumberBox(action.Number, value => { action.Number = value; Persist(); }));
                panel.Children.Add(FieldLabel("Height"));
                panel.Children.Add(NumberBox(action.Number2, value => { action.Number2 = value; Persist(); }));
            }
            else
            {
                panel.Children.Add(new TextBlock
                {
                    Text = action.Kind == AutomationActionKind.Show ? "Makes the element visible while the rule matches." : "Hides the element while the rule matches.",
                    Foreground = MutedBrush,
                    FontSize = 10,
                    TextWrapping = TextWrapping.Wrap
                });
            }
        }

        panel.Children.Add(new TextBlock
        {
            Text = AutomationRules.Describe(action, ElementName(display, action.ElementId)),
            Foreground = MutedBrush,
            FontSize = 9,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0)
        });
        var remove = new Button { Content = "Remove action", FontSize = 9, Padding = new Thickness(7, 2, 7, 2), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 7, 0, 0) };
        remove.Click += (_, _) =>
        {
            _selected?.Actions.Remove(action);
            Persist();
            RefreshGraph();
        };
        panel.Children.Add(remove);
        return card;
    }

    TextBox NumberBox(double value, Action<double> changed)
    {
        var box = Box(value.ToString("0.##"), 11);
        box.TextChanged += (_, _) =>
        {
            if (_building || !double.TryParse(box.Text, out var number)) return;
            changed(number);
        };
        return box;
    }

    static string ElementName(ScorebugDisplay? display, string elementId)
    {
        if (display is null || string.IsNullOrWhiteSpace(elementId)) return "element";
        foreach (var layout in display.Layouts)
        {
            var element = layout.Elements.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, elementId, StringComparison.OrdinalIgnoreCase));
            if (element is not null) return element.Name;
        }
        return "element";
    }

    static ElementChoice[] ElementChoices(ScorebugDisplay? display)
    {
        if (display is null) return [];
        return display.Layouts
            .SelectMany(layout => layout.Elements.Select(element => new ElementChoice(
                layout.Id, element.Id, $"{layout.Name} · {element.Name}")))
            .ToArray();
    }

    void AddCard(Border card, double x, double y, double width)
    {
        card.Width = width;
        Canvas.SetLeft(card, x);
        Canvas.SetTop(card, y);
        Panel.SetZIndex(card, 10);
        _graph.Children.Add(card);
    }

    void AddGraphLabel(double x, double y, string text, Brush brush, double size)
    {
        var label = new TextBlock { Text = text, Foreground = brush, FontSize = size, TextWrapping = TextWrapping.Wrap, Width = 280 };
        Canvas.SetLeft(label, x);
        Canvas.SetTop(label, y);
        _graph.Children.Add(label);
    }

    void DrawFan(double startX, double startY, double busX, double endX, IReadOnlyList<double> endYs, Brush brush)
    {
        if (endYs.Count == 0) return;
        DrawLine(startX, startY, busX, startY, brush);
        var min = Math.Min(startY, endYs.Min());
        var max = Math.Max(startY, endYs.Max());
        if (max - min > 0.5) DrawLine(busX, min, busX, max, brush);
        foreach (var y in endYs) DrawLine(busX, y, endX, y, brush);
    }

    void DrawBus(double startX, IReadOnlyList<double> startYs, double busX, double endX, IReadOnlyList<double> endYs, Brush brush)
    {
        if (startYs.Count == 0 || endYs.Count == 0) return;
        var min = Math.Min(startYs.Min(), endYs.Min());
        var max = Math.Max(startYs.Max(), endYs.Max());
        DrawLine(startX, startYs[0], busX, startYs[0], brush);
        if (startYs.Count > 1) DrawLine(startX, startYs[^1], busX, startYs[^1], brush);
        DrawLine(busX, min, busX, max, brush);
        foreach (var y in endYs) DrawLine(busX, y, endX, y, brush);
    }

    void DrawLine(double x1, double y1, double x2, double y2, Brush brush)
    {
        _graph.Children.Add(new Line
        {
            X1 = x1, Y1 = y1, X2 = x2, Y2 = y2,
            Stroke = brush, StrokeThickness = 1.6, IsHitTestVisible = false
        });
    }

    Border Card(string eyebrow, Brush accent)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = eyebrow, Foreground = accent, FontSize = 9, FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 5)
        });
        return new Border
        {
            Background = PanelBrush,
            BorderBrush = EdgeBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(11, 10, 11, 10),
            Child = panel,
            MinHeight = 150
        };
    }

    static TextBlock FieldLabel(string text) => new()
    {
        Text = text, Foreground = MutedBrush, FontSize = 9, Margin = new Thickness(0, 4, 0, 2)
    };

    TextBox Box(string text, double size, bool bold = false)
    {
        var box = new TextBox
        {
            Text = text,
            FontSize = size,
            FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
            Padding = new Thickness(5, 3, 5, 3),
            Margin = new Thickness(0, 0, 0, 2),
            Background = new SolidColorBrush(Color.FromRgb(11, 17, 24)),
            Foreground = TextBrush,
            BorderBrush = EdgeBrush
        };
        return box;
    }

    static Brush ColorBrush(string text)
    {
        try
        {
            if (ColorConverter.ConvertFromString(text) is Color color)
            {
                var brush = new SolidColorBrush(color);
                brush.Freeze();
                return brush;
            }
        }
        catch { }
        return Brushes.Transparent;
    }

    bool MatchesNow(AutomationRule rule) =>
        rule.Enabled && rule.Matches(rule.BypassOutputUpdate ? _liveEngine?.State ?? _engine.State : _engine.State);

    void TestRules()
    {
        var matched = _rules.Count(MatchesNow);
        _status.Text = $"{matched} of {_rules.Count} rules match the current game state. Bypass rules use the live game; the others use the published copy.";
    }

    static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    // The custom ComboBox template draws SelectionBoxItem directly, so the records must print
    // their human label rather than the CLR type name.
    sealed record FieldChoice(AutomationField Field, string Label) { public override string ToString() => Label; }
    sealed record OperatorChoice(AutomationOperator Operator, string Label) { public override string ToString() => Label; }
    sealed record ActionChoice(AutomationActionKind Kind, string Label) { public override string ToString() => Label; }
    sealed record ElementChoice(string LayoutId, string ElementId, string Label) { public override string ToString() => Label; }
    sealed record LayoutChoice(string Id, string Label) { public override string ToString() => Label; }
    sealed record PhaseChoice(AutomationActionPhase Phase, string Label) { public override string ToString() => Label; }
}
