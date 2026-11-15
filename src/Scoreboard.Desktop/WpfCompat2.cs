// ScorePlus Linux port — dirty WPF compatibility shims, layer 2 (members, helpers, dialogs).
// Everything here is inert glue so the ported code compiles on Avalonia; TODO(port) marks each
// site that needs a real implementation in a later phase.
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.VisualTree;

namespace Scoreboard.Desktop
{

// ---------- Keyboard / focus (WPF's System.Windows.Input.Keyboard) ----------

public static class Keyboard
{
    static Avalonia.Controls.Window? Main => (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
    public static object? FocusedElement => Main is { } w ? w.FocusManager.GetFocusedElement() : null; // dirty: scoped to main window
    public static void ClearFocus() { if (Main is { } w) w.FocusManager.ClearFocus(); }
}

// ---------- VisualTreeHelper (WPF static helper; Avalonia uses VisualTree extensions) ----------

public readonly struct DpiInformation
{
    public double DpiScaleX { get; }
    public double DpiScaleY { get; }
    public DpiInformation(double x, double y) { DpiScaleX = x; DpiScaleY = y; }
}

public static class VisualTreeHelper
{
    public static int GetChildrenCount(Visual visual) => visual.GetVisualChildren().Count();
    public static Visual GetChild(Visual visual, int index) => visual.GetVisualChildren().ElementAt(index);
    public static Visual? GetParent(Visual visual) => visual.GetVisualParent();
    public static Avalonia.AvaloniaObject? GetParent(Avalonia.AvaloniaObject obj) =>
        obj is Visual v ? v.GetVisualParent() : null;
    // Dirty: ported walks pass AvaloniaObject roots (Theme/panels); unwrap controls, then visuals.
    public static int GetChildrenCount(Avalonia.AvaloniaObject root) => ChildrenOf(root).Count;
    public static Avalonia.AvaloniaObject GetChild(Avalonia.AvaloniaObject root, int index) => ChildrenOf(root)[index];
    static System.Collections.Generic.List<Avalonia.AvaloniaObject> ChildrenOf(Avalonia.AvaloniaObject root)
    {
        var list = new System.Collections.Generic.List<Avalonia.AvaloniaObject>();
        IEnumerable<object?> children = root switch
        {
            Panel panel => panel.Children.Cast<object?>(),
            ItemsControl items => items.Items.Cast<object?>(),
            ContentControl { Content: not null } content => [content.Content],
            Visual visual => visual.GetVisualChildren().Cast<object?>(),
            _ => [],
        };
        list.AddRange(children.OfType<Avalonia.AvaloniaObject>());
        return list;
    }
    // Dirty: WPF returns device DPI (96 baseline). Avalonia reports scaling; 96 keeps math stable.
    public static DpiInformation GetDpi(Visual visual) => new(96, 96);
}

// ---------- Cursors ----------

public static class Cursors
{
    public static readonly Cursor Hand = new(StandardCursorType.Hand);
    public static readonly Cursor IBeam = new(StandardCursorType.Ibeam);
    public static readonly Cursor SizeAll = new(StandardCursorType.SizeAll);
    public static readonly Cursor SizeWE = new(StandardCursorType.SizeWestEast);
    public static readonly Cursor SizeNWSE = new(StandardCursorType.BottomRightCorner);
    public static readonly Cursor Arrow = new(StandardCursorType.Arrow);
}

// ---------- ColorConverter ----------

public static class ColorConverter
{
    public static object ConvertFromString(string value) => Color.Parse(value);
}

// ---------- PointCollection / Int32Rect (WPF geometry helpers) ----------

public sealed class PointCollection : List<Point>
{
}

public readonly struct Int32Rect
{
    public int X { get; }
    public int Y { get; }
    public int Width { get; }
    public int Height { get; }
    public Int32Rect(int x, int y, int width, int height) { X = x; Y = y; Width = width; Height = height; }
}

// ---------- Style / Setter (thin shim over Avalonia.Styling.Style) ----------

public sealed class Style
{
    public List<Setter> Setters { get; } = [];
    public string? Selector { get; set; }
}

public sealed class Setter
{
    public AvaloniaProperty? Property { get; set; }
    public object? Value { get; set; }
    public Setter() { }
    public Setter(AvaloniaProperty property, object value) { Property = property; Value = value; }
}

// ---------- WPF clipboard payload ----------

public interface IDataObject
{
    bool GetDataPresent(string format);
    object? GetData(string format);
}

internal sealed class ClipboardData : IDataObject
{
    readonly string? _text;
    public ClipboardData(string? text) { _text = text; }
    public bool GetDataPresent(string format) => format == "UnicodeText" || format == "Text";
    public object? GetData(string format) => GetDataPresent(format) ? _text : null;
}

// ---------- mouse/drag event-name adapters (used as extensions on +=/-= sites) ----------
// TECH DEBT: Avalonia has no *LeftButtonDown events; these extensions only compile if call
// sites use method syntax. Most sites get codemods to Pointer* instead; these cover stragglers.

public static class WpfMouseEvents
{
    public static void GotKeyboardFocus(this InputElement target, EventHandler<GotFocusEventArgs> handler)
        => target.AddHandler(InputElement.GotFocusEvent, handler, RoutingStrategies.Bubble);
    public static void LostKeyboardFocus(this InputElement target, EventHandler<RoutedEventArgs> handler)
        => target.AddHandler(InputElement.LostFocusEvent, handler, RoutingStrategies.Bubble);
    public static void DragEnter(this InputElement target, EventHandler<DragEventArgs> handler)
        => DragDrop.AddDragEnterHandler(target, handler);
    public static void DragOver(this InputElement target, EventHandler<DragEventArgs> handler)
        => DragDrop.AddDragOverHandler(target, handler);
    public static void DragLeave(this InputElement target, EventHandler<DragEventArgs> handler)
        => DragDrop.AddDragLeaveHandler(target, handler);
    public static void Drop(this InputElement target, EventHandler<DragEventArgs> handler)
        => DragDrop.AddDropHandler(target, handler);

