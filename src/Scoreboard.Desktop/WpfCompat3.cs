// ScorePlus Linux port — dirty WPF compatibility shims, layer 3 (WPF-shaped members on real
// Avalonia controls via C# 14 extension members). Everything here is inert glue so the ported
// code compiles on Avalonia; TODO(port) marks each site that needs a real implementation.
//
// Shapes confirmed against Avalonia 11.3.22 by reflection: SolidColorBrush is NOT Brush
// (it implements IImmutableSolidColorBrush), so Freeze/Clone extensions live on IBrush — the
// interface every brush implements — so they are visible whether the receiver is typed Brush or
// SolidColorBrush.
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Scoreboard.Desktop;

// Avalonia's name generator does not bind names on unqualified project-local element names.
// Keep the WPF WrapPanel-shaped name used by the ported XAML as a real local type.
public class WrapFlow : WrapPanel { }

// ---------- WPF's static Dispatcher (Avalonia: Dispatcher.UIThread) ----------
// TECH DEBT: CurrentDispatcher always reports the UI thread; off-thread dispatch fidelity is lost.
// TODO(port): delete once call sites use Avalonia.Threading.Dispatcher.UIThread directly.
public static class Dispatcher
{
    public static bool CheckAccess() => Avalonia.Threading.Dispatcher.UIThread.CheckAccess();
    public static void Invoke(Action action) => Avalonia.Threading.Dispatcher.UIThread.Invoke(action);
    public static void BeginInvoke(Action action) => Avalonia.Threading.Dispatcher.UIThread.Post(action);
    public static void BeginInvoke(DispatcherPriority priority, Action action) => Avalonia.Threading.Dispatcher.UIThread.Post(action, priority);
    public static Avalonia.Threading.Dispatcher CurrentDispatcher => Avalonia.Threading.Dispatcher.UIThread;
}

// ---------- WPF enum/enum-shaped shims ----------

public enum MouseButtonState { Released, Pressed }

public enum CharacterCasing { Normal, Upper, Lower }

// WPF's ScrollBarVisibility; the ScrollViewer extensions map it onto Avalonia's enum.
public sealed class ScrollBarVisibility
{
    public static readonly ScrollBarVisibility Auto = new("Auto");
    public static readonly ScrollBarVisibility Hidden = new("Hidden");
    public static readonly ScrollBarVisibility Visible = new("Visible");
    public static readonly ScrollBarVisibility Disabled = new("Disabled");
    public string Name { get; }
    ScrollBarVisibility(string name) { Name = name; }
    public Avalonia.Controls.Primitives.ScrollBarVisibility ToAvalonia() => Name switch
    {
        "Auto" => Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
        "Hidden" => Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden,
        "Visible" => Avalonia.Controls.Primitives.ScrollBarVisibility.Visible,
        _ => Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
    };
}

// ---------- WPF statics (SystemParameters, Brushes, Fonts, DataFormats) ----------

public static class WpfSystemParameters
{
    // Dirty: work area of the primary screen; per-monitor work areas land with ScreenFit's port.
    public static Rect WorkArea
    {
        get
        {
            try
            {
                var life = Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;
                var screen = life?.MainWindow?.Screens?.Primary;
                if (screen is not null) return new Rect(screen.WorkingArea.X, screen.WorkingArea.Y, screen.WorkingArea.Width, screen.WorkingArea.Height);
            }
            catch { }
            return new Rect(0, 0, 1920, 1080);
        }
    }
}

