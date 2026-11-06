using Snip.PiDash;

namespace Snip.Tests;

public class CaptureMathTests
{
    // two monitors: a 4K one, and a 1080p one to its left whose top is lower
    static readonly PixRect Left = new(-1920, 400, 1920, 1080), Main = new(0, 0, 3840, 2160);
    static readonly PixRect Desktop = new(-1920, 0, 5760, 2160);

    [Fact]
    public void The_topmost_window_under_the_pointer_wins_and_is_kept_on_the_desktop()
    {
        CapturedWindow[] zOrder =
        [
            new(new PixRect(100, 100, 400, 300), "Dialog"),
            new(new PixRect(-200, 50, 1000, 800), "Editor, half on the left monitor"),
            new(new PixRect(-3000, 0, 9000, 3000), "Bigger than everything"),
        ];
        Assert.Equal(new PixRect(100, 100, 400, 300), CaptureMath.WindowAt(zOrder, 150, 150, Desktop));
        Assert.Equal(new PixRect(-200, 50, 1000, 800), CaptureMath.WindowAt(zOrder, -100, 600, Desktop));
        Assert.Equal(Desktop, CaptureMath.WindowAt(zOrder, 3000, 2000, Desktop));
        Assert.Null(CaptureMath.WindowAt([], 5, 5, Desktop));
    }

    [Fact]
    public void A_drag_either_way_covers_both_corners_and_stays_on_the_desktop()
    {
        Assert.Equal(new PixRect(10, 20, 91, 81), CaptureMath.Region(100, 100, 10, 20, Desktop));
        Assert.Equal(new PixRect(-1920, 0, 1930, 11), CaptureMath.Region(-5000, -50, 9, 10, Desktop));
        Assert.Equal(Left, CaptureMath.MonitorAt([Main, Left], -10, 500));
        Assert.Null(CaptureMath.MonitorAt([Main, Left], -10, 100));   // above the lower monitor: no screen there
    }

    [Fact]
    public void The_chosen_part_is_cut_from_the_frozen_desktop_whatever_its_origin()
    {
        // a 4x2 "desktop" starting at -2,10, each pixel's blue byte numbering it 0..7
        var desktop = new PixRect(-2, 10, 4, 2);
        var bytes = new byte[4 * 2 * 4];
        for (var i = 0; i < 8; i++) bytes[i * 4] = (byte)i;
        var cut = CaptureMath.Cut(new PixelBuffer(4, 2, bytes), desktop, new PixRect(-1, 10, 2, 2));
        Assert.Equal((2, 2), (cut.Width, cut.Height));
        Assert.Equal([1, 2, 5, 6], Enumerable.Range(0, 4).Select(i => (int)cut.Bgra[i * 4]));
        Assert.Throws<ArgumentException>(() => CaptureMath.Cut(new PixelBuffer(4, 2, bytes), desktop, new PixRect(50, 50, 2, 2)));
    }
}

public class SettingsTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory().FullName;

    public void Dispose() => Directory.Delete(_dir, true);

    [Fact]
    public void Settings_kept_while_snip_was_part_of_pi_dash_carry_over_once()
    {
        var old = Path.Combine(_dir, "PiDash", "snip.json");
        Directory.CreateDirectory(Path.GetDirectoryName(old)!);
        File.WriteAllText(old, """{"theme":"Dark","colour":"#1E88E5","width":6}""");
        var path = Path.Combine(_dir, "Snip", "snip.json");

        var carried = SnipSettings.Load(path, old);
        Assert.Equal(("Dark", 6.0), (carried.Theme, carried.Width));
        Assert.Equal(Rgba.Blue, carried.Style.Colour);
        Assert.True(File.Exists(path));
        Assert.True(carried.PiDash);
        Assert.EndsWith(Path.Combine("Pictures", "Snip"), carried.Shots);

        File.WriteAllText(old, """{"theme":"Light"}""");                    // later changes there are not ours
        Assert.Equal("Dark", SnipSettings.Load(path, old).Theme);
    }
}

public class DashMqttTests : IDisposable
{
    readonly string _file = Path.GetTempFileName();

    public void Dispose() => File.Delete(_file);

    [Fact]
    public void Reads_the_pi_dash_apps_broker_or_nothing()
    {
        File.WriteAllText(_file, """{"mqtt":{"host":"pidash.example","port":1884,"user":"desktop","password":"pw"},"calendar":{}}""");
        Assert.Equal(new DashMqtt("pidash.example", 1884, "desktop", "pw"), DashMqtt.Read(_file));

        File.WriteAllText(_file, """{"mqtt":{"host":"pi","user":"desktop"}}""");
        Assert.Equal(new DashMqtt("pi", 1883, "desktop", ""), DashMqtt.Read(_file));

        File.WriteAllText(_file, """{"mqtt":{"user":"desktop"}}""");
        Assert.Null(DashMqtt.Read(_file));
        File.WriteAllText(_file, "{ not json");
        Assert.Null(DashMqtt.Read(_file));
        Assert.Null(DashMqtt.Read(Path.Combine(Path.GetTempPath(), "no-such-pidash", "desktop.json")));
    }

    [Fact]
    public void The_mouse_is_on_the_dash_only_while_pi_dash_sets_its_event()
    {
        var name = @"Local\PiDash.MouseOnDash.test-" + Guid.NewGuid().ToString("N")[..8];
        Assert.False(DashLink.MouseOnDash(name));                            // Pi Dash not running
        using var flag = new EventWaitHandle(false, EventResetMode.ManualReset, name);
        Assert.False(DashLink.MouseOnDash(name));
        flag.Set();
        Assert.True(DashLink.MouseOnDash(name));
        flag.Reset();
        Assert.False(DashLink.MouseOnDash(name));
    }
}

public class SnipPipeTests
{
    [Fact]
    public void Each_kind_of_start_becomes_a_line()
    {
        Assert.Equal(SnipPipe.Open(Path.GetFullPath("shot.png")), SnipPipe.LineFor(["shot.png"]));
        Assert.Equal(Path.GetFullPath("shot.png"), SnipPipe.PathOf(SnipPipe.LineFor(["shot.png"])!));
        Assert.Equal(SnipPipe.Capture, SnipPipe.LineFor(["--capture"]));
        Assert.Equal(SnipPipe.Quit, SnipPipe.LineFor(["--quit"]));
        Assert.Null(SnipPipe.LineFor([]));                                  // a plain start, as at sign-in
        Assert.Null(SnipPipe.PathOf(SnipPipe.Show));
    }

    [Fact]
    public void The_running_snip_answers()
    {
        var name = "Snip-test-" + Guid.NewGuid().ToString("N")[..8];
        var got = new List<string>();
        Assert.Null(SnipPipe.TrySend(SnipPipe.Show, name, TimeSpan.FromSeconds(1)));   // nobody there
        using var _ = SnipPipe.Listen(line => { got.Add(line); return line == SnipPipe.Quit ? SnipPipe.Busy : SnipPipe.Ok; }, name);
        string? answer = null;
        Assert.True(SpinWait.SpinUntil(() => (answer = SnipPipe.TrySend(SnipPipe.Open(@"C:\x\dash.png"), name)) != null, 3000));
        Assert.Equal(SnipPipe.Ok, answer);
        Assert.Equal(SnipPipe.Busy, SnipPipe.TrySend(SnipPipe.Quit, name));
        Assert.Equal([@"open C:\x\dash.png", "quit"], got);
    }
}