    public static void MouseLeftButtonDown(this InputElement target, EventHandler<PointerPressedEventArgs> handler)
        => target.AddHandler(InputElement.PointerPressedEvent, handler, RoutingStrategies.Direct);
    public static void PreviewMouseLeftButtonDown(this InputElement target, EventHandler<PointerPressedEventArgs> handler)
        => target.AddHandler(InputElement.PointerPressedEvent, handler, RoutingStrategies.Tunnel);
    public static void MouseMove(this InputElement target, EventHandler<PointerEventArgs> handler)
        => target.AddHandler(InputElement.PointerMovedEvent, handler, RoutingStrategies.Direct);
    public static void PreviewMouseMove(this InputElement target, EventHandler<PointerEventArgs> handler)
        => target.AddHandler(InputElement.PointerMovedEvent, handler, RoutingStrategies.Tunnel);
    public static void MouseLeftButtonUp(this InputElement target, EventHandler<PointerReleasedEventArgs> handler)
        => target.AddHandler(InputElement.PointerReleasedEvent, handler, RoutingStrategies.Direct);
    public static void PreviewMouseLeftButtonUp(this InputElement target, EventHandler<PointerReleasedEventArgs> handler)
        => target.AddHandler(InputElement.PointerReleasedEvent, handler, RoutingStrategies.Tunnel);
    public static void MouseRightButtonUp(this InputElement target, EventHandler<PointerReleasedEventArgs> handler)
        => target.AddHandler(InputElement.PointerReleasedEvent, (sender, e) =>
        {
            if (e.InitialPressMouseButton == MouseButton.Right) handler(sender, e);
        }, RoutingStrategies.Direct);
    public static void LostMouseCapture(this InputElement target, EventHandler<PointerCaptureLostEventArgs> handler)
        => target.AddHandler(InputElement.PointerCaptureLostEvent, handler, RoutingStrategies.Direct);
    public static void RightTappedExt(this InputElement target, EventHandler<PointerPressedEventArgs> handler)
        => target.AddHandler(InputElement.PointerPressedEvent, handler, RoutingStrategies.Direct);
}

// ---------- modal dialogs: WPF-shaped ShowDialog over a nested dispatcher frame ----------

public static class WpfDialogExtensions
{
    /// <summary>
    /// WPF's blocking bool ShowDialog(): shows the window transient for its owner and runs a nested
    /// dispatcher frame until the window closes, then hands back the DialogResult the dialog set
    /// (null — the user closed the window or pressed Escape — reads as cancel, exactly as in WPF).
    /// <para>
    /// The frame matters. `Dispatcher.UIThread.PushFrame` is Avalonia's equivalent of the nested
    /// message pump WPF runs inside ShowDialog: input, layout, rendering AND timers keep running
    /// while the dialog is up. `RunJobs()` plus `Thread.Sleep` in a `while (!closed)` loop only
    /// drains work that is already queued, and Avalonia's timers are not queued work — they are
    /// scheduled by the platform loop — so every DispatcherTimer in the process stops firing for as
    /// long as a dialog is open. That was reproduced directly: the SCOREPLUS_WINDOW_DEBUG audit
    /// timers never ticked while the launch box was open, and ticked on time with the box skipped.
    /// </para>
    /// <para>
    /// The result channel is the compat shim's own `DialogResult`, not Avalonia's: this Avalonia
    /// version has no `CloseFinished` event, so the reflection that used to look for it found
    /// nothing and the old version of this method returned false no matter what the dialog did —
    /// which silently disabled all 49 `if (dialog.ShowDialog() == true)` call sites in the port.
    /// </para>
    /// <para>
    /// TODO(port): still modeless at the window-manager level. The dialog stays above its owner
    /// (transient-for) but the owner is not input-disabled the way WPF disables it; switching these
    /// call sites to Avalonia's async ShowDialog&lt;T&gt; is what gets true modality.
    /// </para>
    /// </summary>
    public static bool ShowDialogPumped(this Window window, Window? owner = null)
    {
        var frame = new Avalonia.Threading.DispatcherFrame(true);
        window.Closed += OnClosed;
        try
        {
            if (owner is not null) window.Show(owner);
            else window.Show();
            Avalonia.Threading.Dispatcher.UIThread.PushFrame(frame);
        }
        finally
        {
            window.Closed -= OnClosed;
        }
        // WPF call sites set `DialogResult = true/false` on the compat Window; its setter also
        // closes the window, so by the time the frame has exited the value is already final.
        return window.DialogResult ?? false;

        void OnClosed(object? sender, EventArgs e) => frame.Continue = false;
    }
}

} // namespace Scoreboard.Desktop

// ---------- Microsoft.Win32 file dialogs (unavailable on Linux; WPF-shaped stubs) ----------
// TECH DEBT: ShowDialog always returns null (cancel). TODO(port): zenity/portals dialog layer.

namespace Microsoft.Win32
{
    public enum FileDialogCharacterCasing { Default, Upper, Lower }

