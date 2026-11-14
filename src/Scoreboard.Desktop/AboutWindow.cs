using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Scoreboard.Desktop;

/// <summary>
/// The small About box. It deliberately carries only the logo, product name, running release and
/// the project credit, so it stays a quick identity check rather than another settings page.
/// </summary>
public sealed class AboutWindow : Window
{
    public AboutWindow(string theme)
    {
        Title = "About ScorePlus";
        Width = 360;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        FontFamily = new FontFamily("Segoe UI");

        var root = new StackPanel { Margin = new Thickness(24, 22, 24, 18) };
        var logo = new Image
        {
            Source = new BitmapImage(new Uri("pack://application:,,,/ScorePlus;component/Assets/logo.png")),
            Height = 58,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        RenderOptions.SetBitmapScalingMode(logo, BitmapScalingMode.HighQuality);
        var logoPlate = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x0A, 0x0F, 0x16)),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(16, 12, 16, 12),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 16),
            Child = logo
        };
        ThemeManager.SetSkipTheme(logoPlate, true);
        root.Children.Add(logoPlate);
        root.Children.Add(new TextBlock
        {
            Text = "ScorePlus",
            FontSize = 24,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 4)
        });
        root.Children.Add(new TextBlock
        {
            Text = $"Version {AppInfo.Release}",
            FontSize = 12,
            Opacity = 0.8,
            Margin = new Thickness(0, 0, 0, 18)
        });
        root.Children.Add(new TextBlock
        {
            Text = "CREDITS",
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Opacity = 0.6,
            Margin = new Thickness(0, 0, 0, 4)
        });
        root.Children.Add(new TextBlock
        {
            Text = "LHUltimate",
            FontSize = 13,
            FontWeight = FontWeights.SemiBold
        });

        var close = new Button
        {
            Content = "Close",
            IsDefault = true,
            IsCancel = true,
            MinHeight = 34,
            MinWidth = 84,
            Padding = new Thickness(16, 6, 16, 6),
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 20, 0, 0)
        };
        close.Click += (_, _) => Close();
        root.Children.Add(close);

        Content = root;
        ThemeManager.Apply(this, theme);
        Loaded += (_, _) => close.Focus();
    }
}
