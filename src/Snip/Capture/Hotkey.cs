using Forms = System.Windows.Forms;

namespace Snip.Capture;

/// <summary>
/// Print Screen as a system-wide hotkey, on a message-only window of the UI thread (Avalonia's message loop dispatches
/// to it). <see cref="Registered"/> is false when another app holds the key, such as Windows itself while its
/// "Use the Print screen key to open screen capture" setting is on.
/// </summary>
sealed class Hotkey : Forms.NativeWindow, IDisposable
{
    const int Id = 0x5A1;
    readonly Action _pressed;

    public Hotkey(Action pressed)
    {
        _pressed = pressed;
        CreateHandle(new Forms.CreateParams { Parent = new nint(-3) });   // HWND_MESSAGE
        Registered = Native.RegisterHotKey(Handle, Id, Native.MOD_NOREPEAT, Native.VK_SNAPSHOT);
        if (Registered)
            Log.Info("Print Screen is Snip's");
        else
            Log.Warn("Print Screen is taken (Windows' own screen capture setting, or another app); Snip can't have it");
    }

    public bool Registered { get; }

    protected override void WndProc(ref Forms.Message m)
    {
        if (m.Msg == Native.WM_HOTKEY && m.WParam == Id)
        {
            try { _pressed(); }
            catch (Exception ex) { Log.Error("Print Screen failed", ex); }
            return;
        }
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        if (Handle != 0)
        {
            Native.UnregisterHotKey(Handle, Id);
            DestroyHandle();
        }
    }
}
