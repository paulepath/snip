using Microsoft.Win32;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace Snip;

/// <summary>
/// Snip in the notification area: a click captures a region (as Print Screen does); the menu has the other ways in,
/// what Snip can see (Print Screen, the Pi Dash), Start with Windows and Exit. Notices show as its balloon.
/// </summary>
sealed class Tray : IDisposable
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string RunName = "Snip";

    readonly Forms.NotifyIcon _icon;
    readonly Forms.ToolStripMenuItem _printLine, _dashLine, _useDash, _startup;

    public sealed record Actions(Action Capture, Action CaptureScreen, Action Open, Action Paste, Action ShotsFolder,
        Action<bool> UseDash, Action Exit);

    public Tray(Actions actions, bool useDash)
    {
        _printLine = new Forms.ToolStripMenuItem("Print Screen: starting") { Enabled = false };
        _dashLine = new Forms.ToolStripMenuItem("Pi Dash: starting") { Enabled = false };
        _useDash = new Forms.ToolStripMenuItem("Use the Pi Dash") { CheckOnClick = true, Checked = useDash };
        _useDash.CheckedChanged += (_, _) => actions.UseDash(_useDash.Checked);
        _startup = new Forms.ToolStripMenuItem("Start with Windows") { CheckOnClick = true, Checked = StartsWithWindows };
        _startup.CheckedChanged += (_, _) => SetStartWithWindows(_startup.Checked);

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
            new Forms.ToolStripMenuItem("Exit", null, (_, _) => actions.Exit()),
        ]);
        menu.Opening += (_, _) => _startup.Checked = StartsWithWindows;

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

    public void Notify(string text) => _icon.ShowBalloonTip(3000, "Snip", text, Forms.ToolTipIcon.Info);

    public static bool StartsWithWindows
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(RunName) is string;
        }
    }

    static void SetStartWithWindows(bool on)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (on) key.SetValue(RunName, $"\"{Environment.ProcessPath}\"");
        else key.DeleteValue(RunName, throwOnMissingValue: false);
        Log.Info($"Start with Windows {(on ? "on" : "off")}");
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
