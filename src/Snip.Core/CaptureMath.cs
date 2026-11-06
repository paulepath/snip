namespace Snip;

/// <summary>A rectangle in screen pixels (physical; the virtual desktop may start left of or above 0,0).</summary>
public readonly record struct PixRect(int X, int Y, int W, int H)
{
    public int Right => X + W;
    public int Bottom => Y + H;
    public bool IsEmpty => W <= 0 || H <= 0;
    public bool Contains(int x, int y) => x >= X && x < Right && y >= Y && y < Bottom;

    /// <summary>The rectangle two corners span, whichever way round: both corner pixels included.</summary>
    public static PixRect Between(int x1, int y1, int x2, int y2) =>
        new(Math.Min(x1, x2), Math.Min(y1, y2), Math.Abs(x2 - x1) + 1, Math.Abs(y2 - y1) + 1);

    public PixRect Intersect(PixRect o)
    {
        int x = Math.Max(X, o.X), y = Math.Max(Y, o.Y), r = Math.Min(Right, o.Right), b = Math.Min(Bottom, o.Bottom);
        return r <= x || b <= y ? new PixRect(x, y, 0, 0) : new PixRect(x, y, r - x, b - y);
    }
}

/// <summary>A top-level window as it was when the screen was taken: where it is, and its title (for the log).</summary>
public sealed record CapturedWindow(PixRect Bounds, string Title);

/// <summary>The choosing part of a capture, as plain arithmetic: which window or monitor is under a point, a dragged
/// region, and cutting the chosen part out of the frozen desktop.</summary>
public static class CaptureMath
{
    /// <summary>The topmost window under a point (the list is in z-order, top first), within the desktop; or null.</summary>
    public static PixRect? WindowAt(IReadOnlyList<CapturedWindow> zOrder, int x, int y, PixRect desktop)
    {
        foreach (var w in zOrder)
            if (w.Bounds.Contains(x, y))
            {
                var r = w.Bounds.Intersect(desktop);
                return r.IsEmpty ? null : r;
            }
        return null;
    }

    public static PixRect? MonitorAt(IReadOnlyList<PixRect> monitors, int x, int y)
    {
        foreach (var m in monitors)
            if (m.Contains(x, y))
                return m;
        return null;
    }

    /// <summary>A dragged region, kept on the desktop.</summary>
    public static PixRect Region(int x1, int y1, int x2, int y2, PixRect desktop) => PixRect.Between(x1, y1, x2, y2).Intersect(desktop);

    /// <summary>The part of the frozen desktop (whose top left is at <paramref name="desktop"/>'s X, Y) under
    /// <paramref name="region"/>.</summary>
    public static PixelBuffer Cut(PixelBuffer frozen, PixRect desktop, PixRect region)
    {
        var r = region.Intersect(desktop);
        if (r.IsEmpty) throw new ArgumentException("The region is off the desktop.", nameof(region));
        var bytes = new byte[r.W * r.H * 4];
        for (var row = 0; row < r.H; row++)
            Buffer.BlockCopy(frozen.Bgra, ((r.Y - desktop.Y + row) * frozen.Width + (r.X - desktop.X)) * 4, bytes, row * r.W * 4, r.W * 4);
        return new PixelBuffer(r.W, r.H, bytes);
    }

    /// <summary>A file name for a shot taken at <paramref name="at"/>: snip-20261106-154301.png, or -2, -3… when two
    /// land in the same second.</summary>
    public static string FreePath(string folder, string prefix, DateTimeOffset at)
    {
        var stem = $"{prefix}-{at:yyyyMMdd-HHmmss}";
        var path = Path.Combine(folder, stem + ".png");
        for (var n = 2; File.Exists(path); n++)
            path = Path.Combine(folder, $"{stem}-{n}.png");
        return path;
    }
}
