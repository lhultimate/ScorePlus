using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;

namespace Scoreboard.Desktop;

/// <summary>
/// The peak meter and show line from the main rail, packaged so a detached control panel can carry
/// the same thing. It reads the shared <see cref="SoundLevelMeter"/>, so every window shows the same
/// level instead of each opening its own capture.
/// </summary>
public sealed class SoundMeterView : Border
{
    const int Segments = 22;

    readonly Border[] _bars = new Border[Segments];
    readonly TextBlock _level = new() { Text = "—", Foreground = new SolidColorBrush(Color.FromRgb(0x93, 0xA0, 0xB2)), FontSize = 10, FontWeight = FontWeights.Bold, Margin = new Thickness(6, 0, 0, 0) };
    readonly TextBlock _status = new() { Text = "STANDBY", Foreground = new SolidColorBrush(Color.FromRgb(0x38, 0xD7, 0xA3)), FontSize = 10, FontWeight = FontWeights.Bold, TextTrimming = TextTrimming.CharacterEllipsis };
    readonly TextBlock _detail = new() { Text = "—", Foreground = new SolidColorBrush(Color.FromRgb(0x93, 0xA0, 0xB2)), FontSize = 10, Margin = new Thickness(0, 3, 0, 0), TextWrapping = TextWrapping.Wrap };
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(70) };
    int _lit = -1;

    public SoundMeterView()
    {
        Background = new SolidColorBrush(Color.FromRgb(0x17, 0x1C, 0x25));
        CornerRadius = new CornerRadius(9);
        Padding = new Thickness(10, 8, 10, 8);
        Margin = new Thickness(0, 0, 0, 9);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var meter = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var caption = new DockPanel();
        DockPanel.SetDock(_level, Dock.Right);
        caption.Children.Add(_level);
        caption.Children.Add(new TextBlock { Text = "SOUND LEVEL", Foreground = new SolidColorBrush(Color.FromRgb(0x5C, 0x6B, 0x7E)), FontSize = 10, FontWeight = FontWeights.Bold });
        meter.Children.Add(caption);

        var segments = new UniformGrid { Columns = Segments, Height = 12, Margin = new Thickness(0, 5, 0, 0) };
        var unlit = new SolidColorBrush(Color.FromRgb(0x1B, 0x24, 0x30));
        unlit.Freeze();
        for (var index = 0; index < _bars.Length; index++)
        {
            var fraction = (index + 1) / (double)_bars.Length;
            var colour = fraction > 0.92 ? Color.FromRgb(0xFF, 0x6B, 0x6B)
                : fraction > 0.78 ? Color.FromRgb(0xE8, 0xB2, 0x3A)
                : Color.FromRgb(0x38, 0xD7, 0xA3);
            var bar = new Border
            {
                Background = unlit,
                CornerRadius = new CornerRadius(1),
                Margin = new Thickness(1, 0, 1, 0),
                ToolTip = fraction > 0.92 ? "Full scale — the output is clipping" : null,
                Tag = colour
            };
            ThemeManager.SetSkipTheme(bar, true);
            _bars[index] = bar;
            segments.Children.Add(bar);
        }
        meter.Children.Add(segments);
        grid.Children.Add(meter);

        var show = new StackPanel { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, MaxWidth = 220 };
        show.Children.Add(_status);
        show.Children.Add(_detail);
        Grid.SetColumn(show, 1);
        grid.Children.Add(show);

        Child = grid;
        _timer.Tick += (_, _) => UpdateMeter();
        Loaded += (_, _) => _timer.Start();
        Unloaded += (_, _) => _timer.Stop();
    }

    /// <summary>What the show line reads: what is on air, and the score the audience is looking at.</summary>
    public void SetStatus(string status, string detail)
    {
        _status.Text = status;
        _detail.Text = detail;
    }

    void UpdateMeter()
    {
        if (!IsVisible) return;
        var level = SoundLevelMeter.Read();
        // The same square-root scale as the main rail, so quiet speech still moves the meter.
        var lit = (int)Math.Round(Math.Sqrt(level) * _bars.Length);
        if (lit != _lit)
        {
            _lit = lit;
            var unlit = new SolidColorBrush(Color.FromRgb(0x1B, 0x24, 0x30));
            unlit.Freeze();
            for (var index = 0; index < _bars.Length; index++)
                _bars[index].Background = index < lit ? new SolidColorBrush((Color)_bars[index].Tag) : unlit;
        }
        _level.Text = level <= 0
            ? (SoundLevelMeter.Available ? "−∞ dB" : "no output")
            : $"{SoundLevelMeter.Decibels(level):0} dB";
    }
}
