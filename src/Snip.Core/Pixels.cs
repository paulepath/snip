namespace Snip;

/// <summary>An image as 32-bit BGRA pixels (straight alpha), row after row with no padding.</summary>
public sealed class PixelBuffer(int width, int height, byte[] bgra)
{
    public int Width { get; } = width;
    public int Height { get; } = height;
    public byte[] Bgra { get; } = bgra.Length == width * height * 4
        ? bgra : throw new ArgumentException($"{width}x{height} needs {width * height * 4} bytes, not {bgra.Length}");

    public int Stride => Width * 4;
    public PixelBuffer Clone() => new(Width, Height, (byte[])Bgra.Clone());

    /// <summary>A copy with every <see cref="PixelateBox"/> of the document applied.</summary>
    public PixelBuffer WithPixelate(IEnumerable<PixelateBox> boxes)
    {
        var copy = Clone();
        foreach (var box in boxes)
            Pixels.Pixelate(copy, box.Rect, box.Block);
        return copy;
    }
}

public static class Pixels
{
    /// <summary>Within <paramref name="rect"/>, each <paramref name="block"/>-pixel square becomes its average colour.
    /// Blocks line up with the rectangle's own corner; the ones at its right and bottom edges may be smaller.</summary>
    public static void Pixelate(PixelBuffer image, RectD rect, int block)
    {
        block = Math.Max(2, block);
        var (rx, ry, rw, rh) = rect.Normalised.Intersect(new RectD(0, 0, image.Width, image.Height)).Pixels();
        int right = Math.Min(rx + rw, image.Width), bottom = Math.Min(ry + rh, image.Height);
        var px = image.Bgra;
        var stride = image.Stride;
        for (var by = ry; by < bottom; by += block)
        for (var bx = rx; bx < right; bx += block)
        {
            int ex = Math.Min(bx + block, right), ey = Math.Min(by + block, bottom);
            long b = 0, g = 0, r = 0, a = 0;
            var count = (ex - bx) * (ey - by);
            for (var y = by; y < ey; y++)
            for (var x = bx; x < ex; x++)
            {
                var i = y * stride + x * 4;
                b += px[i]; g += px[i + 1]; r += px[i + 2]; a += px[i + 3];
            }
            byte mb = (byte)(b / count), mg = (byte)(g / count), mr = (byte)(r / count), ma = (byte)(a / count);
            for (var y = by; y < ey; y++)
            for (var x = bx; x < ex; x++)
            {
                var i = y * stride + x * 4;
                px[i] = mb; px[i + 1] = mg; px[i + 2] = mr; px[i + 3] = ma;
            }
        }
    }
}