// WPF's Brushes fields typed as Brush (Avalonia's own fields are IImmutableSolidColorBrush).
public static class Brushes
{
    public static readonly Brush Transparent = new SolidColorBrush(global::Avalonia.Media.Colors.Transparent);
    public static readonly Brush White = new SolidColorBrush(global::Avalonia.Media.Colors.White);
    public static readonly Brush Black = new SolidColorBrush(global::Avalonia.Media.Colors.Black);
    public static readonly Brush Gray = new SolidColorBrush(global::Avalonia.Media.Colors.Gray);
    public static readonly Brush DimGray = new SolidColorBrush(global::Avalonia.Media.Colors.DimGray);
    public static readonly Brush Red = new SolidColorBrush(global::Avalonia.Media.Colors.Red);
    public static readonly Brush Green = new SolidColorBrush(global::Avalonia.Media.Colors.Green);
    // ScorePlus's green; WPF has no LimeGreen that matches, this keeps the old accent compiling.
    public static readonly Brush LimeGreen = new SolidColorBrush(global::Avalonia.Media.Color.FromRgb(56, 215, 163));
    public static readonly Brush DeepSkyBlue = new SolidColorBrush(global::Avalonia.Media.Colors.DeepSkyBlue);
}

public static class Fonts
{
    public static IEnumerable<FontFamily> SystemFontFamilies => FontManager.Current.SystemFonts;

    // Avalonia has no URI font catalog API. Keep WPF's call shape for imported font files;
    // FontFamily resolves the file-backed URI when the family is used by a control.
    public static IEnumerable<FontFamily> GetFontFamilies(Uri location) => [new FontFamily(location.ToString())];
}

// WPF's DataFormats constants for drag/drop payloads.
public static class DataFormats
{
    public const string FileDrop = "FileNameW";
    public const string Text = "Text";
    public const string UnicodeText = "UnicodeText";
}

// ---------- WindowInteropHelper (win32 chrome; no role on Linux) ----------
// TECH DEBT: every handle is Zero, so win32 monitor math short-circuits and the Avalonia
// ScreenFit path takes over. TODO(port): delete once ScreenFit reads Avalonia Screens.
public static class WpfInterop
{
    public static IntPtr HandleFor(Window window) => IntPtr.Zero;
}

// ---------- PasswordBox (Avalonia ships none on this reference set; TextBox is the dirty stand-in) ----------
public class PasswordBox : TextBox
{
    public string Password
    {
        get => Text;
        set => Text = value;
    }

    public event EventHandler<RoutedEventArgs>? PasswordChanged;

    public PasswordBox() => TextChanged += (_, e) => PasswordChanged?.Invoke(this, e);
}

// ---------- WPF's BitmapScalingMode / RenderOptions attached knobs ----------
// TECH DEBT: inert; the Linux renderer picks its own sampling. TODO(port): drop with the render pass.
public enum BitmapScalingMode { NearestNeighbor, Linear, HighQuality }

public static class RenderOptions
{
    public static void SetBitmapScalingMode(Avalonia.AvaloniaObject target, BitmapScalingMode mode) { }
    public static BitmapScalingMode GetBitmapScalingMode(Avalonia.AvaloniaObject target) => BitmapScalingMode.HighQuality;
}

// ---------- WPF's VisualTreeHelper.GetParent / drag source helpers ----------
// TECH DEBT: DoDragDrop is inert on Linux until a real Avalonia drag source is wired; external
// drops (files from the desktop) still reach the Drop handlers. TODO(port): DragDrop.DoDragDropAsync.
public static class WpfDragDrop
{
    public static void DoDragDrop(object source, object data, Avalonia.Input.DragDropEffects effects) { }
}

// WPF's Transform.Identity: Avalonia treats null as identity.
public static class WpfTransforms
{
    public static Avalonia.Media.ITransform Identity => null!;
}

// ---------- WebView2 (Windows-only browser; the HTML capture path is inert on Linux) ----------
// TECH DEBT: EnsureCoreWebView2Async throws right away, so HtmlEngine records LastError and the
// scorebug draws its HTML placeholder instead. TODO(port): QtWebEngine/CEF or a headless chromium
// raster pass for HtmlSurface.

