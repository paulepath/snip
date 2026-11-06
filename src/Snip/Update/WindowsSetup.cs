using System.Text.Json;
using Microsoft.Win32;

namespace Snip.Update;

/// <summary>
/// What Snip changes in Windows, and puts back: starting at sign-in (HKCU Run "Snip"), and Print Screen. Windows 11
/// keeps Print Screen for its own screen capture unless "Use the Print screen key to open screen capture" is off
/// (HKCU\Control Panel\Keyboard, PrintScreenKeyForSnippingEnabled = 0); the value it had first is kept in
/// %LOCALAPPDATA%\Snip\install.json so uninstalling can restore it. Velopack's install and uninstall hooks call
/// <see cref="Installed"/> and <see cref="Uninstalling"/>; tools/install.ps1 does the same for a dev build.
/// </summary>
static class WindowsSetup
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string RunName = "Snip";
    const string KeyboardKey = @"Control Panel\Keyboard";
    const string PrintValue = "PrintScreenKeyForSnippingEnabled";

    static string StatePath => Path.Combine(AppPaths.Dir, "install.json");

    public static bool StartsWithWindows
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(RunName) is string;
        }
    }

    /// <summary>Starts this Snip.exe at sign-in. Installed, that's %LOCALAPPDATA%\SnipApp\current\Snip.exe, which
    /// stays put across updates.</summary>
    public static void SetStartWithWindows(bool on)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (on) key.SetValue(RunName, $"\"{Environment.ProcessPath}\"");
        else key.DeleteValue(RunName, throwOnMissingValue: false);
        Log.Info($"Start with Windows {(on ? "on" : "off")}");
    }

    /// <summary>Whether Windows' own screen capture still has Print Screen (then Snip's hotkey can't).</summary>
    public static bool WindowsHasPrintScreen
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyboardKey);
            return key?.GetValue(PrintValue) is not int v || v != 0;   // unset means on, in Windows 11
        }
    }

    /// <summary>Turns Windows' own use of Print Screen off, keeping the value it had the first time.</summary>
    public static void TakePrintScreen()
    {
        using var key = Registry.CurrentUser.CreateSubKey(KeyboardKey);
        if (!File.Exists(StatePath))
        {
            Directory.CreateDirectory(AppPaths.Dir);
            File.WriteAllText(StatePath, JsonSerializer.Serialize(new { printScreenKeyForSnippingEnabled = key.GetValue(PrintValue) as int? }));
        }
        key.SetValue(PrintValue, 0, RegistryValueKind.DWord);
        Log.Info("Print Screen: Windows' own screen capture setting turned off");
    }

    /// <summary>Puts Windows' setting back as it was before Snip.</summary>
    public static void RestorePrintScreen()
    {
        int? old = null;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(StatePath));
            if (doc.RootElement.TryGetProperty("printScreenKeyForSnippingEnabled", out var v) && v.TryGetInt32(out var n))
                old = n;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
        using var key = Registry.CurrentUser.CreateSubKey(KeyboardKey);
        if (old is { } value) key.SetValue(PrintValue, value, RegistryValueKind.DWord);
        else key.DeleteValue(PrintValue, throwOnMissingValue: false);
        try { File.Delete(StatePath); } catch (IOException) { }
        Log.Info("Print Screen: Windows' own setting put back");
    }

    /// <summary>Velopack's install hook (and the first install's): start at sign-in, and take Print Screen.</summary>
    public static void Installed()
    {
        SetStartWithWindows(true);
        TakePrintScreen();
    }

    /// <summary>Velopack's uninstall hook: undo both. %LOCALAPPDATA%\Snip (settings and log) is left; Velopack removes
    /// the app's own folder.</summary>
    public static void Uninstalling()
    {
        SetStartWithWindows(false);
        RestorePrintScreen();
    }
}
