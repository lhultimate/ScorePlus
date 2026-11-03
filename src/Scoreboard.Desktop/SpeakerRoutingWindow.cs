using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Scoreboard.Desktop;

/// <summary>
/// Names a speaker bus and chooses the Windows output device it represents. The bus's level is
/// controlled from the dock's normal VOLUME slider, so this dialog deliberately stays about identity
/// and routing only.
/// </summary>
internal sealed class SpeakerRoutingWindow : Window
{
    readonly TextBox _name = new();
    readonly ComboBox _devices = new() { DisplayMemberPath = "Name" };
    AudioRouter.OutputDevice? _chosen;

    SpeakerRoutingWindow(MediaSession session, MediaEntry entry, string theme)
    {
        Title = "Route speaker";
        Width = 440;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        FontFamily = new FontFamily("Segoe UI");

        var root = new StackPanel { Margin = new Thickness(20, 18, 20, 16) };
        root.Children.Add(new TextBlock
        {
            Text = "Name and speaker",
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 4)
        });
        root.Children.Add(new TextBlock
        {
            Text = "Give this output a name the crew recognises, then choose the Windows device it controls. Everything routed to that device is scaled by this bus's volume slider.",
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.75,
            Margin = new Thickness(0, 0, 0, 10)
        });

        root.Children.Add(new TextBlock { Text = "NAME", FontSize = 10, FontWeight = FontWeights.Bold, Opacity = 0.7, Margin = new Thickness(0, 0, 0, 4) });
        _name.Text = entry.Name ?? string.Empty;
        _name.FontSize = 13;
        _name.Padding = new Thickness(8, 6, 8, 6);
        _name.Margin = new Thickness(0, 0, 0, 10);
        _name.ToolTip = $"Leave empty to call it Speaker {MediaLibrary.SpeakerIndex(entry.Path) + 1}";
        root.Children.Add(_name);

        root.Children.Add(new TextBlock { Text = "SPEAKER", FontSize = 10, FontWeight = FontWeights.Bold, Opacity = 0.7, Margin = new Thickness(0, 0, 0, 4) });
        var devices = session.SpeakerDevices();
        _devices.ItemsSource = devices;
        _devices.FontSize = 13;
        _devices.MinHeight = 92;
        _devices.Margin = new Thickness(0, 0, 0, 6);
        _devices.SelectedItem = devices.FirstOrDefault(device =>
            string.Equals(device.Id, entry.AudioDevice, StringComparison.OrdinalIgnoreCase));
        if (_devices.SelectedItem is null && _devices.Items.Count > 0) _devices.SelectedIndex = 0;
        root.Children.Add(_devices);

        if (devices.Count == 0)
            root.Children.Add(new TextBlock
            {
                Text = "No speaker could be opened just now. Check that it is plugged in and available to Windows.",
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.7
            });

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
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

    void Accept()
    {
        _chosen = _devices.SelectedItem as AudioRouter.OutputDevice;
        DialogResult = true;
        Close();
    }

    public static void Ask(Window owner, MediaSession session, MediaEntry entry, string theme)
    {
        var dialog = new SpeakerRoutingWindow(session, entry, theme) { Owner = owner };
        if (dialog.ShowDialog() != true) return;
        session.SetSpeakerOutput(entry, dialog._name.Text, dialog._chosen?.Id);
    }
}
