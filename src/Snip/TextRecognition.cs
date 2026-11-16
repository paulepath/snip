using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace Snip;

/// <summary>
/// The Get text tool's reading: Windows' own text recognition (Windows.Media.Ocr), on the device, in the languages
/// the user has in Windows (Settings &gt; Time &amp; language &gt; Language). The part is enlarged first, because screen
/// text is small for it.
/// </summary>
static class TextRecognition
{
    public sealed record Result(bool NoEngine, string Text);

    static OcrEngine? _engine;

    static OcrEngine? Engine => _engine ??= OcrEngine.TryCreateFromUserProfileLanguages()
        ?? (OcrEngine.AvailableRecognizerLanguages.FirstOrDefault() is { } language ? OcrEngine.TryCreateFromLanguage(language) : null);

    /// <summary>The text in <paramref name="pixels"/>, one line per line found ("" if none).</summary>
    public static async Task<Result> ReadAsync(PixelBuffer pixels)
    {
        if (Engine is not { } engine) return new Result(true, "");
        const int margin = 12;
        var max = (int)OcrEngine.MaxImageDimension;
        var factor = Math.Clamp((max - 2 * margin) / Math.Max(pixels.Width, pixels.Height), 1, 2);
        var image = Pixels.Enlarge(pixels, factor, margin);
        using var bitmap = SoftwareBitmap.CreateCopyFromBuffer(image.Bgra.AsBuffer(), BitmapPixelFormat.Bgra8,
            image.Width, image.Height, BitmapAlphaMode.Premultiplied);
        var result = await engine.RecognizeAsync(bitmap);
        return new Result(false, string.Join(Environment.NewLine, result.Lines.Select(line => line.Text)));
    }
}
