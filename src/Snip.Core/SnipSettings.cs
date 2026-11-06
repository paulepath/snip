using System.Text.Json;
using System.Text.Json.Serialization;

namespace Snip;

/// <summary>
/// %LOCALAPPDATA%\Snip\snip.json: the theme, the colour and size last picked, where shots go, whether to use the Pi
/// Dash, and where the editor window was. Snip began inside Pi Dash, so on first run the settings it kept there
/// (%LOCALAPPDATA%\PiDash\snip.json) are carried over.
/// </summary>
public sealed class SnipSettings
{
    /// <summary>"System" (follow Windows), "Dark" or "Light".</summary>
    public string Theme { get; set; } = "System";
    public string Colour { get; set; } = Rgba.Red.ToString();
    public double Width { get; set; } = MarkStyle.Default.Width;
    /// <summary>The highlighter keeps a colour of its own (yellow unless picked), apart from the pen's.</summary>
    public string HighlightColour { get; set; } = Rgba.Yellow.ToString();
    /// <summary>Where Windows shots are saved; null for Pictures\Snip.</summary>
    public string? ShotsFolder { get; set; }
    /// <summary>Print Screen while the mouse is on the Pi Dash shoots the dash (when Pi Dash is installed).</summary>
    public bool PiDash { get; set; } = true;
    public int? Left { get; set; }
    public int? Top { get; set; }
    public double? WindowWidth { get; set; }
    public double? WindowHeight { get; set; }
    public bool Maximized { get; set; }

    [JsonIgnore]
    public string? Location { get; set; }

    public static string FilePath { get; } = Path.Combine(AppPaths.Dir, "snip.json");

    /// <summary>Where Snip kept its settings while it was part of Pi Dash.</summary>
    public static string OldFilePath { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PiDash", "snip.json");

    public static string DefaultShotsFolder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Snip");

    [JsonIgnore]
    public string Shots => string.IsNullOrWhiteSpace(ShotsFolder) ? DefaultShotsFolder : ShotsFolder;

    static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    /// <summary>The style new marks get.</summary>
    [JsonIgnore]
    public MarkStyle Style
    {
        get => new(Rgba.TryParse(Colour, out var c) ? c : Rgba.Red, Math.Clamp(Width, MarkStyle.MinWidth, MarkStyle.MaxWidth));
        set { Colour = value.Colour.ToString(); Width = value.Width; }
    }

    /// <summary>The style for new highlights: the highlighter's colour at the usual width.</summary>
    [JsonIgnore]
    public MarkStyle HighlightStyle
    {
        get => Style with { Colour = Rgba.TryParse(HighlightColour, out var c) ? c : Rgba.Yellow };
        set { HighlightColour = value.Colour.ToString(); Width = value.Width; }
    }

    /// <summary>The settings at <paramref name="path"/>; when there are none yet, those at <paramref name="oldPath"/>
    /// (saved to <paramref name="path"/> straight away), else the defaults.</summary>
    public static SnipSettings Load(string? path = null, string? oldPath = null)
    {
        path ??= FilePath;
        if (!File.Exists(path) && (oldPath ?? OldFilePath) is { } old && File.Exists(old))
        {
            var carried = Read(old) ?? new();
            carried.Location = path;
            carried.Save();
            Log.Info($"Settings carried over from {old}");
            return carried;
        }
        var settings = Read(path) ?? new();
        settings.Location = path;
        return settings;
    }

    static SnipSettings? Read(string path)
    {
        try
        {
            return JsonSerializer.Deserialize<SnipSettings>(File.ReadAllText(path), Json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    public void Save()
    {
        try
        {
            var path = Location ?? FilePath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(this, Json));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Couldn't save Snip's settings: {ex.Message}");
        }
    }
}
