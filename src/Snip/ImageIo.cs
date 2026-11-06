using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace Snip;

/// <summary>
/// Images in and out: files and the clipboard are read with System.Drawing (one known pixel layout, BGRA), shown as
/// Avalonia bitmaps, and the edited image is drawn with <see cref="Renderer"/> into a bitmap of its own for Copy and
/// Save, so what you get is what the canvas shows.
/// </summary>
static class ImageIo
{
    public static PixelBuffer Load(string path)
    {
        // Read through a copy in memory, so the file isn't held open (Save may write over it).
        using var stream = new MemoryStream(File.ReadAllBytes(path));
        using var image = Drawing.Image.FromStream(stream);
        return FromDrawing(image);
    }

    static PixelBuffer FromDrawing(Drawing.Image image)
    {
        using var bitmap = new Drawing.Bitmap(image.Width, image.Height, Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Drawing.Graphics.FromImage(bitmap))
            g.DrawImage(image, 0, 0, image.Width, image.Height);
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

    /// <summary>The pixels as a PNG file's bytes (a capture, before any marks).</summary>
    public static byte[] ToPng(PixelBuffer pixels)
    {
        using var bitmap = new Drawing.Bitmap(pixels.Width, pixels.Height, Drawing.Imaging.PixelFormat.Format32bppArgb);
        var data = bitmap.LockBits(new Drawing.Rectangle(0, 0, pixels.Width, pixels.Height),
            Drawing.Imaging.ImageLockMode.WriteOnly, Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            for (var y = 0; y < pixels.Height; y++)
                Marshal.Copy(pixels.Bgra, y * pixels.Stride, data.Scan0 + y * data.Stride, pixels.Stride);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
        using var stream = new MemoryStream();
        bitmap.Save(stream, Drawing.Imaging.ImageFormat.Png);
        return stream.ToArray();
    }

    /// <summary>The pixels as an Avalonia bitmap to draw.</summary>
    public static WriteableBitmap ToBitmap(PixelBuffer pixels)
    {
        var bitmap = new WriteableBitmap(new PixelSize(pixels.Width, pixels.Height), new Vector(96, 96),
            PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        using var locked = bitmap.Lock();
        for (var y = 0; y < pixels.Height; y++)
            Marshal.Copy(pixels.Bgra, y * pixels.Stride, locked.Address + y * locked.RowBytes, pixels.Stride);
        return bitmap;
    }

    /// <summary>The edited image (the crop, with every mark) as PNG.</summary>
    public static byte[] Export(SnipDocument doc, Bitmap image)
    {
        var (x, y, w, h) = doc.Output.Pixels();
        using var target = new RenderTargetBitmap(new PixelSize(Math.Max(1, w), Math.Max(1, h)), new Vector(96, 96));
        using (var context = target.CreateDrawingContext())
        {
            if (doc.OutputPastImage)
                context.FillRectangle(Avalonia.Media.Brushes.White, new Rect(0, 0, w, h));   // behind marks past the image
            using (context.PushTransform(Matrix.CreateTranslation(-x, -y)))
                Renderer.Draw(context, doc, image);
        }
        using var stream = new MemoryStream();
#pragma warning disable CS0618 // PNG at the default quality is all Snip needs
        target.Save(stream);
#pragma warning restore CS0618
        return stream.ToArray();
    }

    /// <summary>Puts a PNG on the clipboard as a bitmap (for most apps) and as PNG (for those that keep transparency).
    /// Call on the UI thread, which is STA.</summary>
    public static void Copy(byte[] png)
    {
        using var source = new MemoryStream(png);
        using var image = Drawing.Image.FromStream(source);
        var data = new Forms.DataObject();
        data.SetImage(new Drawing.Bitmap(image));
        data.SetData("PNG", false, new MemoryStream(png));
        Forms.Clipboard.SetDataObject(data, copy: true, retryTimes: 5, retryDelay: 100);
    }

    /// <summary>An image on the clipboard, or null.</summary>
    public static PixelBuffer? Paste()
    {
        try
        {
#pragma warning disable WFDEV005 // "PNG" is a raw stream; the typed TryGetData needs a resolver for it
            if (Forms.Clipboard.GetData("PNG") is MemoryStream png)
#pragma warning restore WFDEV005
            {
                using var image = Drawing.Image.FromStream(png);
                return FromDrawing(image);
            }
            using var bitmap = Forms.Clipboard.GetImage();
            return bitmap == null ? null : FromDrawing(bitmap);
        }
        catch (Exception ex) when (ex is ExternalException or ArgumentException)
        {
            Log.Warn($"Couldn't read the clipboard's image: {ex.Message}");
            return null;
        }
    }
}
