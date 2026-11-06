using System.Collections.Concurrent;
using System.Text.Json;

namespace Snip.PiDash;

/// <summary>
/// Print Screen while the PC's mouse is on the Pi Dash: asks the dash for a shot of its screen (pidash/desktop/screenshot
/// {"id"}, through <see cref="DashLink"/>), and the PNG comes back on pidash/ui/screenshot/&lt;id&gt; (pi-dash's
/// server/screenshot.py; docs/pidash.md). It is saved to Pictures\Pi Dash and handed to <c>onShot</c> on the UI thread
/// (the clipboard, then the editor). A shot that doesn't come within <see cref="Timeout"/> gets one notice, at most
/// once a minute.
/// </summary>
sealed class DashScreenshot(
    Func<string, string, Task<bool>> publish,
    SynchronizationContext ui,
    string folder,
    Action<string, byte[]> onShot,
    Action<string> notify,
    Func<DateTimeOffset>? clock = null)
{
    public const string RequestTopic = "pidash/desktop/screenshot";
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    const long TellEveryMs = 60_000;

    public static string DefaultFolder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Pi Dash");

    static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.Now);
    readonly ConcurrentDictionary<string, bool> _pending = new();
    long _toldAt = long.MinValue / 2;

    /// <summary>Asks the dash for a shot. Returns the request's id, or null when it couldn't be sent.</summary>
    public async Task<string?> RequestAsync(TimeSpan? timeout = null)
    {
        var id = Guid.NewGuid().ToString("N");
        _pending[id] = true;
        if (!await publish(RequestTopic, JsonSerializer.Serialize(new { id })))
        {
            _pending.TryRemove(id, out _);
            Tell("The Pi Dash isn't connected, so there's no screenshot.");
            return null;
        }
        Log.Info($"Screenshot of the dash asked for ({id})");
        _ = Task.Delay(timeout ?? Timeout).ContinueWith(_ =>
        {
            if (_pending.TryRemove(id, out var __))
            {
                Log.Warn($"Screenshot of the dash: nothing came back within {(timeout ?? Timeout).TotalSeconds:0} s ({id})");
                Tell("The dash didn't send a screenshot.");
            }
        }, TaskScheduler.Default);
        return id;
    }

    /// <summary>A PNG from the dash (any thread). Returns where it was saved, or null if it wasn't ours, came too late
    /// or isn't a PNG.</summary>
    public string? Received(string id, byte[] png)
    {
        if (!_pending.TryRemove(id, out _))
            return null;
        if (png.Length < PngSignature.Length || !png.AsSpan(0, PngSignature.Length).SequenceEqual(PngSignature))
        {
            Log.Warn($"Screenshot of the dash: {png.Length} bytes that aren't a PNG ({id})");
            Tell("The dash couldn't take a screenshot.");
            return null;
        }
        string path;
        try
        {
            Directory.CreateDirectory(folder);
            path = CaptureMath.FreePath(folder, "dash", _clock());
            File.WriteAllBytes(path, png);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error("Saving the dash's screenshot failed", ex);
            Tell("The dash's screenshot couldn't be saved.");
            return null;
        }
        Log.Info($"Screenshot of the dash saved: {path} ({png.Length / 1024} KB)");
        ui.Post(_ => onShot(path, png), null);
        return path;
    }

    void Tell(string text)
    {
        var now = Environment.TickCount64;
        if (now - Interlocked.Read(ref _toldAt) < TellEveryMs)
            return;
        Interlocked.Exchange(ref _toldAt, now);
        ui.Post(_ => notify(text), null);
    }
}
