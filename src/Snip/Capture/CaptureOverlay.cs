using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace Snip.Capture;

/// <summary>
/// Choosing what to keep from a frozen screen: one borderless, topmost window on each monitor shows that monitor as
/// it was, dimmed. Hovering lights up the window under the pointer (click to take it); dragging marks out a region;
/// Enter takes the whole monitor under the pointer; Esc or a right click gives up. Everything is worked out in screen
/// pixels (<see cref="CaptureMath"/>), so monitors with different scaling line up.
/// </summary>
sealed class CaptureOverlay
{
    const int DragAfter = 4;   // pixels the pointer must move with the button down before it's a drag, not a click

    readonly FrozenScreen _screen;
    readonly TaskCompletionSource<PixRect?> _done = new();
    readonly List<OverlayWindow> _windows = [];
    (int X, int Y) _pointer;
    (int X, int Y)? _press;
    bool _dragging;

    CaptureOverlay(FrozenScreen screen) => _screen = screen;

    /// <summary>What to keep, in screen pixels, or null if the user gave up.</summary>
    public static Task<PixRect?> ChooseAsync(FrozenScreen screen)
    {
        var overlay = new CaptureOverlay(screen);
        overlay.Open();
        return overlay._done.Task;
    }

    /// <summary>What's lit up now: the region being dragged, else the window (or monitor) under the pointer.</summary>
    public PixRect? Selection
    {
        get
        {
            if (_dragging && _press is { } p)
                return CaptureMath.Region(p.X, p.Y, _pointer.X, _pointer.Y, _screen.Desktop);
            return CaptureMath.WindowAt(_screen.Windows, _pointer.X, _pointer.Y, _screen.Desktop)
                ?? CaptureMath.MonitorAt(_screen.Monitors, _pointer.X, _pointer.Y);
        }
    }

    public bool Dragging => _dragging;

    void Open()
    {
        _pointer = ScreenGrab.Pointer();
        foreach (var monitor in _screen.Monitors)
        {
            var window = new OverlayWindow(this, monitor, ImageIo.ToBitmap(CaptureMath.Cut(_screen.Pixels, _screen.Desktop, monitor)));
            _windows.Add(window);
            window.Show();
        }
        // the one under the pointer takes the keys
        var home = _windows.FirstOrDefault(w => w.Monitor.Contains(_pointer.X, _pointer.Y)) ?? _windows.FirstOrDefault();
        home?.Activate();
        Redraw();
    }

    public void Moved(int x, int y)
    {
        _pointer = (x, y);
        if (_press is { } p && !_dragging && (Math.Abs(x - p.X) > DragAfter || Math.Abs(y - p.Y) > DragAfter))
            _dragging = true;
        Redraw();
    }

    public void Pressed(int x, int y)
    {
        _pointer = (x, y);
        _press = (x, y);
        _dragging = false;
    }

    public void Released(int x, int y)
    {
        _pointer = (x, y);
        if (_press == null) return;
        var chosen = Selection;
        _press = null;
        Finish(chosen is { IsEmpty: false } ? chosen : null);
    }

    public void Key(Key key)
    {
        if (key == Avalonia.Input.Key.Escape)
            Finish(null);
        else if (key == Avalonia.Input.Key.Enter)
            Finish(CaptureMath.MonitorAt(_screen.Monitors, _pointer.X, _pointer.Y));
    }

    public void Cancel() => Finish(null);

    void Finish(PixRect? result)
    {
        if (_done.Task.IsCompleted) return;
        foreach (var w in _windows)
            w.Close();
        // let the overlay windows go before anything else opens
        Dispatcher.UIThread.Post(() => _done.TrySetResult(result), DispatcherPriority.Background);
    }

    void Redraw()
    {
        foreach (var w in _windows)
            w.Canvas.InvalidateVisual();
    }

