using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Scoreboard.Core;
using Scoreboard.Desktop;

// Checks the one thing the in-process smoke harness cannot: that an HTML element really draws a
// frame. WebView2 refuses to start unless the application's own event loop is running, which the
// WPF smoke suite replaces with pushed frames, so this runs a real Application.Run of its own.
static class Program
{
    [STAThread]
    static int Main()
    {
        var exitCode = 1;
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Startup += (_, _) =>
        {
            var element = new ScorebugElement
            {
                Kind = ScorebugElementKind.Html,
                Width = 320,
                Height = 120,
                TransparentBackground = true,
                Content = "<div style=\"font:700 40px Segoe UI,Arial;color:#38D7A3\">HTML OK</div>"
            };
            var surface = new HtmlSurface(element, showPlaceholder: true);
            var host = new Window { Width = 360, Height = 200, Content = surface };
            host.Show();
            host.UpdateLayout();
            ImageSource? frame = null;
            var deadline = DateTime.UtcNow.AddSeconds(25);
            while (frame is null && DateTime.UtcNow < deadline)
            {
                Pump(TimeSpan.FromMilliseconds(200));
                frame = surface.Children.OfType<Image>().Select(image => image.Source).FirstOrDefault(source => source is not null);
            }
            if (frame is null)
            {
                Console.WriteLine($"FAIL: an HTML element never drew a frame ({HtmlEngine.LastError ?? "no error recorded"})");
            }
            else
            {
                Console.WriteLine("PASS: embedded HTML is drawn by a browser engine and shown on the graphic");
                exitCode = 0;
            }
            host.Close();
            surface.Dispose();
            app.Shutdown();
        };
        app.Run();
        return exitCode;
    }

    static void Pump(TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = duration };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }
}
