using System.Text.Json;
using Snip.PiDash;


namespace Snip.Tests;

public class DashScreenshotTests : IDisposable
{
    static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];
    static readonly DateTimeOffset At = new(2026, 11, 6, 15, 43, 1, TimeSpan.Zero);

    readonly string _folder = Directory.CreateTempSubdirectory().FullName;
    readonly List<(string Topic, string Payload)> _sent = [];
    readonly List<string> _shots = [], _told = [];
    bool _connected = true;

    sealed class Inline : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state) => d(state);
    }

    DashScreenshot Make() => new((topic, payload) =>
        {
            _sent.Add((topic, payload));
            return Task.FromResult(_connected);
        },
        new Inline(), _folder, (path, _) => _shots.Add(path), _told.Add, () => At);

    public void Dispose() => Directory.Delete(_folder, true);

    [Fact]
    public async Task Asks_the_dash_and_saves_the_shot_that_comes_back_once()
    {
        var shots = Make();
        var id = await shots.RequestAsync();
        Assert.Equal(DashScreenshot.RequestTopic, _sent.Single().Topic);
        Assert.Equal(id, JsonDocument.Parse(_sent.Single().Payload).RootElement.GetProperty("id").GetString());

        Assert.Null(shots.Received("someone-elses", Png));
        var path = shots.Received(id!, Png);
        Assert.Equal(Path.Combine(_folder, "dash-20261106-154301.png"), path);
        Assert.Equal(Png, File.ReadAllBytes(path!));
        Assert.Equal([path!], _shots);
        Assert.Null(shots.Received(id!, Png));                                   // a repeat isn't saved twice
        Assert.Empty(_told);
    }

    [Fact]
    public async Task Says_so_when_the_dash_is_offline_sends_nothing_back_or_sends_no_png()
    {
        _connected = false;
        Assert.Null(await Make().RequestAsync());
        Assert.Equal(["The Pi Dash isn't connected, so there's no screenshot."], _told);

        _connected = true;
        var late = Make();
        var id = await late.RequestAsync(TimeSpan.FromMilliseconds(30));
        await Task.Delay(300);
        Assert.Equal(["The dash didn't send a screenshot."], _told.Skip(1));
        Assert.Null(late.Received(id!, Png));                                    // too late: forgotten

        var empty = Make();
        var failed = await empty.RequestAsync();
        Assert.Null(empty.Received(failed!, []));                                // the Pi's grim failed
        Assert.Equal("The dash couldn't take a screenshot.", _told.Last());
        Assert.Empty(_shots);
    }

    [Fact]
    public void Two_shots_in_one_second_get_their_own_files()
    {
        var first = CaptureMath.FreePath(_folder, "dash", At);
        File.WriteAllBytes(first, Png);
        Assert.Equal(Path.Combine(_folder, "dash-20261106-154301-2.png"), CaptureMath.FreePath(_folder, "dash", At));
    }
}
