using System.Collections.Immutable;

namespace Snip;

/// <summary>How a mark is drawn: its colour and its width (a line's thickness; for text and steps, their size).</summary>
public sealed record MarkStyle(Rgba Colour, double Width)
{
    public static readonly MarkStyle Default = new(Rgba.Red, 3);
    public const double MinWidth = 1, MaxWidth = 12;

    /// <summary>A text label's font size for this width.</summary>
    public double FontSize => 12 + Width * 2.5;

    /// <summary>A step badge's radius for this width.</summary>
    public double StepRadius => 10 + Width * 1.6;
}

/// <summary>
/// One mark on the image. Immutable: an edit makes a new one, so the document's history is just its old versions.
/// Every mark can say where it is, move, and say whether a point (in image pixels) is on it.
/// </summary>
public abstract record Annotation(MarkStyle Style)
{
    public abstract RectD Bounds { get; }
    public abstract Annotation Offset(double dx, double dy);
    public abstract bool Hit(Pt p, double tolerance);
    public Annotation Restyled(MarkStyle style) => this with { Style = style };
}

public sealed record ArrowShape(MarkStyle Style, Pt From, Pt To) : Annotation(Style)
{
    // the head is wider than the line: half its width either side (Shapes.ArrowHead)
    public override RectD Bounds => RectD.Between(From, To).Inflate((8 + Style.Width * 3.2) * 0.55 + Style.Width / 2);
    public override Annotation Offset(double dx, double dy) => this with { From = From + new Pt(dx, dy), To = To + new Pt(dx, dy) };
    public override bool Hit(Pt p, double tolerance) => Shapes.DistanceToSegment(p, From, To) <= tolerance + Style.Width;
}

public sealed record RectShape(MarkStyle Style, RectD Rect) : Annotation(Style)
{
    public override RectD Bounds => Rect.Inflate(Style.Width / 2);
    public override Annotation Offset(double dx, double dy) => this with { Rect = Rect.Offset(dx, dy) };
    public override bool Hit(Pt p, double tolerance) => Rect.Inflate(tolerance + Style.Width / 2).Contains(p);
}

public sealed record EllipseShape(MarkStyle Style, RectD Rect) : Annotation(Style)
{
    public override RectD Bounds => Rect.Inflate(Style.Width / 2);
    public override Annotation Offset(double dx, double dy) => this with { Rect = Rect.Offset(dx, dy) };

    public override bool Hit(Pt p, double tolerance)
    {
        var r = Rect.Inflate(tolerance + Style.Width / 2);
        if (r.W <= 0 || r.H <= 0) return false;
        double nx = (p.X - r.Centre.X) / (r.W / 2), ny = (p.Y - r.Centre.Y) / (r.H / 2);
        return nx * nx + ny * ny <= 1;
    }
}

public sealed record PenStroke(MarkStyle Style, ImmutableArray<Pt> Points) : Annotation(Style)
{
    public override RectD Bounds => RectD.Union(Points).Inflate(Style.Width / 2);
    public override Annotation Offset(double dx, double dy) => this with { Points = [.. Points.Select(q => q + new Pt(dx, dy))] };

    public override bool Hit(Pt p, double tolerance)
    {
        if (Points.Length == 1) return p.DistanceTo(Points[0]) <= tolerance + Style.Width;
        for (var i = 1; i < Points.Length; i++)
            if (Shapes.DistanceToSegment(p, Points[i - 1], Points[i]) <= tolerance + Style.Width / 2)
                return true;
        return false;
    }
}

/// <summary>Text at a point (its top left). <paramref name="Size"/> is the laid-out size, measured by the editor.</summary>
public sealed record TextLabel(MarkStyle Style, Pt At, string Text, Pt Size) : Annotation(Style)
{
    public override RectD Bounds => new(At.X, At.Y, Size.X, Size.Y);
    public override Annotation Offset(double dx, double dy) => this with { At = At + new Pt(dx, dy) };
    public override bool Hit(Pt p, double tolerance) => Bounds.Inflate(tolerance).Contains(p);
}

/// <summary>A translucent box of the colour over the image, like a highlighter pen.</summary>
public sealed record HighlightBox(MarkStyle Style, RectD Rect) : Annotation(Style)
{
    public const byte Alpha = 0x66;
    public override RectD Bounds => Rect;
    public override Annotation Offset(double dx, double dy) => this with { Rect = Rect.Offset(dx, dy) };
    public override bool Hit(Pt p, double tolerance) => Rect.Inflate(tolerance).Contains(p);
}

/// <summary>The image under the box in big blocks, to hide what's there. Applies to the screenshot itself, not to
/// the marks drawn over it.</summary>
public sealed record PixelateBox(MarkStyle Style, RectD Rect, int Block) : Annotation(Style)
{
    public const int DefaultBlock = 10;
    public override RectD Bounds => Rect;
    public override Annotation Offset(double dx, double dy) => this with { Rect = Rect.Offset(dx, dy) };
    public override bool Hit(Pt p, double tolerance) => Rect.Inflate(tolerance).Contains(p);
}

/// <summary>A numbered circle (1, 2, 3…) for "do this, then this". Numbers follow the order they were added.</summary>
public sealed record StepBadge(MarkStyle Style, Pt At, int Number) : Annotation(Style)
{
    public override RectD Bounds => new RectD(At.X, At.Y, 0, 0).Inflate(Style.StepRadius);
    public override Annotation Offset(double dx, double dy) => this with { At = At + new Pt(dx, dy) };
    public override bool Hit(Pt p, double tolerance) => p.DistanceTo(At) <= Style.StepRadius + tolerance;
}
