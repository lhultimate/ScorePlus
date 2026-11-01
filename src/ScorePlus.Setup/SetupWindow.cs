using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace ScorePlus.Setup;

/// <summary>
/// The installer's own window, in the app's dark chrome. It installs per user, shows where the app
/// goes, and stays open afterwards so the operator can launch it straight away.
/// </summary>
public sealed class SetupWindow : Window
{
    // The setup wears the app's own dark chrome — near-black panels with a light edge — instead of
    // the old navy, so the installer, the app and the dialogs inside it read as one product.
    static readonly Brush Panel = Frozen(Color.FromRgb(0x08, 0x08, 0x08));
    static readonly Brush Field = Frozen(Color.FromRgb(0x05, 0x05, 0x05));
    static readonly Brush Edge = Frozen(Color.FromRgb(0x38, 0x38, 0x38));
    static readonly Brush Muted = Frozen(Color.FromRgb(0xB0, 0xB0, 0xB0));
    static readonly Brush Ink = Frozen(Color.FromRgb(0xF4, 0xF7, 0xFB));
    static readonly Brush Accent = Frozen(Color.FromRgb(0x38, 0xD7, 0xA3));
    static readonly Brush AccentFill = Frozen(Color.FromRgb(0x0B, 0x6B, 0x50));
    static readonly Brush AccentEdge = Frozen(Color.FromRgb(0x14, 0xA6, 0x7C));

    readonly bool _uninstall;
    readonly TextBox _folder = new();
    readonly CheckBox _desktop = new();
    readonly TextBlock _status = new();
    readonly ProgressBar _progress = new();
    readonly Button _primary;
    readonly StackPanel _folderRow;
    bool _done;

    public SetupWindow(bool uninstall)
    {
        _uninstall = uninstall;
        Title = uninstall ? "Remove ScorePlus" : "ScorePlus Setup";
        Width = 620;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.FromRgb(0x00, 0x00, 0x00));
        Foreground = Ink;
        FontFamily = new FontFamily("Segoe UI");

        var root = new StackPanel { Margin = new Thickness(26, 22, 26, 20) };

