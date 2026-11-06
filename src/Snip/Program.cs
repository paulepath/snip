using Avalonia;
using Snip.Update;
using Velopack;

namespace Snip;

static class Program
{
    /// <summary>
    /// Snip.exe                 starts Snip in the tray (as at sign-in); Print Screen is then Snip's
    /// Snip.exe &lt;image&gt;         an editor on that picture
    /// Snip.exe --capture       as Print Screen
    /// Snip.exe --show          an empty editor, to open or paste a picture into
    /// Snip.exe --quit          ends a running Snip (the installer uses it); exit code 2 if a picture has unsaved marks
    /// One Snip per user: a later start hands its request to the running one (SnipPipe) and exits.
    /// </summary>
    [STAThread]
    static int Main(string[] args)
    {
        // Must run first: Velopack's install, update and uninstall hooks run here and may end the process.
        VelopackApp.Build()
            .OnAfterInstallFastCallback(_ => WindowsSetup.Installed())
            .OnBeforeUninstallFastCallback(_ => WindowsSetup.Uninstalling())
            .Run();

        var line = SnipPipe.LineFor(args);
        var answer = SnipPipe.TrySend(line ?? "running?");
        if (answer != null)
            return answer == SnipPipe.Busy ? 2 : 0;
        if (line == SnipPipe.Quit)
            return 0;   // nothing to quit

        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Error("Unhandled exception", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) => { Log.Error("Unobserved task exception", e.Exception); e.SetObserved(); };
        try
        {
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            Log.Error("Fatal", ex);
            return 1;
        }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
