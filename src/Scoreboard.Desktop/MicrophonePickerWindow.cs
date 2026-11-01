using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Scoreboard.Desktop;

/// <summary>
/// Asks which microphone a new library item should listen to, by the name Windows gives the device.
/// With a single microphone there is nothing to choose, so the dock adds it without opening this.
/// </summary>
internal sealed class MicrophonePickerWindow : Window
{
    readonly ListBox _list = new();
    int? _result;

    sealed record MicrophoneChoice(MicrophoneDevice Device)
    {
        public string Label => $"{Device.Name}   ·   device {Device.Index + 1}";
    }

    MicrophonePickerWindow(IReadOnlyList<MicrophoneDevice> devices, string theme)
    {
        Title = "Add microphone";
        Width = 470;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        FontFamily = new FontFamily("Segoe UI");

        var root = new StackPanel { Margin = new Thickness(20, 18, 20, 16) };
        root.Children.Add(new TextBlock
        {
            Text = "Which microphone should this item listen to?",
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 4)
        });
        root.Children.Add(new TextBlock
        {
            Text = "The item goes live from the library like a camera. Its level and output are set on the dock, so a commentary mic can feed the arena or the broadcast mix.",
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.75,
            Margin = new Thickness(0, 0, 0, 10)
        });

        _list.ItemsSource = devices.Select(device => new MicrophoneChoice(device)).ToArray();
        _list.DisplayMemberPath = "Label";
        _list.FontSize = 13;
        _list.MinHeight = 96;
        _list.Margin = new Thickness(0, 0, 0, 12);
        _list.SelectedIndex = 0;
        _list.MouseDoubleClick += (_, _) => Accept();
        root.Children.Add(_list);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinHeight = 36, Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(4, 0, 0, 0) };
        var add = new Button { Content = "Add microphone", IsDefault = true, MinHeight = 36, Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(4, 0, 0, 0) };
        cancel.Click += (_, _) => { DialogResult = false; Close(); };
        add.Click += (_, _) => Accept();
        buttons.Children.Add(cancel);
        buttons.Children.Add(add);
        root.Children.Add(buttons);
        Content = root;
        ThemeManager.Apply(this, theme);
        Loaded += (_, _) => _list.Focus();
    }

    void Accept()
    {
        if (_list.SelectedItem is not MicrophoneChoice choice) return;
        _result = choice.Device.Index;
        DialogResult = true;
        Close();
    }

    /// <summary>The chosen capture device, or null when the operator cancelled.</summary>
    public static int? Ask(Window owner, IReadOnlyList<MicrophoneDevice> devices, string theme)
    {
        var window = new MicrophonePickerWindow(devices, theme) { Owner = owner };
        return window.ShowDialog() == true ? window._result : null;
    }
}
