using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Scoreboard.Core;

namespace Scoreboard.Desktop;

/// <summary>
/// The advertisement log: one line per clip the show carries, with its length and how many times
/// it has gone to air. Reset sets the impressions back to zero after a confirmation — the lines and
/// lengths stay, so the list still shows what the show runs.
/// </summary>
public sealed class AdLogWindow : Window
{
    readonly string _theme;
    readonly StackPanel _rows = new();
    readonly TextBlock _summary = new() { FontSize = 11, Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center };
    readonly Button _reset;

    public AdLogWindow(string theme)
    {
        _theme = theme;
        Title = "ScorePlus — Advertisement Log";
        Width = 820;
        Height = 620;
        MinWidth = 680;
        MinHeight = 460;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        FontFamily = new FontFamily("Segoe UI");
        ScreenFit.Track(this);

        var root = new DockPanel { Margin = new Thickness(20, 18, 20, 16) };

        // ---- header ----
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal };
        var accent = new Border
        {
            Width = 4,
            Height = 24,
            CornerRadius = new CornerRadius(2),
            Background = new SolidColorBrush(Color.FromRgb(0x38, 0xD7, 0xA3)),
            Margin = new Thickness(0, 0, 10, 0)
        };
        ThemeManager.SetSkipTheme(accent, true);
        titleRow.Children.Add(accent);
        titleRow.Children.Add(new TextBlock
        {
            Text = "ADVERTISEMENT LOG",
            FontSize = 19,
            FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center
        });
        header.Children.Add(titleRow);
        header.Children.Add(new TextBlock
        {
            Text = "Every clip's impressions and its length. An impression is one play to air — from the dock, a playlist or a quick-media button.",
            FontSize = 11,
            Opacity = 0.7,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0)
        });
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        // ---- footer ----
        var footer = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
        var close = new Button
        {
            Content = "Close",
            IsCancel = true,
            MinHeight = 36,
            MinWidth = 96,
            Padding = new Thickness(14, 7, 14, 7)
        };
        close.Click += (_, _) => Close();
        DockPanel.SetDock(close, Dock.Right);
        footer.Children.Add(close);

        _reset = new Button
        {
            Content = "Reset impressions…",
            MinHeight = 36,
            MinWidth = 150,
            Padding = new Thickness(14, 7, 14, 7),
            Margin = new Thickness(0, 0, 8, 0)
        };
        _reset.Click += (_, _) => ResetImpressions();
        DockPanel.SetDock(_reset, Dock.Right);
        footer.Children.Add(_reset);
        footer.Children.Add(_summary);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);

        // ---- the table ----
        var table = new DockPanel();
        var head = Row(isHeader: true, "CLIP", "LENGTH", "IMPRESSIONS", "LAST PLAYED");
        DockPanel.SetDock(head, Dock.Top);
        table.Children.Add(head);
        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Margin = new Thickness(0, 2, 0, 0)
        };
        scroll.Content = _rows;
        table.Children.Add(scroll);
        root.Children.Add(table);

        Content = root;
        ThemeManager.Apply(this, theme);
        AdLogStore.Changed += Refresh;
        Closed += (_, _) => AdLogStore.Changed -= Refresh;
        Loaded += (_, _) => Refresh();
    }

    void ResetImpressions()
    {
        var total = AdLogStore.TotalPlays;
        if (total == 0)
        {
            NoticeWindow.Tell(this, "Nothing to reset", "No clip has been played since the log was last reset.");
            return;
        }
        if (!ConfirmWindow.Ask(this, _theme, "RESET IMPRESSIONS",
                $"Set every impression back to zero? The log counts {total} play{(total == 1 ? "" : "s")} across {AdLogStore.PlayedCount} clip{(AdLogStore.PlayedCount == 1 ? "" : "s")}. Lengths and clip names stay; this cannot be undone.",
                "Reset to zero", destructive: true)) return;
        AdLogStore.Reset();
        Refresh();
    }

    void Refresh()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(Refresh); return; }
        _rows.Children.Clear();
        var clips = AdLogStore.All();
        foreach (var clip in clips)
            _rows.Children.Add(Row(false, clip.Name, clip.DurationLabel, clip.PlaysLabel, clip.LastPlayed is null ? "never" : clip.LastLabel));

        if (clips.Count == 0)
        {
            var empty = new TextBlock
            {
                Text = "No clips yet. Add media in the Media tab, then play it to air — every play lands here with its length.",
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.65,
                Margin = new Thickness(6, 18, 6, 6)
            };
            _rows.Children.Add(empty);
        }

        var plays = AdLogStore.TotalPlays;
        _summary.Text = clips.Count == 0
            ? "The log is empty."
            : $"{plays} impression{(plays == 1 ? "" : "s")} · {AdLogStore.PlayedCount} clip{(AdLogStore.PlayedCount == 1 ? "" : "s")} played";
        _reset.IsEnabled = plays > 0;
    }

    /// <summary>One table line: a clip's name, its length, its count and when it last ran.</summary>
    static Border Row(bool isHeader, string name, string length, string plays, string last)
    {
        var grid = new Grid { Margin = new Thickness(6, 0, 6, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 220 });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(86) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(112) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });

        void Cell(int column, string text, bool bold = false, bool dim = false)
        {
            var block = new TextBlock
            {
                Text = text,
                FontSize = isHeader ? 10 : 12,
                FontWeight = bold || isHeader ? FontWeights.Bold : FontWeights.Normal,
                Opacity = dim ? 0.62 : 1,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 6, 0)
            };
            if (isHeader) block.Opacity = 0.62;
            Grid.SetColumn(block, column);
            grid.Children.Add(block);
        }

        Cell(0, name);
        Cell(1, length, dim: !isHeader);
        Cell(2, plays, bold: !isHeader);
        Cell(3, last, dim: !isHeader);

        var border = new Border
        {
            Padding = new Thickness(2, isHeader ? 4 : 7, 2, isHeader ? 4 : 7),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = grid
        };
        border.SetResourceReference(Border.BorderBrushProperty, "OperatorBorderBrush");
        if (isHeader) border.SetResourceReference(Border.BackgroundProperty, "OperatorSurfaceBrush");
        return border;
    }
}
