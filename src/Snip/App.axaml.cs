using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Threading;
using Snip.Capture;
using Snip.PiDash;

namespace Snip;

/// <summary>
/// Snip runs in the tray from sign-in. Print Screen (<see cref="Hotkey"/>) captures the screen, or the Pi Dash's
/// screen while the PC's mouse is on it; each shot opens in its own editor window. Later starts hand their request
/// over <see cref="SnipPipe"/>. Closing every window leaves Snip running; Exit in the tray menu ends it.
/// </summary>
public partial class App : Application
{
    SnipSettings _settings = new();
    IClassicDesktopStyleApplicationLifetime? _desktop;
    Tray? _tray;
    Hotkey? _hotkey;
    CaptureService? _capture;
    DashLink? _dashLink;
    DashScreenshot? _dashShots;
    SnipPipe? _pipe;
    SynchronizationContext? _ui;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        Palette.Apply(ActualThemeVariant);
        ActualThemeVariantChanged += (_, _) => Palette.Apply(ActualThemeVariant);
    }

    /// <summary>Applies a theme choice: "Dark", "Light" or "System" (follow Windows).</summary>
    public static void SetTheme(string? choice)
    {
        if (Current is not { } app) return;
        app.RequestedThemeVariant = choice switch
        {
            "Dark" => ThemeVariant.Dark,
            "Light" => ThemeVariant.Light,
            _ => ThemeVariant.Default,
        };
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _desktop = desktop;
            _ui = SynchronizationContext.Current ?? new AvaloniaSynchronizationContext();
            Log.Info($"Snip {typeof(App).Assembly.GetName().Version} starting (pid {Environment.ProcessId})");
            _settings = SnipSettings.Load();
            SetTheme(_settings.Theme);
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            _tray = new Tray(new Tray.Actions(
                Capture: () => _ = _capture!.CaptureAsync(),
                CaptureScreen: () => _ = _capture!.CaptureAsync(wholeMonitor: true),
                Open: () => _ = OpenFileAsync(),
                Paste: Paste,
                ShotsFolder: ShowShotsFolder,
                UseDash: on => { _settings.PiDash = on; _settings.Save(); SetUpDash(); },
                Exit: () => Exit()), _settings.PiDash);
            _capture = new CaptureService(_settings, OpenImage, Notify);
            _hotkey = new Hotkey(PrintScreen);
            SetUpDash();
            DispatcherTimer.Run(() => { ShowStatus(); return true; }, TimeSpan.FromSeconds(2));
            ShowStatus();

            _pipe = SnipPipe.Listen(line => Dispatcher.UIThread.Invoke(() => Handle(line)));
            desktop.Exit += (_, _) =>
            {
                _pipe?.Dispose();
                _hotkey?.Dispose();
                _tray?.Dispose();
                _dashLink?.StopAsync().Wait(TimeSpan.FromSeconds(3));
                Log.Info("Snip closed");
            };
            if (SnipPipe.LineFor(desktop.Args ?? []) is { } line)
                Dispatcher.UIThread.Post(() => Handle(line));
        }
        base.OnFrameworkInitializationCompleted();
    }

    // ------------------------------------------------------------------ Print Screen

    void PrintScreen()
    {
        if (_dashShots != null && DashLink.MouseOnDash())
            _ = _dashShots.RequestAsync();   // the mouse is on the Pi Dash: a shot of the dash
        else
            _ = _capture!.CaptureAsync();
    }

    /// <summary>The Pi Dash link, when Pi Dash is installed here and it's switched on; otherwise none.</summary>
    void SetUpDash()
    {
        var mqtt = _settings.PiDash ? DashMqtt.Read() : null;
        if (mqtt == null)
        {
            if (_dashLink != null)
            {
                _ = _dashLink.StopAsync();
                _dashLink = null;
                _dashShots = null;
                Log.Info("Pi Dash: off");
            }
            return;
        }
        if (_dashLink != null) return;
        _dashLink = new DashLink(mqtt);
        var shots = new DashScreenshot(_dashLink.PublishAsync, _ui!, DashScreenshot.DefaultFolder,
            (path, png) =>
            {
                try { ImageIo.Copy(png); }
                catch (System.Runtime.InteropServices.ExternalException ex) { Log.Warn($"Copying the dash's shot failed: {ex.Message}"); }
                OpenPath(path);
            },
            Notify);
        _dashShots = shots;
        _dashLink.ScreenshotReceived = (id, png) => shots.Received(id, png);
        _dashLink.Start();
    }

    void ShowStatus()
    {
        var print = _hotkey?.Registered == true ? "Print Screen: Snip's" : "Print Screen: taken by Windows or another app";
        var dash = !_settings.PiDash ? "Pi Dash: off"
            : _dashLink == null ? "Pi Dash: not installed here"
            : _dashLink.IsConnected ? $"Pi Dash: connected to {_dashLink.Host}"
            : $"Pi Dash: connecting to {_dashLink.Host}…";
        _tray?.ShowStatus(print, dash);
    }

    void Notify(string text) => _tray?.Notify(text);

    // ------------------------------------------------------------------ windows

    IEnumerable<SnipWindow> Windows => _desktop?.Windows.OfType<SnipWindow>() ?? [];

    /// <summary>What the pipe asks for. Returns the answer for the sender.</summary>
    string Handle(string line)
    {
        switch (line)
        {
            case SnipPipe.Capture:
                _ = _capture!.CaptureAsync();
                return SnipPipe.Ok;
            case SnipPipe.Show:
                (Windows.LastOrDefault() ?? NewWindow()).BringUp();
                return SnipPipe.Ok;
            case SnipPipe.Quit:
                return Exit() ? SnipPipe.Ok : SnipPipe.Busy;
        }
        if (SnipPipe.PathOf(line) is { } path)
            OpenPath(path);
        return SnipPipe.Ok;   // anything else (a plain start while Snip runs) just finds it running
    }

    SnipWindow NewWindow()
    {
        var window = new SnipWindow(_settings);
        window.Show();
        return window;
    }

    /// <summary>An image file in an editor (the one already showing it, if there is one).</summary>
    void OpenPath(string path)
    {
        var window = Windows.FirstOrDefault(w => string.Equals(w.FilePath, path, StringComparison.OrdinalIgnoreCase));
        if (window == null)
        {
            window = new SnipWindow(_settings);
            if (!window.Load(path))
            {
                Notify($"Couldn't open {Path.GetFileName(path)}.");
                return;
            }
            window.Show();
        }
        window.BringUp();
    }

    void OpenImage(PixelBuffer pixels, string? path)
    {
        var window = new SnipWindow(_settings);
        window.SetImage(pixels, path);
        window.Show();
        window.BringUp();
    }

    async Task OpenFileAsync()
    {
        var window = NewWindow();
        await window.OpenAsync();
    }

    void Paste()
    {
        if (ImageIo.Paste() is { } pixels)
            OpenImage(pixels, null);
        else
            Notify("There's no picture on the clipboard.");
    }

    void ShowShotsFolder()
    {
        try
        {
            Directory.CreateDirectory(_settings.Shots);
            Process.Start(new ProcessStartInfo(_settings.Shots) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            Log.Warn($"Couldn't open {_settings.Shots}: {ex.Message}");
        }
    }

    /// <summary>Ends Snip, unless a window has marks not yet copied or saved: that one comes forward instead.</summary>
    bool Exit()
    {
        if (Windows.FirstOrDefault(w => w.HasUnsentMarks) is { } busy)
        {
            busy.BringUp();
            Notify("A picture has marks you haven't copied or saved. Close it first.");
            return false;
        }
        foreach (var w in Windows.ToList())
            w.Close();
        _desktop?.Shutdown();
        return true;
    }
}
