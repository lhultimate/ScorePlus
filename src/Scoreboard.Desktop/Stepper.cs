using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace Scoreboard.Desktop;

/// <summary>
/// One editable value with a stack of add buttons above it and subtract buttons below it,
/// so a score or stat reads exactly like the broadcast console it replaces.
/// The middle value can be clicked and typed over; Enter or leaving the field commits it.
/// </summary>
public sealed class Stepper : Grid
{
    public sealed record StepButton(string Text, int Delta, string? ActionId, string? Tip = null);

    readonly TextBox _value = new();
    readonly UniformGrid _plusRow = new();
    readonly UniformGrid _minusRow = new();
    readonly Grid _captionRow = new();
    readonly bool _compact;
    FrameworkElement? _captionAccessory;
    bool _typing;

    /// <summary>Raised when a +/− button is pressed, carrying that button's definition.</summary>
    public event Action<StepButton>? Stepped;

    /// <summary>Raised when the middle value is typed over and committed.</summary>
    public event Action<int>? Committed;

    /// <summary>Raised instead of <see cref="Committed"/> when <see cref="AcceptText"/> is on.</summary>
    public event Action<string>? TextCommitted;

    /// <summary>
    /// Let the middle box hold any text, not just a number — a custom stat can be "N/A" or "3rd".
    /// Off by default, so every other stepper keeps its numeric behaviour.
    /// </summary>
    public bool AcceptText { get; set; }

    // The last value written from the game, so Escape can put a typed-over text box back.
    string? _text;

    /// <summary>
    /// A compact stepper puts −, the value and + on one line and can carry a small accessory
    /// in its caption. The tall stack remains the default for scores and stats.
    /// </summary>
    public Stepper(string? caption = null, double fontSize = 40, bool compact = false)
    {
        _compact = compact;
        if (compact)
        {
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        }
        else
        {
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        _captionRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _captionRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        // A compact caption with an accessory has to reserve the accessory's height, so every
        // compact stepper reserves it: otherwise the Ball On value starts lower than Down and To Go.
        if (compact) _captionRow.MinHeight = 30;
        if (!string.IsNullOrWhiteSpace(caption))
        {
            var label = new TextBlock
            {
                Text = caption.ToUpperInvariant(),
                Foreground = new SolidColorBrush(Color.FromRgb(147, 160, 178)),
                FontSize = 10,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = compact ? VerticalAlignment.Center : VerticalAlignment.Stretch,
                Margin = compact ? new Thickness(0) : new Thickness(0, 0, 0, 4)
            };
            _captionRow.Children.Add(label);
            SetRow(_captionRow, 0);
            if (compact) SetColumnSpan(_captionRow, 3);
            Children.Add(_captionRow);
        }

        _value.Text = "0";
        _value.FontSize = fontSize;
        _value.FontWeight = FontWeights.Bold;
        _value.TextAlignment = TextAlignment.Center;
        _value.Padding = new Thickness(2, 0, 2, 0);
        _value.VerticalContentAlignment = VerticalAlignment.Center;
        _value.Cursor = Cursors.IBeam;
        _value.BorderBrush = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF));
        _value.BorderThickness = new Thickness(1);
        _value.ToolTip = "Click and type a value to set it exactly, then press Enter";
        _value.GotKeyboardFocus += (_, _) => { _typing = true; _value.SelectAll(); };
        _value.LostKeyboardFocus += (_, _) => { CommitTyped(); _typing = false; ShowValue(); };
        _value.KeyDown += Value_KeyDown;

        if (compact)
        {
            _plusRow.Width = 36;
            _plusRow.Margin = new Thickness(0, 0, 3, 0);
            SetRow(_plusRow, 1);
            SetColumn(_plusRow, 0);
            Children.Add(_plusRow);

            _value.Margin = new Thickness(0);
            _value.MinHeight = 34;
            SetRow(_value, 1);
            SetColumn(_value, 1);
            Children.Add(_value);

            _minusRow.Width = 36;
            _minusRow.Margin = new Thickness(3, 0, 0, 0);
            SetRow(_minusRow, 1);
            SetColumn(_minusRow, 2);
            Children.Add(_minusRow);
        }
        else
        {
            _plusRow.Margin = new Thickness(0, 0, 0, 3);
            SetRow(_plusRow, 1);
            Children.Add(_plusRow);

            _value.Margin = new Thickness(0, 1, 0, 1);
            SetRow(_value, 2);
            Children.Add(_value);

            _minusRow.Margin = new Thickness(0, 3, 0, 0);
            SetRow(_minusRow, 3);
            Children.Add(_minusRow);
        }
    }

    /// <summary>Put a small control (such as the ball-side switch) beside a compact caption.</summary>
    public void SetCaptionAccessory(FrameworkElement accessory)
    {
        if (_captionAccessory is not null) _captionRow.Children.Remove(_captionAccessory);
        _captionAccessory = accessory;
        SetColumn(accessory, 1);
        accessory.HorizontalAlignment = HorizontalAlignment.Right;
        accessory.VerticalAlignment = VerticalAlignment.Center;
        _captionRow.Children.Add(accessory);
    }

    public int Value
    {
        get => int.TryParse(_value.Text, out var parsed) ? parsed : 0;
        set { if (!_typing && !AcceptText) _value.Text = value.ToString(); }
    }

    public string Text
    {
        get => _value.Text;
        set
        {
            _text = value;
            if (!_typing) _value.Text = value;
        }
    }

    public void ShowValue()
    {
        if (AcceptText) { _value.Text = _text ?? ""; return; }
        _value.Text = int.TryParse(_value.Text, out var parsed) ? parsed.ToString() : "0";
    }

    public void SetButtons(IEnumerable<StepButton> plus, IEnumerable<StepButton> minus)
    {
        Fill(_plusRow, plus);
        Fill(_minusRow, minus);
    }


    void Fill(UniformGrid row, IEnumerable<StepButton> buttons)
    {
        row.Children.Clear();
        var items = buttons.ToArray();
        if (items.Length == 0) return;
        row.Columns = items.Length;
        foreach (var item in items)
        {
            var button = new Button
            {
                Content = item.Text,
                FontSize = 13,
                Padding = new Thickness(2, 6, 2, 6),
                Margin = new Thickness(2, 0, 2, 0),
                MinWidth = 0,
                MinHeight = 30,
                Tag = item.ActionId,
                ToolTip = item.Tip
            };
            button.Click += (_, _) => Stepped?.Invoke(item);
            row.Children.Add(button);
        }
    }

    void Value_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            // Enter takes the value and leaves the box, so a typed score does not keep swallowing
            // every keypress afterwards.
            CommitTyped();
            Keyboard.ClearFocus();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            _typing = false;
            ShowValue();
            Keyboard.ClearFocus();
            e.Handled = true;
        }
    }

    void CommitTyped()
    {
        if (!_typing) return;
        _typing = false;
        var text = _value.Text.Trim();
        if (AcceptText)
        {
            // Any text is a legal value: the box keeps exactly what was typed.
            TextCommitted?.Invoke(text);
            return;
        }
        if (!int.TryParse(text, out var parsed)) { ShowValue(); return; }
        parsed = Math.Max(0, parsed);
        _value.Text = parsed.ToString();
        _text = _value.Text;
        Committed?.Invoke(parsed);
    }
}
