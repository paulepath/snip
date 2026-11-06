using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Drawing = System.Drawing;

namespace Snip.Capture;

/// <summary>The whole desktop as it was when Print Screen was pressed, with its monitors and visible windows.</summary>
sealed record FrozenScreen(PixelBuffer Pixels, PixRect Desktop, IReadOnlyList<PixRect> Monitors, IReadOnlyList<CapturedWindow> Windows);

/// <summary>Takes the virtual desktop (every monitor, physical pixels) with GDI, and lists what's on it: the monitors,
/// and the visible top-level windows in z-order (top first) with their bounds as DWM draws them (no invisible resize
/// border), leaving out Snip's own, minimised and cloaked ones and the desktop itself.</summary>
static class ScreenGrab
{
    public static FrozenScreen Take()
    {
        var desktop = new PixRect(Native.GetSystemMetrics(Native.SM_XVIRTUALSCREEN), Native.GetSystemMetrics(Native.SM_YVIRTUALSCREEN),
            Native.GetSystemMetrics(Native.SM_CXVIRTUALSCREEN), Native.GetSystemMetrics(Native.SM_CYVIRTUALSCREEN));
        // the windows first: the overlay isn't up yet, but listing is quick and the grab should be as early as possible
        var windows = Windows();
        var pixels = Grab(desktop);
        return new FrozenScreen(pixels, desktop, Monitors(), windows);
    }

    static PixelBuffer Grab(PixRect desktop)
    {
        using var bitmap = new Drawing.Bitmap(desktop.W, desktop.H, Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Drawing.Graphics.FromImage(bitmap))
        {
            // BitBlt itself: Graphics.CopyFromScreen won't take CAPTUREBLT, which layered windows need
            var dest = g.GetHdc();
            var screen = Native.GetDC(0);
            try { Native.BitBlt(dest, 0, 0, desktop.W, desktop.H, screen, desktop.X, desktop.Y, Native.SRCCOPY_CAPTUREBLT); }
            finally
            {
                Native.ReleaseDC(0, screen);
                g.ReleaseHdc(dest);
            }
        }
        var data = bitmap.LockBits(new Drawing.Rectangle(0, 0, desktop.W, desktop.H), Drawing.Imaging.ImageLockMode.ReadOnly,
            Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            var bytes = new byte[desktop.W * desktop.H * 4];
            for (var y = 0; y < desktop.H; y++)
                Marshal.Copy(data.Scan0 + y * data.Stride, bytes, y * desktop.W * 4, desktop.W * 4);
            for (var i = 3; i < bytes.Length; i += 4)
                bytes[i] = 255;   // the screen is opaque, whatever GDI leaves in alpha
            return new PixelBuffer(desktop.W, desktop.H, bytes);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    static List<PixRect> Monitors()
    {
        var list = new List<PixRect>();
        Native.EnumDisplayMonitors(0, 0, (nint monitor, nint _, ref Native.RECT _, nint _) =>
        {
            var info = new Native.MONITORINFO { Size = Marshal.SizeOf<Native.MONITORINFO>() };
            if (Native.GetMonitorInfo(monitor, ref info))
                list.Add(new PixRect(info.Monitor.Left, info.Monitor.Top, info.Monitor.Right - info.Monitor.Left, info.Monitor.Bottom - info.Monitor.Top));
            return true;
        }, 0);
        return list;
    }

    static List<CapturedWindow> Windows()
    {
        var list = new List<CapturedWindow>();
        var self = (uint)Environment.ProcessId;
        var text = new StringBuilder(256);
        Native.EnumWindows((hwnd, _) =>
        {
            if (!Native.IsWindowVisible(hwnd) || Native.IsIconic(hwnd))
                return true;
            Native.GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == self)
                return true;
            var ex = (long)Native.GetWindowLongPtr(hwnd, Native.GWL_EXSTYLE);
            if ((ex & Native.WS_EX_TOOLWINDOW) != 0 && (ex & Native.WS_EX_NOACTIVATE) != 0)
                return true;
            if (Native.DwmGetWindowAttribute(hwnd, Native.DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0)
                return true;   // on another virtual desktop, or a suspended store app
            text.Clear();
            Native.GetClassName(hwnd, text, text.Capacity);
            if (text.ToString() is "Progman" or "WorkerW")
                return true;   // the desktop: a click there means the monitor
            if (Native.DwmGetWindowAttribute(hwnd, Native.DWMWA_EXTENDED_FRAME_BOUNDS, out Native.RECT r, Marshal.SizeOf<Native.RECT>()) != 0)
                return true;
            var bounds = new PixRect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
            if (bounds.W < 8 || bounds.H < 8)
                return true;
            text.Clear();
            Native.GetWindowText(hwnd, text, text.Capacity);
            list.Add(new CapturedWindow(bounds, text.ToString()));
            return true;
        }, 0);
        return list;
    }

    /// <summary>Where the pointer is, in screen pixels.</summary>
    public static (int X, int Y) Pointer() => Native.GetCursorPos(out var p) ? (p.X, p.Y) : (0, 0);

    [Conditional("DEBUG")]
    public static void Describe(FrozenScreen s) =>
        Log.Info($"Capture: desktop {s.Desktop}, {s.Monitors.Count} monitor(s), {s.Windows.Count} window(s)");
}
