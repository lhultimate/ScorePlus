using System.Diagnostics;
using System.IO;
using System.Windows;

namespace ScorePlus.Setup;

public static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        var uninstall = args.Any(a => string.Equals(a, "/uninstall", StringComparison.OrdinalIgnoreCase));
        var silent = args.Any(a => string.Equals(a, "/silent", StringComparison.OrdinalIgnoreCase));
        var fromTemp = args.Any(a => string.Equals(a, "/fromtemp", StringComparison.OrdinalIgnoreCase));

        // The uninstaller cannot delete the file it is running from, so hand the job to a copy in
        // the temp folder and let this one exit.
        if (uninstall && !fromTemp)
        {
            try
            {
                var copy = Path.Combine(Path.GetTempPath(), "ScorePlusUninstall.exe");
                File.Copy(Environment.ProcessPath!, copy, true);
                // A scripted uninstall stays silent; an interactive one gets the confirmation window.
                var handoff = silent ? "/uninstall /fromtemp /SILENT" : "/uninstall /fromtemp";
                Process.Start(new ProcessStartInfo(copy, handoff) { UseShellExecute = true });
                return 0;
            }
            catch { return 1; }
        }

        if (silent)
        {
            // Optional switches make the installer testable and usable in a folder: /DIR=<path>
            // chooses where it goes, /NOICONS skips shortcuts, /NOREG keeps the registry untouched.
            var explicitDirectory = args.FirstOrDefault(a => a.StartsWith("/DIR=", StringComparison.OrdinalIgnoreCase))?["/DIR=".Length..].Trim('"');
            // An update reuses the folder it was installed into — but only if that folder is a sane
            // home for a program. A copy installed into Downloads or the Desktop is moved to the
            // proper per-user folder rather than being written over again.
            var existing = Installer.InstalledDirectory();
            if (existing is not null && !Installer.IsSafeInstallDirectory(existing, out _)) existing = null;
            var directory = explicitDirectory ?? existing ?? Installer.DefaultDirectory;
            var shortcuts = !args.Any(a => string.Equals(a, "/NOICONS", StringComparison.OrdinalIgnoreCase));
            var register = !args.Any(a => string.Equals(a, "/NOREG", StringComparison.OrdinalIgnoreCase));
            var result = uninstall
                ? Installer.Uninstall()
                : Installer.Install(directory, desktopShortcut: false, progress: null, shortcuts: shortcuts, register: register);
            return result.Ok ? 0 : 1;
        }

        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        app.Run(new SetupWindow(uninstall));
        return 0;
    }

}
