using Snip.Update;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace Snip;

/// <summary>
/// Snip in the notification area: a click captures a region (as Print Screen does); the menu has the other ways in,
/// what Snip can see (Print Screen, the Pi Dash), its version and any update, Start with Windows and Exit. Notices show
/// as its balloon.
/// </summary>
sealed class Tray : IDisposable
{
    readonly Forms.NotifyIcon _icon;
    readonly Forms.ToolStripMenuItem _printLine, _dashLine, _useDash, _startup, _versionLine, _update;

    public sealed record Actions(Action Capture, Action CaptureScreen, Action Open, Action Paste, Action ShotsFolder,
        Action<bool> UseDash, Action RestartToUpdate, Action Exit);

    public Tray(Actions actions, bool useDash)
    {
        _printLine = new Forms.ToolStripMenuItem("Print Screen: starting") { Enabled = false };
        _dashLine = new Forms.ToolStripMenuItem("Pi Dash: starting") { Enabled = false };
        _useDash = new Forms.ToolStripMenuItem("Use the Pi Dash") { CheckOnClick = true, Checked = useDash };
        _useDash.CheckedChanged += (_, _) => actions.UseDash(_useDash.Checked);
        _startup = new Forms.ToolStripMenuItem("Start with Windows") { CheckOnClick = true, Checked = WindowsSetup.StartsWithWindows };
        _startup.CheckedChanged += (_, _) => WindowsSetup.SetStartWithWindows(_startup.Checked);
        _versionLine = new Forms.ToolStripMenuItem("Snip") { Enabled = false };
        _update = new Forms.ToolStripMenuItem("Restart to update", null, (_, _) => actions.RestartToUpdate()) { Visible = false };

        var menu = new Forms.ContextMenuStrip();
        var capture = new Forms.ToolStripMenuItem("Capture a region", null, (_, _) => actions.Capture()) { ShortcutKeyDisplayString = "Print Screen" };
        capture.Font = new Drawing.Font(menu.Font, Drawing.FontStyle.Bold);
        menu.Items.AddRange([
            capture,
            new Forms.ToolStripMenuItem("Capture the whole screen", null, (_, _) => actions.CaptureScreen()),
            new Forms.ToolStripSeparator(),
            new Forms.ToolStripMenuItem("Open a picture…", null, (_, _) => actions.Open()),
            new Forms.ToolStripMenuItem("Paste a picture", null, (_, _) => actions.Paste()),
            new Forms.ToolStripMenuItem("Shots folder", null, (_, _) => actions.ShotsFolder()),
            new Forms.ToolStripSeparator(),
            _printLine,
            _dashLine,
            _useDash,
            _startup,
            new Forms.ToolStripSeparator(),
            _versionLine,
            _update,
            new Forms.ToolStripMenuItem("Exit", null, (_, _) => actions.Exit()),
        ]);
        menu.Opening += (_, _) => _startup.Checked = WindowsSetup.StartsWithWindows;

        using var stream = typeof(Tray).Assembly.GetManifestResourceStream("snip.ico");
        _icon = new Forms.NotifyIcon
        {
            Icon = stream != null ? new Drawing.Icon(stream) : Drawing.SystemIcons.Application,
            Text = "Snip",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.MouseClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) actions.Capture(); };
    }

    public void ShowStatus(string print, string dash)
    {
        _printLine.Text = print;
        _dashLine.Text = dash;
        _icon.Text = "Snip: " + (print.Length > 50 ? print[..50] : print);
    }

    /// <summary>The version line, and "Restart to update" while a downloaded update waits.</summary>
    public void ShowVersion(string version, string? pending)
    {
        _versionLine.Text = version == "dev" ? "Snip (a dev build: no updates)" : $"Snip {version}";
        _update.Text = $"Restart to update to {pending}";
        _update.Visible = pending != null;
    }

    public void Notify(string text) => _icon.ShowBalloonTip(3000, "Snip", text, Forms.ToolTipIcon.Info);

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