    public abstract class FileDialogStub
    {
        public string Title { get; set; } = "";
        public string Filter { get; set; } = "";
        public int FilterIndex { get; set; } = 1;
        public string InitialDirectory { get; set; } = "";
        public string DefaultExt { get; set; } = "";
        public bool AddExtension { get; set; } = true;
        public bool CheckFileExists { get; set; } = true;
        public bool CheckPathExists { get; set; } = true;
        public bool ValidateNames { get; set; } = true;
        public bool OverwritePrompt { get; set; } = true;
        public bool RestoreDirectory { get; set; }
        public FileDialogCharacterCasing CharacterCasing { get; set; }
        public string FileName { get; set; } = "";
        public string[] FileNames { get; set; } = [];
        public bool? ShowDialog() => null; // dirty: always cancel on Linux
        public bool? ShowDialog(object? owner) => null;
    }

    public sealed class OpenFileDialog : FileDialogStub
    {
        public bool Multiselect { get; set; }
        public bool DereferenceLinks { get; set; } = true;
    }

    public sealed class SaveFileDialog : FileDialogStub
    {
        public bool CreatePrompt { get; set; }
    }

    public sealed class FolderBrowserDialog
    {
        public string Description { get; set; } = "";
        public string SelectedPath { get; set; } = "";
        public bool ShowNewFolderButton { get; set; }
        public bool? ShowDialog() => null;
        public bool? ShowDialog(object? owner) => null;
    }
}