/// <summary>WPF WebView2 stand-in: compiles, never boots on Linux.</summary>
public sealed class WebView2 : Avalonia.Controls.Control
{
    public double WpfWidth { get; set; }
    public double WpfHeight { get; set; }
    public System.Drawing.Color DefaultBackgroundColor { get; set; }
    public CoreWebView2? CoreWebView2 => null;
    public System.Threading.Tasks.Task EnsureCoreWebView2Async(CoreWebView2Environment? environment)
        => throw new PlatformNotSupportedException("WebView2 has no Linux runtime; HTML elements render as placeholders.");
}

// The CoreWebView2 surface HtmlSurface reaches for; every member throws so nothing half-boots.
public class CoreWebView2
{
    public CoreWebView2Settings Settings => throw new PlatformNotSupportedException();
    public event EventHandler<CoreWebView2NavigationCompletedEventArgs>? NavigationCompleted;
    public void NavigateToString(string html) => throw new PlatformNotSupportedException();
    public System.Threading.Tasks.Task CapturePreviewAsync(object format, System.IO.Stream stream) => throw new PlatformNotSupportedException();
}
public class CoreWebView2Settings
{
    public bool AreDefaultContextMenusEnabled { get; set; }
    public bool AreDevToolsEnabled { get; set; }
    public bool IsStatusBarEnabled { get; set; }
    public bool IsZoomControlEnabled { get; set; }
}
public class CoreWebView2NavigationCompletedEventArgs : EventArgs { public bool IsSuccess => false; }
public class CoreWebView2Environment
{
    public static System.Threading.Tasks.Task<CoreWebView2Environment> CreateAsync(string? browserExecutableFolder, string? userDataFolder)
        => throw new PlatformNotSupportedException();
}

/// <summary>FindName against the control's NameScope (dirty port: XAML fields mostly cover this).</summary>
public static class FindNameExtensions
{
    public static object? FindName(this Avalonia.Controls.Control root, string name)
        => root.FindNameScope()?.Find(name);
}

// ---------- LogicalTreeHelper (WPF); dirty walk over logical then visual children ----------
public static class LogicalTreeHelper
{
    public static IEnumerable<object> GetChildren(Avalonia.AvaloniaObject root)
    {
        if (root is Panel panel) { foreach (var child in panel.Children) yield return child; }
        else if (root is ItemsControl items) { foreach (var child in items.Items) if (child is not null) yield return child; }
        else if (root is ContentControl cc && cc.Content is not null) yield return cc.Content;
        else if (root is Visual v) foreach (var child in v.GetVisualChildren()) yield return child;
    }
}

// ---------- Attached property metadata (Avalonia 11 dropped WPF's PropertyMetadata) ----------
public sealed class PropertyMetadata
{
    public object? Default { get; }
    public PropertyMetadata(object? defaultValue = null) { Default = defaultValue; }
}

// ---------- the WPF-shaped surface on real Avalonia controls (C# 14 extension members) ----------
// TECH DEBT: Style/SetResourceReference/mouse capture are inert. TODO(port): real per-instance
// styling once each window's themes are rebuilt; mouse capture via Pointer.Capture.
public static class WpfCompat3Extensions
{
    // WPF hands a clone its own gradient stops; sharing the list would let a faded or textured
    // copy repaint the brush it was cloned from.
    static GradientStops CopyStops(GradientStops stops)
    {
        var copy = new GradientStops();
        foreach (var stop in stops) copy.Add(new GradientStop(stop.Color, stop.Offset));
        return copy;
    }

