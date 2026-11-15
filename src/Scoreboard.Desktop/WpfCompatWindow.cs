// Compile-time compatibility for WPF window call sites during the Avalonia port.
using Avalonia;
using Avalonia.Controls;

namespace Scoreboard.Desktop;

public enum ResizeMode { NoResize, CanMinimize, CanResize, CanResizeWithGrip }

public class Window : Avalonia.Controls.Window
{
    private bool? _dialogResult;

    public bool? DialogResult
    {
        get => _dialogResult;
        set { _dialogResult = value; Close(value); }
    }

    public ResizeMode ResizeMode { get; set; } = ResizeMode.CanResize;

    public new Window? Owner
    {
        get => base.Owner as Window;
        set => base.Owner = value;
    }

    public double Left
    {
        get => Position.X;
        set => Position = new PixelPoint((int)value, Position.Y);
    }

    public double Top
    {
        get => Position.Y;
        set => Position = new PixelPoint(Position.X, (int)value);
    }

    /// <summary>
    /// WPF's blocking ShowDialog. Runs a nested dispatcher frame (see WpfDialogExtensions), so the
    /// dialog is modal to the console in the sense the ported code expects: the call does not return
    /// until the window closes, and it returns the DialogResult the dialog set.
    /// </summary>
    public bool? ShowDialog(Window? owner = null)
    {
        if (owner is not null) Owner = owner;
        return WpfDialogExtensions.ShowDialogPumped(this, owner);
    }

    public static Window? GetWindow(Control control) => TopLevel.GetTopLevel(control) as Window;
}

public static class FontWeights
{
    public static Avalonia.Media.FontWeight Normal => Avalonia.Media.FontWeight.Normal;
    public static Avalonia.Media.FontWeight Medium => Avalonia.Media.FontWeight.Medium;
    public static Avalonia.Media.FontWeight SemiBold => Avalonia.Media.FontWeight.SemiBold;
    public static Avalonia.Media.FontWeight Bold => Avalonia.Media.FontWeight.Bold;
}

public static class FontStyles
{
    public static Avalonia.Media.FontStyle Normal => Avalonia.Media.FontStyle.Normal;
    public static Avalonia.Media.FontStyle Italic => Avalonia.Media.FontStyle.Italic;
    public static Avalonia.Media.FontStyle Oblique => Avalonia.Media.FontStyle.Oblique;
}

public static class Orientation
{
    public static Avalonia.Layout.Orientation Horizontal => Avalonia.Layout.Orientation.Horizontal;
    public static Avalonia.Layout.Orientation Vertical => Avalonia.Layout.Orientation.Vertical;
}
