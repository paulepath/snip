using Avalonia.Media;
using Avalonia.Styling;

namespace Snip;

/// <summary>
/// The window's colours, each with its dark and light value: Total Git's palette (D:\code\total-git,
/// Services\Palette.cs), cut down to what Snip uses, so the apps look alike. The brushes change colour in place when
/// the theme changes (<see cref="Apply"/>).
/// </summary>
public static class Palette
{
    static readonly List<Entry> Entries = [];

    public static bool IsLight { get; private set; }

    /// <summary>Around the image.</summary>
    public static readonly SolidColorBrush Backdrop = Make("#121417", "#E9ECEF");
    public static readonly SolidColorBrush Panel = Make("#23272D", "#F6F8FA");
    public static readonly SolidColorBrush Toolbar = Make("#2B2F36", "#EEF1F4");
    public static readonly SolidColorBrush Chrome = Make("#16191D", "#E3E7EB");
    public static readonly SolidColorBrush SelectedFill = Make("#1F4E7A", "#DDF4FF");
    public static readonly SolidColorBrush SelectedFillText = Make("#FFFFFF", "#0A3069");
    public static readonly SolidColorBrush TextPrimary = Make("#E6E8EB", "#1F2328");
    public static readonly SolidColorBrush TextSecondary = Make("#A9AFB8", "#59636E");
    public static readonly SolidColorBrush TextMuted = Make("#8A9099", "#656D76");
    public static readonly SolidColorBrush Accent = Make("#5AA9F2", "#0969DA");
    public static readonly SolidColorBrush Border = Make("#30353C", "#D1D9E0");
    public static readonly SolidColorBrush BorderStrong = Make("#3A404A", "#D1D9E0");
    public static readonly SolidColorBrush Success = Make("#4CC38A", "#1A7F37");

    /// <summary>Sets every colour for <paramref name="variant"/> (Light, else dark). Call on the UI thread.</summary>
    public static void Apply(ThemeVariant variant)
    {
        var light = variant == ThemeVariant.Light;
        IsLight = light;
        foreach (var e in Entries)
            e.Brush.Color = light ? e.Light : e.Dark;
    }

    static SolidColorBrush Make(string dark, string light)
    {
        var entry = new Entry(new SolidColorBrush(Color.Parse(dark)), Color.Parse(dark), Color.Parse(light));
        Entries.Add(entry);
        return entry.Brush;
    }

    sealed record Entry(SolidColorBrush Brush, Color Dark, Color Light);
}
