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
    /// <summary>The part of <paramref name="image"/> inside <paramref name="rect"/> (clipped to the image), or null if
    /// that is empty.</summary>
    public static PixelBuffer? Cut(PixelBuffer image, RectD rect)
    {
        var (x, y, w, h) = rect.Normalised.Intersect(new RectD(0, 0, image.Width, image.Height)).Pixels();
        w = Math.Min(w, image.Width - x);
        h = Math.Min(h, image.Height - y);
        if (w <= 0 || h <= 0) return null;
        var bytes = new byte[w * h * 4];
        for (var row = 0; row < h; row++)
            Buffer.BlockCopy(image.Bgra, (y + row) * image.Stride + x * 4, bytes, row * w * 4, w * 4);
        return new PixelBuffer(w, h, bytes);
    }

    /// <summary>The image <paramref name="factor"/> times bigger, smoothed (bilinear), with a <paramref name="margin"/>
    /// of its edge colour round it. Text recognition reads screen-sized text better this way.</summary>
    public static PixelBuffer Enlarge(PixelBuffer image, int factor, int margin = 0)
    {
        factor = Math.Max(1, factor);
        int w = image.Width * factor + 2 * margin, h = image.Height * factor + 2 * margin;
        var src = image.Bgra;
        var dst = new byte[w * h * 4];
        for (var y = 0; y < h; y++)
        {
            var sy = Math.Clamp((y - margin + 0.5) / factor - 0.5, 0, image.Height - 1);
            int y0 = (int)sy, y1 = Math.Min(y0 + 1, image.Height - 1);
            var fy = sy - y0;
            for (var x = 0; x < w; x++)
            {
                var sx = Math.Clamp((x - margin + 0.5) / factor - 0.5, 0, image.Width - 1);
                int x0 = (int)sx, x1 = Math.Min(x0 + 1, image.Width - 1);
                var fx = sx - x0;
                int a = y0 * image.Stride + x0 * 4, b = y0 * image.Stride + x1 * 4,
                    c = y1 * image.Stride + x0 * 4, d = y1 * image.Stride + x1 * 4;
                var o = (y * w + x) * 4;
                for (var ch = 0; ch < 4; ch++)
                {
                    var top = src[a + ch] + (src[b + ch] - src[a + ch]) * fx;
                    var bottom = src[c + ch] + (src[d + ch] - src[c + ch]) * fx;
                    dst[o + ch] = (byte)Math.Round(top + (bottom - top) * fy);
                }
            }
        }
        return new PixelBuffer(w, h, dst);
    }

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
