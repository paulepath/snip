using System.Globalization;

namespace Snip;

/// <summary>A point in image pixels.</summary>
public readonly record struct Pt(double X, double Y)
{
    public static Pt operator +(Pt a, Pt b) => new(a.X + b.X, a.Y + b.Y);
    public static Pt operator -(Pt a, Pt b) => new(a.X - b.X, a.Y - b.Y);
    public double Length => Math.Sqrt(X * X + Y * Y);
    public double DistanceTo(Pt p) => (p - this).Length;
}

/// <summary>A rectangle in image pixels; always with a non-negative width and height once made through
/// <see cref="Between"/> or <see cref="Normalised"/>.</summary>
public readonly record struct RectD(double X, double Y, double W, double H)
{
    public double Right => X + W;
    public double Bottom => Y + H;
    public Pt Centre => new(X + W / 2, Y + H / 2);

    /// <summary>The rectangle two corners span, whichever way round they are.</summary>
    public static RectD Between(Pt a, Pt b) =>
        new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y));

    public RectD Normalised => Between(new Pt(X, Y), new Pt(X + W, Y + H));
    public RectD Offset(double dx, double dy) => this with { X = X + dx, Y = Y + dy };
    public RectD Inflate(double by) => new(X - by, Y - by, W + 2 * by, H + 2 * by);
    public bool Contains(Pt p) => p.X >= X && p.X <= Right && p.Y >= Y && p.Y <= Bottom;

    public RectD Intersect(RectD other)
    {
        double x = Math.Max(X, other.X), y = Math.Max(Y, other.Y);
        double r = Math.Min(Right, other.Right), b = Math.Min(Bottom, other.Bottom);
        return r <= x || b <= y ? new RectD(x, y, 0, 0) : new RectD(x, y, r - x, b - y);
    }

    /// <summary>Whole pixels: the edges rounded outwards.</summary>
    public (int X, int Y, int W, int H) Pixels()
    {
        int x = (int)Math.Floor(X), y = (int)Math.Floor(Y);
        return (x, y, (int)Math.Ceiling(Right) - x, (int)Math.Ceiling(Bottom) - y);
    }

    public static RectD Union(IEnumerable<Pt> points)
    {
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var p in points)
        {
            minX = Math.Min(minX, p.X); minY = Math.Min(minY, p.Y);
            maxX = Math.Max(maxX, p.X); maxY = Math.Max(maxY, p.Y);
        }
        return minX > maxX ? default : new RectD(minX, minY, maxX - minX, maxY - minY);
    }
}

/// <summary>A colour, straight (not premultiplied) alpha.</summary>
public readonly record struct Rgba(byte R, byte G, byte B, byte A = 255)
{
    public static readonly Rgba Red = new(0xE5, 0x39, 0x35), Orange = new(0xFB, 0x8C, 0x00), Yellow = new(0xFF, 0xD6, 0x00),
        Green = new(0x43, 0xA0, 0x47), Blue = new(0x1E, 0x88, 0xE5), Purple = new(0x8E, 0x24, 0xAA),
        Black = new(0x21, 0x21, 0x21), White = new(0xFF, 0xFF, 0xFF);

    /// <summary>The swatches the editor offers, in order.</summary>
    public static readonly IReadOnlyList<Rgba> Swatches = [Red, Orange, Yellow, Green, Blue, Purple, Black, White];

    public Rgba WithAlpha(byte a) => this with { A = a };

    /// <summary>"#RRGGBB", or "#AARRGGBB" when not opaque.</summary>
    public override string ToString() => A == 255 ? $"#{R:X2}{G:X2}{B:X2}" : $"#{A:X2}{R:X2}{G:X2}{B:X2}";

    public static bool TryParse(string? text, out Rgba colour)
    {
        colour = default;
        var hex = text?.TrimStart('#');
        if (hex is not { Length: 6 or 8 } || !uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v))
            return false;
        colour = hex.Length == 6
            ? new Rgba((byte)(v >> 16), (byte)(v >> 8), (byte)v)
            : new Rgba((byte)(v >> 16), (byte)(v >> 8), (byte)v, (byte)(v >> 24));
        return true;
    }
}

public static class Shapes
{
    /// <summary>How far <paramref name="p"/> is from the segment a–b.</summary>
    public static double DistanceToSegment(Pt p, Pt a, Pt b)
    {
        var ab = b - a;
        var len2 = ab.X * ab.X + ab.Y * ab.Y;
        if (len2 == 0) return p.DistanceTo(a);
        var t = Math.Clamp(((p.X - a.X) * ab.X + (p.Y - a.Y) * ab.Y) / len2, 0, 1);
        return p.DistanceTo(new Pt(a.X + t * ab.X, a.Y + t * ab.Y));
    }

    /// <summary>With Shift held: a box becomes a square (an ellipse a circle) and a line snaps to 45°.</summary>
    public static Pt Constrain(Pt from, Pt to, bool line)
    {
        var d = to - from;
        if (line)
        {
            var angle = Math.Round(Math.Atan2(d.Y, d.X) / (Math.PI / 4)) * (Math.PI / 4);
            var len = d.Length;
            return new Pt(from.X + Math.Cos(angle) * len, from.Y + Math.Sin(angle) * len);
        }
        var side = Math.Max(Math.Abs(d.X), Math.Abs(d.Y));
        return new Pt(from.X + Math.CopySign(side, d.X == 0 ? 1 : d.X), from.Y + Math.CopySign(side, d.Y == 0 ? 1 : d.Y));
    }

    /// <summary>An arrow's head: its tip and the two back corners, sized from the line's width.</summary>
    public static (Pt Tip, Pt Left, Pt Right, Pt Base) ArrowHead(Pt from, Pt to, double width)
    {
        var d = to - from;
        var len = Math.Max(d.Length, 0.0001);
        var (ux, uy) = (d.X / len, d.Y / len);
        var headLen = Math.Min(len, 8 + width * 3.2);
        var half = headLen * 0.55;
        var b = new Pt(to.X - ux * headLen, to.Y - uy * headLen);
        return (to, new Pt(b.X - uy * half, b.Y + ux * half), new Pt(b.X + uy * half, b.Y - ux * half), b);
    }
}
