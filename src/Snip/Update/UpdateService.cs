using Velopack;
using Velopack.Sources;

namespace Snip.Update;

/// <summary>
/// Updates an installed Snip from the GitHub Releases CI publishes on every push to main (.github/workflows/release.yml),
/// as Total Git does. It checks in the background and downloads; the new version goes in when Snip restarts. Snip is a
/// tray app, so it restarts itself for an update whenever nothing is open (App decides when). Dev builds, not installed
/// with Setup.exe, never update.
/// </summary>
sealed class UpdateService
{
    public const string RepositoryUrl = "https://github.com/paulepath/snip";

    readonly UpdateManager _manager = new(new GithubSource(RepositoryUrl, accessToken: null, prerelease: false));
    VelopackAsset? _pending;
    int _checking;

    /// <summary>Raised (on a background thread) with the new version once it has been downloaded.</summary>
    public event Action<string>? UpdateReady;

    public bool IsInstalled => _manager.IsInstalled;

    /// <summary>The installed version, or "dev" for a local build.</summary>
    public string CurrentVersion => _manager.CurrentVersion?.ToString() ?? "dev";

    public string? PendingVersion => _pending?.Version.ToString();

    public async Task CheckAndDownloadAsync()
    {
        if (!IsInstalled || _pending is not null) return;
        if (Interlocked.Exchange(ref _checking, 1) == 1) return;
        try
        {
            var info = await _manager.CheckForUpdatesAsync();
            if (info is null) return;
            await _manager.DownloadUpdatesAsync(info);
            _pending = info.TargetFullRelease;
            Log.Info($"Update {_pending.Version} downloaded");
            UpdateReady?.Invoke(_pending.Version.ToString());
        }
        catch (Exception ex)
        {
            // offline, rate-limited or a bad release: the next check tries again
            Log.Warn($"Update check failed: {ex.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref _checking, 0);
        }
    }

    /// <summary>Installs the downloaded update and starts the new Snip. Ends this process.</summary>
    public void RestartToUpdate()
    {
        if (_pending is null) return;
        Log.Info($"Restarting into {_pending.Version}");
        _manager.ApplyUpdatesAndRestart(_pending);
    }

    /// <summary>Installs a downloaded update once Snip has exited, without starting it again.</summary>
    public void ApplyOnExit()
    {
        if (_pending is not null) _manager.WaitExitThenApplyUpdates(_pending, silent: true, restart: false);
    }
}
