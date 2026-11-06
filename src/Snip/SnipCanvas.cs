using System.Collections.Immutable;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace Snip;

public enum Tool { Select, Crop, Arrow, Rect, Ellipse, Pen, Text, Highlight, Pixelate, Step }

/// <summary>
/// The image and its marks, zoomed to fit (or as zoomed with Ctrl+wheel) and panned with the middle button or
/// Space+drag. Each tool draws by dragging (Step and Text by clicking); a drag shows the mark as it will be, and lets
/// go into <see cref="History"/> as one undo step. Select picks the topmost mark: drag to move it.
/// Everything is kept in image pixels; only drawing and the pointer go through the zoom.
/// </summary>
sealed class SnipCanvas : Control
{
    const double Gutter = 24, MinScale = 0.05, MaxScale = 16;
    static readonly IDashStyle Dashes = new DashStyle([4, 3], 0);

    UndoStack<SnipDocument>? _history;
    Bitmap? _image;
    Tool _tool = Tool.Arrow;
    double _scale = 1;
    Vector _pan;
    bool _fit = true;

    // what a drag is doing
    Pt? _start;
    Pt _end;
    bool _shift;
    List<Pt>? _pen;
    bool _moving;
    Pt _moveLast;
    SnipDocument? _beforeMove;
    Point? _panFrom;

    public SnipCanvas()
    {
        Focusable = true;
        ClipToBounds = true;
        Cursor = new Cursor(StandardCursorType.Cross);
    }

    public UndoStack<SnipDocument>? History
    {
        get => _history;
        set
        {
            if (_history != null) _history.Changed -= Changed;
            _history = value;
            if (_history != null) _history.Changed += Changed;
            Selected = -1;
            _fit = true;
            InvalidateVisual();
        }
    }

    /// <summary>The image as shown: the screenshot with any pixelate applied.</summary>
    public Bitmap? Image
    {
        get => _image;
        set { _image = value; InvalidateVisual(); }
    }

    public Tool Tool
    {
        get => _tool;
        set
        {
            _tool = value;
            Cursor = new Cursor(value == Tool.Select ? StandardCursorType.Arrow : value == Tool.Text ? StandardCursorType.Ibeam : StandardCursorType.Cross);
            if (value != Tool.Select) Selected = -1;
            InvalidateVisual();
        }
    }

    public MarkStyle MarkStyle { get; set; } = MarkStyle.Default;

    /// <summary>The selected mark's index in the document, or -1.</summary>
    public int Selected { get; private set; } = -1;

    /// <summary>Space is held: the left button pans.</summary>
    public bool SpaceHeld { get; set; }

    public double Scale => _scale;

    public event Action? SelectionChanged;
    public event Action? ZoomChanged;

    /// <summary>The Text tool clicked an empty spot (image pixels), or a label to edit (its index, else -1).</summary>
    public event Action<Pt, int>? TextRequested;

    SnipDocument? Doc => _history?.Current;

    void Changed()
    {
        if (Doc is { } doc && Selected >= doc.Items.Count) Select(-1);
        InvalidateVisual();
    }

    public void Select(int index)
    {
        if (index == Selected) return;
        Selected = index;
        SelectionChanged?.Invoke();
        InvalidateVisual();
    }

    // ------------------------------------------------------------------ zoom

    public Point ToScreen(Pt p) => new(p.X * _scale + _pan.X, p.Y * _scale + _pan.Y);
    public Pt ToImage(Point p) => new((p.X - _pan.X) / _scale, (p.Y - _pan.Y) / _scale);

    public void ZoomToFit()
    {
        _fit = true;
        InvalidateVisual();
        ZoomChanged?.Invoke();
    }

    /// <summary>100%, centred on the middle of the view.</summary>
    public void ZoomActual() => ZoomAt(1, new Point(Bounds.Width / 2, Bounds.Height / 2), absolute: true);

    void ZoomAt(double factor, Point at, bool absolute = false)
    {
        var anchor = ToImage(at);
        _fit = false;
        _scale = Math.Clamp(absolute ? factor : _scale * factor, MinScale, MaxScale);
        _pan = new Vector(at.X - anchor.X * _scale, at.Y - anchor.Y * _scale);
        InvalidateVisual();
        ZoomChanged?.Invoke();
    }

