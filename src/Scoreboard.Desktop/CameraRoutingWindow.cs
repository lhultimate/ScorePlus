using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Scoreboard.Desktop;

/// <summary>
/// The little menu behind a camera item's Route button: what the operator calls it, and which
/// capture device it actually uses. Applying it while the item is live switches cameras on air.
/// </summary>
internal sealed class CameraRoutingWindow : Window
{
    readonly TextBox _name = new();
    readonly ListBox _devices = new();
    int? _chosenDevice;

    CameraRoutingWindow(MediaSession session, MediaEntry entry, string theme)
    {
        Title = "Route camera";
        Width = 430;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        FontFamily = new FontFamily("Segoe UI");

        var root = new StackPanel { Margin = new Thickness(20, 18, 20, 16) };
        root.Children.Add(new TextBlock
        {
            Text = "Name and camera",
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 4)
        });
        root.Children.Add(new TextBlock
        {
            Text = "Give this item a name the crew recognises, then point it at a camera. The camera is listed by the name Windows knows it by. Changing it while the item is live switches the shot immediately.",
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.75,
            Margin = new Thickness(0, 0, 0, 10)
        });

        root.Children.Add(new TextBlock { Text = "NAME", FontSize = 10, FontWeight = FontWeights.Bold, Opacity = 0.7, Margin = new Thickness(0, 0, 0, 4) });
        _name.Text = entry.Name ?? "";
        _name.FontSize = 13;
        _name.Padding = new Thickness(8, 6, 8, 6);
        _name.Margin = new Thickness(0, 0, 0, 10);
        _name.ToolTip = $"Leave empty to call it {DefaultLabel(entry)}";
        root.Children.Add(_name);

        root.Children.Add(new TextBlock { Text = "CAMERA", FontSize = 10, FontWeight = FontWeights.Bold, Opacity = 0.7, Margin = new Thickness(0, 0, 0, 4) });
        var cameras = session.CameraDevices();
        _devices.ItemsSource = cameras.Select(device => new DeviceChoice(device, entry.CameraIndex)).ToArray();
        _devices.DisplayMemberPath = "Label";
        _devices.FontSize = 13;
        _devices.MinHeight = 92;
        _devices.Margin = new Thickness(0, 0, 0, 6);
        _devices.SelectedItem = _devices.Items.Cast<DeviceChoice>().FirstOrDefault(choice => choice.Index == entry.CameraIndex);
        if (_devices.SelectedItem is null && _devices.Items.Count > 0) _devices.SelectedIndex = 0;
        _devices.DoubleTapped += (_, _) => Accept();
        root.Children.Add(_devices);

        if (cameras.Count == 0)
            root.Children.Add(new TextBlock
            {
                Text = "No camera could be opened just now. Check that it is plugged in and not in use by another app.",
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.7
            });

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinHeight = 36, Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(4, 0, 0, 0) };
        var save = new Button { Content = "Apply", IsDefault = true, MinHeight = 36, Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(4, 0, 0, 0) };
        cancel.Click += (_, _) => { DialogResult = false; Close(); };
        save.Click += (_, _) => Accept();
        buttons.Children.Add(cancel);
        buttons.Children.Add(save);
        root.Children.Add(buttons);

        Content = root;
        ThemeManager.Apply(this, theme);
        Loaded += (_, _) => { _name.Focus(); _name.SelectAll(); };
    }

    static string DefaultLabel(MediaEntry entry) => CameraFeed.CameraName(entry.CameraIndex);

    void Accept()
    {
        _chosenDevice = (_devices.SelectedItem as DeviceChoice)?.Index;
        DialogResult = true;
        Close();
    }

    public static void Ask(Window owner, MediaSession session, MediaEntry entry, string theme)
    {
        var dialog = new CameraRoutingWindow(session, entry, theme) { Owner = owner };
        if (dialog.ShowDialog() != true) return;
        session.RenameCamera(entry, dialog._name.Text);
        if (dialog._chosenDevice is int device) session.RouteCamera(entry, device);
    }

    sealed class DeviceChoice
    {
        public DeviceChoice(CameraDevice device, int current)
        {
            Index = device.Index;
            Label = device.Index == current
                ? $"{device.Name}   ·   camera {device.Index + 1}   ·   in use by this item"
                : $"{device.Name}   ·   camera {device.Index + 1}";
        }

        public int Index { get; }
        public string Label { get; }
    }
}