        var brand = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 14) };
        brand.Children.Add(new TextBlock { Text = "●", Foreground = Accent, FontSize = 18, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center });
        brand.Children.Add(new TextBlock { Text = "SCOREPLUS", FontSize = 18, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center });
        brand.Children.Add(new TextBlock { Text = "  ·  SETUP", Foreground = Muted, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
        root.Children.Add(brand);

        root.Children.Add(new TextBlock
        {
            Text = uninstall ? "Remove ScorePlus from this computer" : "Install the live production console",
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 4)
        });
        root.Children.Add(new TextBlock
        {
            Text = uninstall
                ? $"Version {Installer.DisplayVersion} · removes the app, its shortcuts and its installer entry. Saved games, media and preferences stay in your profile."
                : $"Version {Installer.DisplayVersion} · installs for this user only, so no administrator password is needed. Saved games, media and preferences stay in your profile.",
            Foreground = Muted,
            FontSize = 11.5,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 16)
        });

        _folderRow = new StackPanel();
        _folderRow.Children.Add(new TextBlock { Text = "INSTALL FOLDER", FontSize = 10, FontWeight = FontWeights.Bold, Foreground = Muted, Margin = new Thickness(2, 0, 0, 5) });
        var folderLine = new DockPanel();
        var browse = MakeButton("Browse…", primary: false);
        browse.Margin = new Thickness(8, 0, 0, 0);
        browse.MinHeight = 36;
        browse.Click += (_, _) =>
        {
            var picker = new OpenFolderDialog { Title = "Where should ScorePlus go?" };
            if (Directory.Exists(_folder.Text)) picker.InitialDirectory = _folder.Text;
            if (picker.ShowDialog() == true) _folder.Text = picker.FolderName;
        };
        DockPanel.SetDock(browse, Dock.Right);
        folderLine.Children.Add(browse);
        folderLine.Children.Add(new Border
        {
            Background = Field,
            BorderBrush = Edge,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(8, 0, 8, 0),
            Child = _folder
        });
        _folderRow.Children.Add(folderLine);
        _folder.Text = Installer.InstalledDirectory() ?? Installer.DefaultDirectory;
        _folder.BorderThickness = new Thickness(0);
        _folder.Background = Brushes.Transparent;
        // The text, caret and selection have to be set here: a default WPF field picks its own
        // colours from the Windows theme, which is how its text disappeared on a dark panel.
        _folder.Foreground = Ink;
        _folder.CaretBrush = Ink;
        _folder.SelectionBrush = Frozen(Color.FromRgb(0x14, 0xA6, 0x7C));
        _folder.SelectionTextBrush = Brushes.White;
        _folder.VerticalContentAlignment = VerticalAlignment.Center;
        _folder.FontSize = 13;
        _folder.MinHeight = 34;
        _folder.TextChanged += (_, _) => ValidateFolder();
        root.Children.Add(_folderRow);

        _desktop.Content = "Create a shortcut on the desktop";
        _desktop.Foreground = Ink;
        _desktop.Margin = new Thickness(2, 10, 0, 0);
        root.Children.Add(_desktop);

        _progress.Height = 4;
        _progress.Margin = new Thickness(0, 16, 0, 6);
        _progress.Foreground = Accent;
        _progress.Background = Frozen(Color.FromRgb(0x1B, 0x25, 0x31));
        _progress.Visibility = Visibility.Hidden;
        root.Children.Add(_progress);

        _status.Text = uninstall ? "Ready to remove." : "Ready to install.";
        _status.Foreground = Muted;
        _status.FontSize = 11.5;
        _status.TextWrapping = TextWrapping.Wrap;
        _status.Margin = new Thickness(0, 2, 0, 16);
        root.Children.Add(_status);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var close = MakeButton("Cancel", primary: false);
        close.MinWidth = 92;
        close.Margin = new Thickness(0, 0, 8, 0);
        close.IsCancel = true;
        close.Click += (_, _) => Close();
        _primary = MakeButton(uninstall ? "Remove ScorePlus" : "Install ScorePlus", primary: true, destructive: uninstall);
        _primary.MinWidth = 156;
        _primary.Padding = new Thickness(16, 6, 16, 6);
        _primary.Click += async (_, _) => await RunAsync();
        buttons.Children.Add(close);
        buttons.Children.Add(_primary);
        root.Children.Add(buttons);

        Content = new Border
        {
            Background = Panel,
            BorderBrush = Edge,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Margin = new Thickness(14),
            Child = root
        };

        if (uninstall) _folderRow.Visibility = Visibility.Collapsed;
        else ValidateFolder();
        Loaded += (_, _) => { if (!uninstall) _folder.Focus(); };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
    }

    /// <summary>
    /// Says immediately when a folder is not a safe home for the app — a drive root, Downloads, the
    /// Desktop, or a folder already full of somebody's files — rather than letting an install point an
    /// uninstaller at it.
    /// </summary>
    void ValidateFolder()
    {
        if (_uninstall || _done || _primary is null || _status is null) return;
        if (Installer.IsSafeInstallDirectory(_folder.Text, out var reason))
        {
            _primary.IsEnabled = true;
            _status.Foreground = Muted;
            _status.Text = $"ScorePlus will be installed in {_folder.Text.Trim()}.";
        }
        else
        {
            _primary.IsEnabled = false;
            _status.Foreground = Frozen(Color.FromRgb(0xFF, 0x72, 0x72));
            _status.Text = reason;
        }
    }

    async Task RunAsync()
    {
        if (_done)
        {
            if (_uninstall) { Close(); return; }
            var exe = Path.Combine(_folder.Text.Trim(), Installer.AppExecutable);
            if (File.Exists(exe))
            {
                try { Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(exe) }); }
                catch { }
            }
            Close();
            return;
        }

        _primary.IsEnabled = false;
        _progress.Visibility = Visibility.Visible;
        _progress.IsIndeterminate = true;
        _status.Foreground = Muted;

        var folder = _folder.Text;
        var desktop = _desktop.IsChecked == true;
        var result = await Task.Run(() => _uninstall
            ? Installer.Uninstall(message => Dispatcher.Invoke(() => _status.Text = message))
            : Installer.Install(folder, desktop, message => Dispatcher.Invoke(() => _status.Text = message)));

        _progress.IsIndeterminate = false;
        _progress.Visibility = Visibility.Hidden;
        _status.Foreground = result.Ok ? Accent : Frozen(Color.FromRgb(0xFF, 0x72, 0x72));
        _status.Text = result.Message;
        _primary.IsEnabled = true;

        if (!result.Ok)
        {
            _done = false;
            _primary.Content = _uninstall ? "Try again" : "Try again";
            return;
        }
        _done = true;
        _folderRow.IsEnabled = false;
        _desktop.IsEnabled = false;
        _primary.Content = _uninstall ? "Close" : "Launch ScorePlus";
        _primary.Background = _uninstall ? Frozen(Color.FromRgb(0x1B, 0x25, 0x31)) : AccentFill;
        _primary.BorderBrush = _uninstall ? Edge : AccentEdge;
    }

    /// <summary>
    /// A button with its own chrome. The stock WPF button reads the Windows theme for its face but
    /// keeps the window's light text, which is what left white labels on pale boxes; this template
    /// paints both, with hover and pressed states of its own.
    /// </summary>
    static Button MakeButton(string text, bool primary, bool destructive = false)
    {
        var fill = destructive ? Color.FromRgb(0x6B, 0x1B, 0x2B)
            : primary ? Color.FromRgb(0x0B, 0x6B, 0x50)
            : Color.FromRgb(0x14, 0x14, 0x14);
        var edge = destructive ? Color.FromRgb(0xA6, 0x30, 0x46)
            : primary ? Color.FromRgb(0x14, 0xA6, 0x7C)
            : Color.FromRgb(0x38, 0x38, 0x38);
        var button = new Button
        {
            Content = text,
            MinHeight = 38,
            Padding = new Thickness(14, 6, 14, 6),
            Margin = new Thickness(4, 0, 0, 0),
            Background = Frozen(fill),
            BorderBrush = Frozen(edge),
            Foreground = Brushes.White,
            FontWeight = FontWeights.SemiBold,
            Cursor = Cursors.Hand
        };
        var template = new ControlTemplate(typeof(Button));
        var chrome = new FrameworkElementFactory(typeof(Border), "Chrome");
        // Bound, not fixed: the primary button changes colour when the install is done.
        chrome.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
        chrome.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Button.BorderBrushProperty));
        chrome.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        chrome.SetValue(Border.CornerRadiusProperty, new CornerRadius(7));
        chrome.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Button.PaddingProperty));
        var body = new FrameworkElementFactory(typeof(ContentPresenter));
        body.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        body.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        chrome.AppendChild(body);
        template.VisualTree = chrome;
        var hover = new Trigger { Property = IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, Frozen(Lift(fill)), "Chrome"));
        var pressed = new Trigger { Property = Button.IsPressedProperty, Value = true };
        pressed.Setters.Add(new Setter(Border.BackgroundProperty, Frozen(Shade(fill)), "Chrome"));
        var disabled = new Trigger { Property = IsEnabledProperty, Value = false };
        disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.5, "Chrome"));
        template.Triggers.Add(hover);
        template.Triggers.Add(pressed);
        template.Triggers.Add(disabled);
        button.Template = template;
        return button;
    }

    /// <summary>A little brighter for hover, a little deeper for pressed.</summary>
    static Color Lift(Color color) => Color.FromRgb(
        (byte)Math.Min(255, color.R + 22), (byte)Math.Min(255, color.G + 22), (byte)Math.Min(255, color.B + 22));

    static Color Shade(Color color) => Color.FromRgb(
        (byte)(color.R * 0.78), (byte)(color.G * 0.78), (byte)(color.B * 0.78));

    static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
