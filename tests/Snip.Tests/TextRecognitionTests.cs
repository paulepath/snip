using System.Runtime.InteropServices;
using Drawing = System.Drawing;

namespace Snip.Tests;

public class CutAndEnlargeTests
{
    static PixelBuffer Grey(int w, int h, Func<int, int, byte> value)
    {
        var px = new byte[w * h * 4];
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            var i = (y * w + x) * 4;
            px[i] = px[i + 1] = px[i + 2] = value(x, y);
            px[i + 3] = 255;
        }
        return new PixelBuffer(w, h, px);
    }

    [Fact]
    public void Cut_takes_the_part_inside_and_clips_to_the_picture()
    {
        var image = Grey(4, 3, (x, y) => (byte)(y * 10 + x));
        var part = Pixels.Cut(image, new RectD(2, 1, 5, 5))!;   // runs past the right and bottom edges
        Assert.Equal((2, 2), (part.Width, part.Height));
        Assert.Equal(new byte[] { 12, 13, 22, 23 }, new[] { part.Bgra[0], part.Bgra[4], part.Bgra[8], part.Bgra[12] });
    }

    [Fact]
    public void Cut_of_a_part_outside_the_picture_is_null()
    {
        Assert.Null(Pixels.Cut(Grey(4, 3, (_, _) => 0), new RectD(10, 10, 5, 5)));
        Assert.Null(Pixels.Cut(Grey(4, 3, (_, _) => 0), new RectD(-8, 0, 5, 3)));
    }

    [Fact]
    public void Cut_works_for_a_box_dragged_backwards()
    {
        var part = Pixels.Cut(Grey(4, 3, (x, y) => (byte)(y * 10 + x)), new RectD(3, 2, -2, -2))!;
        Assert.Equal((2, 2), (part.Width, part.Height));
        Assert.Equal(1, part.Bgra[0]);
    }

    [Fact]
    public void Enlarge_scales_smoothly_and_pads_with_the_edge()
    {
        var image = Grey(2, 1, (x, _) => x == 0 ? (byte)0 : (byte)200);
        var big = Pixels.Enlarge(image, 2, margin: 3);
        Assert.Equal((2 * 2 + 6, 1 * 2 + 6), (big.Width, big.Height));
        byte At(int x, int y) => big.Bgra[(y * big.Width + x) * 4];
        Assert.Equal(0, At(0, 0));                    // margin: the edge colour
        Assert.Equal(200, At(big.Width - 1, 0));
        Assert.True(At(4, 4) is > 0 and < 200);     // between the two pixels: blended
        Assert.Equal(255, big.Bgra[3]);
    }
}

public class TextRecognitionTests
{
    /// <summary>Draws text the size of screen text (Segoe UI, 11 px, ClearType off), as a screenshot would hold it.</summary>
    static PixelBuffer Render(string text)
    {
        using var bitmap = new Drawing.Bitmap(360, 40, Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Drawing.Graphics.FromImage(bitmap))
        {
            g.Clear(Drawing.Color.White);
            g.TextRenderingHint = Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            using var font = new Drawing.Font("Segoe UI", 11, Drawing.GraphicsUnit.Pixel);
            g.DrawString(text, font, Drawing.Brushes.Black, 6, 12);
        }
        var data = bitmap.LockBits(new Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height),
            Drawing.Imaging.ImageLockMode.ReadOnly, Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            var bytes = new byte[bitmap.Width * bitmap.Height * 4];
            for (var y = 0; y < bitmap.Height; y++)
                Marshal.Copy(data.Scan0 + y * data.Stride, bytes, y * bitmap.Width * 4, bitmap.Width * 4);
            return new PixelBuffer(bitmap.Width, bitmap.Height, bytes);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    [Fact]
    public async Task Reads_small_screen_text()
    {
        var result = await TextRecognition.ReadAsync(Render("Invoice 4471 is due on Friday"));
        if (result.NoEngine) return;   // a Windows with no recognition language (some build servers): nothing to test
        Assert.Contains("Invoice 4471 is due on Friday", result.Text);
    }

    [Fact]
    public async Task A_blank_part_reads_as_no_text()
    {
        var result = await TextRecognition.ReadAsync(Render(""));
        Assert.Equal("", result.Text);
    }
}
