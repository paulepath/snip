namespace Snip.Capture;

/// <summary>
/// Print Screen on Windows: freeze the screen, let the user choose (<see cref="CaptureOverlay"/>), then save the part
/// chosen to the shots folder, put it on the clipboard and open it in the editor: the same ending a dash shot has.
/// </summary>
sealed class CaptureService(SnipSettings settings, Action<PixelBuffer, string> open, Action<string> notify)
{
    bool _busy;

    /// <summary>Choosing or saving a shot right now.</summary>
    public bool Busy => _busy;

    /// <param name="wholeMonitor">Skip choosing: the monitor under the pointer.</param>
    public async Task CaptureAsync(bool wholeMonitor = false)
    {
        if (_busy) return;   // Print Screen pressed again while choosing
        _busy = true;
        try
        {
            var screen = ScreenGrab.Take();
            ScreenGrab.Describe(screen);
            PixRect? chosen;
            if (wholeMonitor)
            {
                var (x, y) = ScreenGrab.Pointer();
                chosen = CaptureMath.MonitorAt(screen.Monitors, x, y) ?? screen.Desktop;
            }
            else
                chosen = await CaptureOverlay.ChooseAsync(screen);
            if (chosen is not { } region)
                return;

            var pixels = CaptureMath.Cut(screen.Pixels, screen.Desktop, region);
            var png = ImageIo.ToPng(pixels);
            string path;
            try
            {
                Directory.CreateDirectory(settings.Shots);
                path = CaptureMath.FreePath(settings.Shots, "snip", DateTimeOffset.Now);
                await File.WriteAllBytesAsync(path, png);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Error("Saving the shot failed", ex);
                notify($"The shot couldn't be saved to {settings.Shots}.");
                return;
            }
            try { ImageIo.Copy(png); }
            catch (System.Runtime.InteropServices.ExternalException ex) { Log.Warn($"Copying the shot failed: {ex.Message}"); }
            Log.Info($"Shot {region.W}x{region.H} at {region.X},{region.Y}: {path}");
            open(pixels, path);
        }
        catch (Exception ex)
        {
            Log.Error("Capture failed", ex);
            notify("The screen couldn't be captured.");
        }
        finally
        {
            _busy = false;
        }
    }
}
