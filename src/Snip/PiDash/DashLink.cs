using System.Buffers;
using System.Text.Json;
using MQTTnet;
using MQTTnet.Protocol;

namespace Snip.PiDash;

/// <summary>The Pi Dash's MQTT broker as its desktop app is set up to reach it (%LOCALAPPDATA%\PiDash\desktop.json).</summary>
sealed record DashMqtt(string Host, int Port, string User, string Password)
{
    public static string ConfigPath { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PiDash", "desktop.json");

    /// <summary>The broker in that file's "mqtt" section, or null when there's no Pi Dash here (or the file is unreadable).</summary>
    public static DashMqtt? Read(string? path = null)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path ?? ConfigPath));
            if (!doc.RootElement.TryGetProperty("mqtt", out var m) || m.ValueKind != JsonValueKind.Object)
                return null;
            string? Text(string name) => m.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            var port = m.TryGetProperty("port", out var p) && p.TryGetInt32(out var n) ? n : 1883;
            return Text("host") is { Length: > 0 } host && Text("user") is { Length: > 0 } user
                ? new DashMqtt(host, port, user, Text("password") ?? "")
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }
}

/// <summary>
/// Snip's own connection to the Pi Dash (docs/pidash.md): as the dash's desktop user, under a client id of its own, it
/// asks for screenshots (pidash/desktop/screenshot) and receives them (pidash/ui/screenshot/#). Nothing else. It
/// reconnects with backoff, and is only used while the Pi Dash tray app says the mouse is on the dash.
/// </summary>
sealed class DashLink
{
    /// <summary>Set by the Pi Dash tray app while the PC's mouse is on the dash (pi-dash RemoteController).</summary>
    public const string MouseOnDashEvent = @"Local\PiDash.MouseOnDash";
    public const string ScreenshotTopic = "pidash/ui/screenshot/";

    readonly DashMqtt _settings;
    readonly IMqttClient _client;
    readonly CancellationTokenSource _stop = new();
    Task? _loop;

    public DashLink(DashMqtt settings)
    {
        _settings = settings;
        _client = new MqttClientFactory().CreateMqttClient();
        _client.ApplicationMessageReceivedAsync += e =>
        {
            var topic = e.ApplicationMessage.Topic;
            if (topic.StartsWith(ScreenshotTopic, StringComparison.Ordinal) && ScreenshotReceived is { } shot)
            {
                var png = e.ApplicationMessage.Payload.ToArray();
                _ = Task.Run(() => shot(topic[ScreenshotTopic.Length..], png));
            }
            return Task.CompletedTask;
        };
    }

    public bool IsConnected => _client.IsConnected;
    public string? LastError { get; private set; }
    public string Host => _settings.Host;

    /// <summary>(request id, PNG bytes), on a pool thread.</summary>
    public Action<string, byte[]>? ScreenshotReceived { get; set; }

    /// <summary>Whether the PC's mouse is on the dash right now (false when Pi Dash isn't running).</summary>
    public static bool MouseOnDash(string name = MouseOnDashEvent)
    {
        if (!EventWaitHandle.TryOpenExisting(name, out var handle))
            return false;
        using (handle)
            return handle.WaitOne(0);
    }

    public void Start() => _loop = Task.Run(() => RunAsync(_stop.Token));

    async Task RunAsync(CancellationToken ct)
    {
        var backoff = TimeSpan.FromSeconds(2);
        while (!ct.IsCancellationRequested)
        {
            if (!_client.IsConnected)
            {
                try
                {
                    var options = new MqttClientOptionsBuilder()
                        .WithTcpServer(_settings.Host, _settings.Port)
                        .WithCredentials(_settings.User, _settings.Password)
                        .WithClientId($"snip-{Environment.MachineName}")
                        .WithCleanSession()
                        .WithKeepAlivePeriod(TimeSpan.FromSeconds(30))
                        .WithTimeout(TimeSpan.FromSeconds(10))
                        .Build();
                    await _client.ConnectAsync(options, ct);
                    await _client.SubscribeAsync(new MqttClientSubscribeOptionsBuilder()
                        .WithTopicFilter(ScreenshotTopic + "#", MqttQualityOfServiceLevel.AtLeastOnce).Build(), ct);
                    Log.Info($"Pi Dash: connected to {_settings.Host}:{_settings.Port}");
                    LastError = null;
                    backoff = TimeSpan.FromSeconds(2);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    var message = ex.InnerException?.Message ?? ex.Message;
                    if (message != LastError)
                        Log.Warn($"Pi Dash: connecting to {_settings.Host}:{_settings.Port} failed: {message} (retrying)");
                    LastError = message;
                    await Delay(backoff, ct);
                    backoff = TimeSpan.FromSeconds(Math.Min(backoff.TotalSeconds * 2, 60));
                    continue;
                }
            }
            await Delay(TimeSpan.FromSeconds(2), ct);
        }
    }

    /// <summary>Publishes if connected; false (never throws) when it couldn't.</summary>
    public async Task<bool> PublishAsync(string topic, string payload)
    {
        if (!_client.IsConnected)
            return false;
        try
        {
            var message = new MqttApplicationMessageBuilder().WithTopic(topic).WithPayload(payload)
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce).Build();
            return (await _client.PublishAsync(message)).IsSuccess;
        }
        catch (Exception ex)
        {
            Log.Warn($"Pi Dash: publish to {topic} failed: {ex.Message}");
            return false;
        }
    }

    public async Task StopAsync()
    {
        _stop.Cancel();
        if (_client.IsConnected)
            try { await _client.DisconnectAsync(); } catch { /* going away anyway */ }
        if (_loop != null)
            await Task.WhenAny(_loop, Task.Delay(2000));
        _client.Dispose();
    }

    static async Task Delay(TimeSpan delay, CancellationToken ct)
    {
        try { await Task.Delay(delay, ct); } catch (OperationCanceledException) { }
    }
}
