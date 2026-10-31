using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Scoreboard.Core;

namespace Scoreboard.Desktop;

/// <summary>
/// An HTML element on the graphic. The markup is drawn by a real browser engine and captured to an
/// image, so the studio, both previews and the audience output all show the identical picture.
/// A live web view cannot be used directly: it is a native window that WPF cannot scale inside the
/// viewbox every feed draws through, and it would float above the rest of the artwork.
/// </summary>
public sealed class HtmlSurface : Grid
{
    readonly Image _image = new() { Stretch = Stretch.Fill };
    readonly Border _placeholder;
    readonly DispatcherTimer _refresh = new() { Interval = TimeSpan.FromMilliseconds(160) };
    HtmlEngine? _engine;
    ScorebugElement _item;
    string _renderedHtml = "";
    int _renderedWidth, _renderedHeight;
    bool _busy;
    bool _disposed;
    int _attempts;

    public HtmlSurface(ScorebugElement item, bool showPlaceholder)
    {
        _item = item;
        _placeholder = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x33, 0x38, 0xD7, 0xA3)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0xAA, 0x38, 0xD7, 0xA3)),
            BorderThickness = new Thickness(1),
            Visibility = Visibility.Collapsed,
            Child = new TextBlock
            {
                Text = "HTML",
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromArgb(0xAA, 0xF4, 0xF7, 0xFB)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        if (!showPlaceholder) _placeholder.Background = Brushes.Transparent;
        Children.Add(_image);
        Children.Add(_placeholder);
        IsHitTestVisible = false;
        _refresh.Tick += (_, _) => Pump();
        Render();
    }

    /// <summary>Follows the element's markup and box size, without rebuilding the surface.</summary>
    public void Update(ScorebugElement item)
    {
        _item = item;
        Render();
    }

    /// <summary>Forces a fresh frame, used when the graphic is rebuilt.</summary>
    public void Refresh() => Render();

    void Render()
    {
        if (_disposed) return;
        var html = _item.Content ?? "";
        var width = Math.Max(16, (int)Math.Round(_item.Width));
        var height = Math.Max(16, (int)Math.Round(_item.Height));
        if (string.IsNullOrWhiteSpace(html))
        {
            _renderedHtml = html;
            _image.Source = null;
            _placeholder.Visibility = Visibility.Collapsed;
            _refresh.Stop();
            return;
        }
        var changed = html != _renderedHtml || width != _renderedWidth || height != _renderedHeight;
        if (!changed && _image.Source is not null) return;
        _renderedHtml = html;
        _renderedWidth = width;
        _renderedHeight = height;
        // Markup that animates has to keep being sampled; a still page only needs one capture.
        // A page that has not arrived yet is retried a few times — the first capture can race the
        // browser starting up, and a blank element on air would look broken.
        _attempts = 0;
        if (IsAnimated(html) || _image.Source is null) _refresh.Start(); else _refresh.Stop();
        Pump();
    }

    /// <summary>Captures a frame, unless the last one is still being taken.</summary>
    async void Pump()
    {
        if (_busy || _disposed) return;
        _busy = true;
        try
        {
            _engine ??= new HtmlEngine();
            var frame = await _engine.RenderAsync(_renderedHtml, _renderedWidth, _renderedHeight);
            if (frame is null)
            {
                // Nothing to show rather than a wrong picture: the studio draws a small HTML badge,
                // the previews and the audience stay clear. Keep trying for a moment, then settle.
                _attempts++;
                if (_attempts > 12) _refresh.Stop();
                _placeholder.Visibility = _image.Source is null ? Visibility.Visible : Visibility.Collapsed;
                return;
            }
            _image.Source = frame;
            _attempts = 0;
            if (!IsAnimated(_renderedHtml)) _refresh.Stop();
            _placeholder.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            HtmlEngine.Note(ex);
            _attempts++;
            if (_attempts > 12) _refresh.Stop();
            _placeholder.Visibility = _image.Source is null ? Visibility.Visible : Visibility.Collapsed;
        }
        finally { _busy = false; }
    }

    static bool IsAnimated(string html)
    {
        foreach (var marker in new[] { "@keyframes", "animation", "<video", "<canvas", "<marquee", "requestAnimationFrame" })
            if (html.Contains(marker, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    public void Dispose()
    {
        _disposed = true;
        _refresh.Stop();
        _engine?.Dispose();
        _engine = null;
    }
}

/// <summary>
/// One hidden browser used to draw an HTML element. It is kept alive so a page can animate between
/// captures; only a change of markup re-navigates it, which would otherwise restart the animation
/// from the top on every frame. Everything is guarded: a machine without the WebView2 runtime gets
/// null frames and a placeholder, never a crash.
/// </summary>
public sealed class HtmlEngine : IDisposable
{
    static readonly SemaphoreSlim Gate = new(1, 1);
    Window? _host;
    WebView2? _view;
    string _loaded = "";
    bool _disposed;

    /// <summary>The last failure, so a blank HTML element can be explained instead of guessed at.</summary>
    public static string? LastError { get; private set; }

    /// <summary>Records a browser failure in the diagnostics folder; never throws.</summary>
    public static void Note(Exception ex)
    {
        LastError = ex.Message;
        try
        {
            var folder = AppPaths.Combine("logs");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "html-last-error.txt"), $"{DateTimeOffset.Now:O} {ex}");
        }
        catch { }
    }

    string Wrap(string html) => html.Contains("<html", StringComparison.OrdinalIgnoreCase)
        ? html
        : "<!doctype html><html><head><meta charset=\"utf-8\"><style>html,body{margin:0;padding:0;overflow:hidden;width:100%;height:100%;background:transparent;font-family:Segoe UI,Arial,sans-serif;color:#fff}</style></head><body>" + html + "</body></html>";

    public async Task<BitmapSource?> RenderAsync(string html, int width, int height)
    {
        if (_disposed) return null;
        await Gate.WaitAsync();
        try
        {
            var view = await EnsureViewAsync(width, height);
            if (view is null) return null;
            var core = view.CoreWebView2;
            if (core is null) return null;
            if (!string.Equals(_loaded, html, StringComparison.Ordinal) || string.IsNullOrEmpty(_loaded))
            {
                _loaded = html;
                var loaded = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                void OnNavigation(object? sender, CoreWebView2NavigationCompletedEventArgs e)
                {
                    core.NavigationCompleted -= OnNavigation;
                    loaded.TrySetResult(e.IsSuccess);
                }
                core.NavigationCompleted += OnNavigation;
                core.NavigateToString(Wrap(html));
                await Task.WhenAny(loaded.Task, Task.Delay(2500));
            }
            using var stream = new MemoryStream();
            await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, stream);
            if (stream.Length == 0) return null;
            stream.Position = 0;
            var frame = new BitmapImage();
            frame.BeginInit();
            frame.CacheOption = BitmapCacheOption.OnLoad;
            frame.StreamSource = stream;
            frame.EndInit();
            frame.Freeze();
            return frame;
        }
        catch (Exception ex)
        {
            Note(ex);
            return null;
        }
        finally { Gate.Release(); }
    }

    async Task<WebView2?> EnsureViewAsync(int width, int height)
    {
        try
        {
            if (_view is not null)
            {
                // The browser is laid out at the element's design size, exactly as the artwork was
                // designed, and the viewbox scales the captured frame like every other graphic.
                if (Math.Abs(_host!.Width - width) > 0.5 || Math.Abs(_host.Height - height) > 0.5)
                {
                    _host.Width = width;
                    _host.Height = height;
                    _view.Width = width;
                    _view.Height = height;
                }
                return _view;
            }
            _view = new WebView2 { Width = width, Height = height, DefaultBackgroundColor = System.Drawing.Color.Transparent };
            _host = new Window
            {
                Title = "ScorePlus HTML",
                Width = width,
                Height = height,
                Left = -32000,
                Top = -32000,
                ShowInTaskbar = false,
                ShowActivated = false,
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                Content = _view
            };
            _host.Show();
            var userData = Path.Combine(AppPaths.Root, "webview");
            Directory.CreateDirectory(userData);
            // Awaited, never blocked: the environment finishes on this thread's dispatcher, so
            // waiting for it synchronously would deadlock the window that asked for the frame.
            var environment = await CoreWebView2Environment.CreateAsync(null, userData);
            await _view.EnsureCoreWebView2Async(environment);
            var core = _view.CoreWebView2;
            if (core is null) return null;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsZoomControlEnabled = false;
            return _view;
        }
        catch (Exception ex)
        {
            Note(ex);
            Dispose();
            return null;
        }
    }

    void DisposeView()
    {
        try { _host?.Close(); } catch { }
        _host = null;
        _view = null;
    }

    public void Dispose()
    {
        _disposed = true;
        DisposeView();
    }
}