    extension (Control c)
    {
        // Theme.cs walks a heterogeneous Control tree, while Avalonia puts these properties on
        // specific control bases. Unsupported control kinds intentionally read null and ignore
        // writes so the broad WPF-style theme walk remains safe.
        public IBrush? Foreground
        {
            get => c switch
            {
                Avalonia.Controls.Primitives.TemplatedControl tc => tc.Foreground,
                TextBlock text => text.Foreground,
                _ => null, // dirty: this Control kind has no foreground property
            };
            set
            {
                switch (c)
                {
                    case Avalonia.Controls.Primitives.TemplatedControl tc: tc.Foreground = value; break;
                    case TextBlock text: text.Foreground = value; break;
                    // dirty: this Control kind has no foreground property
                }
            }
        }
        public IBrush? Background
        {
            get => c switch
            {
                Avalonia.Controls.Primitives.TemplatedControl tc => tc.Background,
                Panel panel => panel.Background,
                _ => null, // dirty: this Control kind has no background property
            };
            set
            {
                switch (c)
                {
                    case Avalonia.Controls.Primitives.TemplatedControl tc: tc.Background = value; break;
                    case Panel panel: panel.Background = value; break;
                    // dirty: this Control kind has no background property
                }
            }
        }
        public IBrush? BorderBrush
        {
            get => c is Avalonia.Controls.Primitives.TemplatedControl tc ? tc.BorderBrush : null;
            set
            {
                if (c is Avalonia.Controls.Primitives.TemplatedControl tc) tc.BorderBrush = value;
                // dirty: unsupported Control kinds have no border brush property
            }
        }
        public object? ToolTip
        {
            get => Avalonia.Controls.ToolTip.GetTip(c);
            set => Avalonia.Controls.ToolTip.SetTip(c, value);
        }
        public Avalonia.StyledElement? Style
        {
            get => null;
            set { /* dirty: WPF per-instance styles ignored */ }
        }
        public bool Visible { get => c.IsVisible; set => c.IsVisible = value; }
        public global::Scoreboard.Desktop.Visibility Visibility
        {
            get => c.IsVisible ? global::Scoreboard.Desktop.Visibility.Visible : global::Scoreboard.Desktop.Visibility.Collapsed;
            set => c.IsVisible = value == global::Scoreboard.Desktop.Visibility.Visible;
        }
        public void SetResourceReference(Avalonia.AvaloniaProperty property, object key)
        {
            // WPF's SetResourceReference is a live link, not a snapshot: switching sports
            // repaints rows that were built long ago. GetResourceObservable gives the same.
            try { c.Bind(property, c.GetResourceObservable(key.ToString()!)); }
            catch { try { if (c.TryFindResource(key, out var value)) c[property] = value; } catch { } }
        }
        public bool CaptureMouse() { return c.IsPointerOver; } // dirty: reports, never captures
        public void ReleaseMouseCapture() { }
        public bool IsMouseCaptured => false;
        public bool IsMouseCaptureWithin => c.IsPointerOver;
    }

    // Background/BorderBrush/Foreground live on TemplatedControl in Avalonia, not Control; WPF
    // code themes bare Controls (ToggleButton etc.) through these.
    extension (Avalonia.Controls.Primitives.TemplatedControl tc)
    {
        public IBrush? Background { get => tc.Background; set => tc.Background = value; }
        public IBrush? BorderBrush { get => tc.BorderBrush; set => tc.BorderBrush = value; }
    }

    // Brushes are IImmutableSolidColorBrush in Avalonia 11; extensions on IBrush are visible on
    // both Brush and SolidColorBrush receivers.
    extension (IBrush b)
    {
        public void Freeze() { }
        public bool IsFrozen => true;
        public Brush Clone() => b switch
        {
            SolidColorBrush sb => new SolidColorBrush(sb.Color) { Opacity = b.Opacity },
            LinearGradientBrush lg => new LinearGradientBrush { StartPoint = lg.StartPoint, EndPoint = lg.EndPoint, GradientStops = CopyStops(lg.GradientStops), Opacity = b.Opacity },
            RadialGradientBrush rg => new RadialGradientBrush { Center = rg.Center, GradientOrigin = rg.GradientOrigin, RadiusX = rg.RadiusX, RadiusY = rg.RadiusY, GradientStops = CopyStops(rg.GradientStops), Opacity = b.Opacity },
            DrawingBrush db => new DrawingBrush { Drawing = db.Drawing, Stretch = db.Stretch, Opacity = b.Opacity },
            ImageBrush ib => new ImageBrush { Source = ib.Source, Stretch = ib.Stretch, Opacity = b.Opacity },
            _ => (b as Brush) ?? new SolidColorBrush(global::Avalonia.Media.Colors.Transparent),
        };
    }