    sealed class OverlayWindow : Window
    {
        public OverlayWindow(CaptureOverlay owner, PixRect monitor, Bitmap image)
        {
            Monitor = monitor;
            Canvas = new OverlayCanvas(owner, monitor, image);
            Content = Canvas;
            WindowDecorations = Avalonia.Controls.WindowDecorations.None;
            Topmost = true;
            ShowInTaskbar = false;
            CanResize = false;
            Background = Brushes.Black;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Position = new PixelPoint(monitor.X, monitor.Y);
            var scaling = Screens.ScreenFromPoint(new PixelPoint(monitor.X + monitor.W / 2, monitor.Y + monitor.H / 2))?.Scaling ?? 1;
            Width = monitor.W / scaling;
            Height = monitor.H / scaling;
            Cursor = new Cursor(StandardCursorType.Cross);
            Opened += (_, _) =>
            {
                // now that the window knows its own scaling, fit it to the monitor exactly
                Position = new PixelPoint(monitor.X, monitor.Y);
                Width = monitor.W / RenderScaling;
                Height = monitor.H / RenderScaling;
            };
            KeyDown += (_, e) => { owner.Key(e.Key); e.Handled = true; };
        }

        public PixRect Monitor { get; }
        public OverlayCanvas Canvas { get; }
    }

    sealed class OverlayCanvas(CaptureOverlay owner, PixRect monitor, Bitmap image) : Control
    {
        static readonly IBrush Dim = new SolidColorBrush(Color.FromArgb(120, 0, 0, 0));
        static readonly IBrush Edge = new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6));
        static readonly IBrush Pill = new SolidColorBrush(Color.FromArgb(220, 20, 22, 26));
        static readonly Typeface Face = new(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold);

        double Sx => Bounds.Width / monitor.W;
        double Sy => Bounds.Height / monitor.H;

        (int X, int Y) ToScreen(Point p) => (monitor.X + (int)Math.Round(p.X / Sx), monitor.Y + (int)Math.Round(p.Y / Sy));

        Rect ToLocal(PixRect r) => new((r.X - monitor.X) * Sx, (r.Y - monitor.Y) * Sy, r.W * Sx, r.H * Sy);

        public override void Render(DrawingContext context)
        {
            var all = new Rect(Bounds.Size);
            context.DrawImage(image, new Rect(0, 0, image.PixelSize.Width, image.PixelSize.Height), all);
            if (owner.Selection is not { } sel || sel.Intersect(monitor).IsEmpty)
            {
                context.FillRectangle(Dim, all);
                return;
            }
            var lit = ToLocal(sel.Intersect(monitor));
            context.FillRectangle(Dim, new Rect(0, 0, all.Width, lit.Top));
            context.FillRectangle(Dim, new Rect(0, lit.Bottom, all.Width, all.Height - lit.Bottom));
            context.FillRectangle(Dim, new Rect(0, lit.Top, lit.Left, lit.Height));
            context.FillRectangle(Dim, new Rect(lit.Right, lit.Top, all.Width - lit.Right, lit.Height));
            context.DrawRectangle(null, new Pen(Edge, owner.Dragging ? 1.5 : 3), lit.Deflate(owner.Dragging ? 0 : 1.5));

            // the size, in a pill just above the selection (inside it when there's no room)
            var label = new FormattedText($"{sel.W} × {sel.H}", System.Globalization.CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight, Face, 12, Brushes.White);
            var at = new Point(Math.Max(4, lit.Left), lit.Top - label.Height - 10 >= 0 ? lit.Top - label.Height - 10 : lit.Top + 6);
            context.DrawRectangle(Pill, null, new Rect(at.X, at.Y, label.Width + 14, label.Height + 6), 4, 4);
            context.DrawText(label, new Point(at.X + 7, at.Y + 3));
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            var (x, y) = ToScreen(e.GetPosition(this));
            owner.Moved(x, y);
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            var point = e.GetCurrentPoint(this);
            if (point.Properties.IsRightButtonPressed)
            {
                owner.Cancel();
                return;
            }
            if (!point.Properties.IsLeftButtonPressed) return;
            e.Pointer.Capture(this);
            var (x, y) = ToScreen(point.Position);
            owner.Pressed(x, y);
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            e.Pointer.Capture(null);
            var (x, y) = ToScreen(e.GetPosition(this));
            owner.Released(x, y);
        }
    }
}