    void Fit()
    {
        if (!_fit || Doc is not { } doc || Bounds.Width <= 0) return;
        var s = Math.Max(MinScale, Math.Min(1, Math.Min((Bounds.Width - 2 * Gutter) / doc.Width, (Bounds.Height - 2 * Gutter) / doc.Height)));
        if (s != _scale)
            Dispatcher.UIThread.Post(() => ZoomChanged?.Invoke());   // the status line shows the fitted zoom
        _scale = s;
        _pan = new Vector(Math.Round((Bounds.Width - doc.Width * _scale) / 2), Math.Round((Bounds.Height - doc.Height * _scale) / 2));
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        if (_fit) ZoomChanged?.Invoke();
    }

    // ------------------------------------------------------------------ drawing

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(Palette.Backdrop, new Rect(Bounds.Size));
        if (Doc is not { } doc || _image == null)
            return;
        Fit();
        // The page: the image and any marks past its edge, which Copy gives on white
        var extent = doc.Crop is { } cr ? Union(doc.Extent, cr) : doc.Extent;
        var page = new Rect(ToScreen(new Pt(extent.X, extent.Y)), ToScreen(new Pt(extent.Right, extent.Bottom)));
        if (extent != doc.Image)
            context.FillRectangle(Brushes.White, page);
        context.DrawRectangle(null, new Pen(Palette.BorderStrong, 1), page.Inflate(0.5));

        using (context.PushTransform(Matrix.CreateScale(_scale, _scale) * Matrix.CreateTranslation(_pan.X, _pan.Y)))
        {
            Renderer.Draw(context, doc, _image);
            if (Preview() is { } preview)
            {
                if (preview is PixelateBox p)
                    context.DrawRectangle(new SolidColorBrush(Color.FromArgb(40, 128, 128, 128)), Outline(), Renderer.R(p.Rect));
                else
                    Renderer.DrawMark(context, preview);
            }
        }

        // The crop: everything outside it dimmed, its edge dashed
        if ((_tool == Tool.Crop && _start != null ? Box() : doc.Crop) is { } crop)
        {
            var c = new Rect(ToScreen(new Pt(crop.X, crop.Y)), ToScreen(new Pt(crop.Right, crop.Bottom)));
            var dim = new SolidColorBrush(Color.FromArgb(140, 0, 0, 0));
            context.FillRectangle(dim, new Rect(page.Left, page.Top, page.Width, Math.Max(0, c.Top - page.Top)));
            context.FillRectangle(dim, new Rect(page.Left, c.Bottom, page.Width, Math.Max(0, page.Bottom - c.Bottom)));
            context.FillRectangle(dim, new Rect(page.Left, c.Top, Math.Max(0, c.Left - page.Left), c.Height));
            context.FillRectangle(dim, new Rect(c.Right, c.Top, Math.Max(0, page.Right - c.Right), c.Height));
            context.DrawRectangle(null, new Pen(Brushes.White, 1, Dashes), c);
        }