    extension (Drawing d)
    {
        public void Freeze() { }
        public bool IsFrozen => true;
    }

    extension (Avalonia.Threading.Dispatcher d)
    {
        public void BeginInvoke(Action action) => d.Post(action);
        public void BeginInvoke(DispatcherPriority priority, Action action) => d.Post(action, priority);
    }

    extension (Window w)
    {
        public Avalonia.Threading.Dispatcher Dispatcher => Avalonia.Threading.Dispatcher.UIThread;
    }

    extension (Avalonia.Controls.TextBox tb)
    {
        public bool IsKeyboardFocused => tb.IsFocused;
    }

    extension (ComboBox cb)
    {
        public string? SelectedValuePath { get => null; set { /* dirty: Avalonia uses ToString/ItemTemplate */ } }
    }

    extension (ItemsControl ic)
    {
        public string? DisplayMemberPath { get => null; set { /* dirty: Avalonia uses ToString/ItemTemplate */ } }
    }

    extension (ScrollViewer sv)
    {
        public ScrollBarVisibility VerticalScrollBarIsVisible
        {
            get => ScrollBarVisibility.Auto;
            set => sv.VerticalScrollBarVisibility = value.ToAvalonia();
        }
        public ScrollBarVisibility HorizontalScrollBarIsVisible
        {
            get => ScrollBarVisibility.Auto;
            set => sv.HorizontalScrollBarVisibility = value.ToAvalonia();
        }
    }

    extension (PointerEventArgs e)
    {
        public MouseButtonState LeftButton => e.GetCurrentPoint(null).Properties.IsLeftButtonPressed ? MouseButtonState.Pressed : MouseButtonState.Released;
    }

    extension (DragEventArgs e)
    {
        public DragDropEffects Effects { get => e.DragEffects; set => e.DragEffects = value; }
    }

    extension (Avalonia.Input.IDataObject data)
    {
        public bool GetDataPresent(string format) => data.Contains(format);
        public object? GetData(string format) => data.Get(format);
        public bool GetDataPresent(Type type) => data.Contains(type.FullName ?? "");
        public object? GetData(Type type) => data.Get(type.FullName ?? "");
    }

    extension (Avalonia.Controls.Shapes.Line line)
    {
        public double X1 { get => line.StartPoint.X; set => line.StartPoint = new Point(value, line.StartPoint.Y); }
        public double Y1 { get => line.StartPoint.Y; set => line.StartPoint = new Point(line.StartPoint.X, value); }
        public double X2 { get => line.EndPoint.X; set => line.EndPoint = new Point(value, line.EndPoint.Y); }
        public double Y2 { get => line.EndPoint.Y; set => line.EndPoint = new Point(line.EndPoint.X, value); }
    }

    extension (Avalonia.Controls.Shapes.Shape shape)
    {
        public PenLineCap StrokeStartLineCap { get => shape.StrokeLineCap; set => shape.StrokeLineCap = value; }
        public PenLineCap StrokeEndLineCap { get => shape.StrokeLineCap; set => shape.StrokeLineCap = value; }
    }

    extension (FontFamily f)
    {
        public string Source => f.Name;
    }

    extension (Avalonia.AvaloniaObject ao)
    {
        // WPF code walks children off AvaloniaObject roots; Visuals delegate, Controls unwrap.
        public System.Collections.Generic.IEnumerable<Visual> GetVisualChildren() =>
            ao switch
            {
                Panel panel => panel.Children.OfType<Visual>(),
                ItemsControl items => items.Items.OfType<Visual>(),
                ContentControl { Content: Visual content } => [content],
                Visual v => Avalonia.VisualTree.VisualExtensions.GetVisualChildren(v),
                _ => Array.Empty<Visual>(),
            };
    }
}

