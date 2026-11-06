using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace Snip;

/// <summary>
/// How a second start of Snip.exe (or another app, such as the installer) reaches the Snip that's running: one line in,
/// one answer back, current user only. Lines: "open &lt;path&gt;" (a window on that image), <see cref="Show"/> (an empty
/// window to open or paste into), <see cref="Capture"/> (as Print Screen) and <see cref="Quit"/> (close, unless a
/// window holds marks not yet copied or saved: then it answers <see cref="Busy"/>).
/// </summary>
public sealed class SnipPipe : IDisposable
{
    public const string Show = "show";
    public const string Capture = "capture";
    public const string Quit = "quit";
    public const string Ok = "ok";
    public const string Busy = "busy";
    const string OpenPrefix = "open ";

    /// <summary>Per Windows account, so two people signed in at once each have their own.</summary>
    public static string DefaultName { get; } = "Snip-" + Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName)))[..16];

    public static string Open(string path) => OpenPrefix + path;

    /// <summary>The path an "open" line names, or null.</summary>
    public static string? PathOf(string line) => line.StartsWith(OpenPrefix, StringComparison.Ordinal) ? line[OpenPrefix.Length..] : null;

    /// <summary>What Snip.exe was started for. A path is made absolute, since the running copy has its own current
    /// folder. No argument (as at sign-in) gives null: Snip just starts in the tray.</summary>
    public static string? LineFor(string[] args) => args switch
    {
        ["--capture", ..] => Capture,
        ["--show", ..] => Show,
        ["--quit", ..] => Quit,
        [var first, ..] when !first.StartsWith("--", StringComparison.Ordinal) => Open(Path.GetFullPath(first)),
        _ => null,
    };

    readonly CancellationTokenSource _stop = new();

    SnipPipe() { }

    /// <summary>Hands <paramref name="line"/> to the running Snip. Its answer, or null if none is running or it didn't
    /// answer within <paramref name="timeout"/>.</summary>
    public static string? TrySend(string line, string? pipeName = null, TimeSpan? timeout = null)
    {
        var wait = timeout ?? TimeSpan.FromSeconds(5);
        try
        {
            using var client = new NamedPipeClientStream(".", pipeName ?? DefaultName, PipeDirection.InOut, PipeOptions.CurrentUserOnly);
            client.Connect(300);
            // A start the user just made may bring a window forward: let the running copy do it instead.
            if (GetNamedPipeServerProcessId(client.SafePipeHandle.DangerousGetHandle(), out var pid))
                AllowSetForegroundWindow(pid);
            using var writer = new StreamWriter(client, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
            using var reader = new StreamReader(client, Encoding.UTF8, leaveOpen: true);
            writer.WriteLine(line);
            var answer = reader.ReadLineAsync();
            return answer.Wait(wait) ? answer.Result : null;
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException or AggregateException)
        {
            return null;
        }
    }

    /// <summary>Answers later starts. <paramref name="handle"/> gets each line on a pool thread and returns the answer
    /// once it's done (the sender waits for it).</summary>
    public static SnipPipe Listen(Func<string, string> handle, string? pipeName = null)
    {
        var pipe = new SnipPipe();
        _ = pipe.RunAsync(handle, pipeName ?? DefaultName);
        return pipe;
    }

    async Task RunAsync(Func<string, string> handle, string name)
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(_stop.Token);
                using var reader = new StreamReader(server, Encoding.UTF8, leaveOpen: true);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(5));
                var line = await reader.ReadLineAsync(timeout.Token);
                if (line == null)
                    continue;
                var answer = handle(line.Trim());
                await using var writer = new StreamWriter(server, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
                await writer.WriteLineAsync(answer);
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or UnauthorizedAccessException)
            {
                // A sender that went away, or (briefly) another copy holding the name: carry on listening.
                Log.Warn($"Snip pipe: {ex.Message}");
                try { await Task.Delay(500, _stop.Token); }
                catch (OperationCanceledException) { return; }
            }
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        _stop.Dispose();
    }

    [DllImport("kernel32.dll")]
    static extern bool GetNamedPipeServerProcessId(IntPtr pipe, out uint serverProcessId);

    [DllImport("user32.dll")]
    static extern bool AllowSetForegroundWindow(uint processId);
}