        if (Selected >= 0 && Selected < doc.Items.Count)
        {
            var b = doc.Items[Selected].Bounds.Inflate(3 / _scale);
            context.DrawRectangle(null, Outline(), new Rect(ToScreen(new Pt(b.X, b.Y)), ToScreen(new Pt(b.Right, b.Bottom))));
        }
    }

    static RectD Union(RectD a, RectD b) =>
        RectD.Between(new Pt(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y)), new Pt(Math.Max(a.Right, b.Right), Math.Max(a.Bottom, b.Bottom)));

    static Pen Outline() => new(Palette.Accent, 1.5, Dashes);

    RectD Box() => RectD.Between(_start!.Value, _end);

    /// <summary>The mark a drag in progress would make, or null.</summary>
    Annotation? Preview()
    {
        var style = MarkStyle;
        if (_pen is { Count: > 0 })
            return new PenStroke(style, [.. _pen]);
        if (_start is not { } s)
            return null;
        return _tool switch
        {
            Tool.Arrow => new ArrowShape(style, s, _end),
            Tool.Rect => new RectShape(style, Box()),
            Tool.Ellipse => new EllipseShape(style, Box()),
            Tool.Highlight => new HighlightBox(style, Box()),
            Tool.Pixelate => new PixelateBox(style, Box(), PixelateBox.DefaultBlock),
            _ => null,
        };
    }

    // ------------------------------------------------------------------ the pointer

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (Doc is not { } doc || _history is not { } history) return;
        Focus();
        var point = e.GetCurrentPoint(this);
        if (point.Properties.IsMiddleButtonPressed || (point.Properties.IsLeftButtonPressed && SpaceHeld))
        {
            _panFrom = point.Position;
            e.Pointer.Capture(this);
            return;
        }
        if (!point.Properties.IsLeftButtonPressed) return;

        var p = ToImage(point.Position);
        var tolerance = 4 / _scale;
        switch (_tool)
        {
            case Tool.Select:
                var hit = doc.TopmostAt(p, tolerance);
                Select(hit);
                if (hit >= 0 && e.ClickCount == 2 && doc.Items[hit] is TextLabel)
                {
                    TextRequested?.Invoke(p, hit);
                    return;
                }
                if (hit >= 0)
                {
                    _moving = true;
                    _moveLast = p;
                    _beforeMove = doc;
                }
                break;
            case Tool.Text:
                var label = doc.TopmostAt(p, tolerance);
                TextRequested?.Invoke(p, label >= 0 && doc.Items[label] is TextLabel ? label : -1);
                return;
            case Tool.Step:
                history.Push(doc.Add(new StepBadge(MarkStyle, p, doc.NextStep)));
                Select(history.Current.Items.Count - 1);
                return;
            case Tool.Pen:
                _pen = [p];
                break;
            default:
                _start = p;
                _end = p;
                _shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
                break;
        }
        e.Pointer.Capture(this);
        InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var position = e.GetPosition(this);
        if (_panFrom is { } from)
        {
            _pan += position - from;
            _panFrom = position;
            _fit = false;
            InvalidateVisual();
            return;
        }
        var p = ToImage(position);
        if (_moving && _history is { } history && Doc is { } doc && Selected >= 0)
        {
            var d = p - _moveLast;
            _moveLast = p;
            history.Amend(doc.Replace(Selected, doc.Items[Selected].Offset(d.X, d.Y)));
        }
        else if (_pen != null)
        {
            if (p.DistanceTo(_pen[^1]) >= 1 / _scale) _pen.Add(p);
            InvalidateVisual();
        }
        else if (_start is { } s)
        {
            _shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
            _end = _shift && _tool is Tool.Arrow or Tool.Rect or Tool.Ellipse ? Shapes.Constrain(s, p, line: _tool == Tool.Arrow) : p;
            InvalidateVisual();
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        e.Pointer.Capture(null);
        if (_panFrom != null)
        {
            _panFrom = null;
            return;
        }
        if (_history is not { } history || Doc is not { } doc) return;

        if (_moving)
        {
            _moving = false;
            var moved = doc;
            if (_beforeMove != null && !ReferenceEquals(moved, _beforeMove))
            {
                history.Amend(_beforeMove);   // so the whole move is one undo step
                history.Push(moved);
            }
            _beforeMove = null;
            return;
        }

        var made = Preview();
        var tool = _tool;
        var box = _start != null ? Box() : default;
        _pen = null;
        _start = null;
        if (tool == Tool.Crop)
        {
            if (box.W >= 2 / _scale && box.H >= 2 / _scale)
                history.Push(doc.WithCrop(box));
        }
        else if (made != null && Big(made))
        {
            history.Push(doc.Add(made));
            Select(history.Current.Items.Count - 1);
        }
        InvalidateVisual();
    }

    /// <summary>Big enough to be meant: a click with a box tool makes nothing.</summary>
    bool Big(Annotation a) => a switch
    {
        ArrowShape arrow => arrow.From.DistanceTo(arrow.To) >= 4 / _scale,
        PenStroke => true,
        _ => a.Bounds.W >= 3 / _scale && a.Bounds.H >= 3 / _scale,
    };

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (Doc == null) return;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
            ZoomAt(Math.Pow(1.15, e.Delta.Y), e.GetPosition(this));
        else
        {
            _fit = false;
            _pan += e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? new Vector(e.Delta.Y * 60, 0) : new Vector(e.Delta.X * 60, e.Delta.Y * 60);
            InvalidateVisual();
        }
        e.Handled = true;
    }

    /// <summary>Drops a drag in progress (Esc).</summary>
    public bool CancelDrag()
    {
        if (_start == null && _pen == null && !_moving) return false;
        if (_moving && _beforeMove != null) _history?.Amend(_beforeMove);
        _start = null;
        _pen = null;
        _moving = false;
        InvalidateVisual();
        return true;
    }

    public ImmutableList<Annotation> Items => Doc?.Items ?? [];
}
