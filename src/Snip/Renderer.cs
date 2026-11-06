using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Snip;

/// <summary>
/// Draws a document in image pixels onto any drawing context: the canvas (under its zoom) and the exported image
/// use the same code. Pixelate is already in the image (<see cref="PixelBuffer.WithPixelate"/>), so it draws nothing
/// here.
/// </summary>
static class Renderer
{
    static readonly Typeface TextFace = new(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold);

    public static void Draw(DrawingContext context, SnipDocument doc, Bitmap image)
    {
        var whole = new Rect(0, 0, doc.Width, doc.Height);
        context.DrawImage(image, whole, whole);
        foreach (var item in doc.Items)
            DrawMark(context, item);
    }

    public static void DrawMark(DrawingContext context, Annotation item)
    {
        var colour = ToColor(item.Style.Colour);
        var brush = new SolidColorBrush(colour);
        var pen = new Pen(brush, item.Style.Width, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        switch (item)
        {
            case ArrowShape a:
                var head = Shapes.ArrowHead(a.From, a.To, a.Style.Width);
                context.DrawLine(pen, P(a.From), P(head.Base));
                var tip = new StreamGeometry();
                using (var g = tip.Open())
                {
                    g.BeginFigure(P(head.Tip), true);
                    g.LineTo(P(head.Left));
                    g.LineTo(P(head.Right));
                    g.EndFigure(true);
                }
                context.DrawGeometry(brush, new Pen(brush, 1, lineJoin: PenLineJoin.Round), tip);
                break;
            case RectShape r:
                context.DrawRectangle(null, new Pen(brush, r.Style.Width, lineJoin: PenLineJoin.Miter), R(r.Rect));
                break;
            case EllipseShape e:
                context.DrawEllipse(null, pen, R(e.Rect).Center, e.Rect.W / 2, e.Rect.H / 2);
                break;
            case PenStroke s when s.Points.Length == 1:
                context.DrawEllipse(brush, null, P(s.Points[0]), s.Style.Width / 2, s.Style.Width / 2);
                break;
            case PenStroke s:
                var line = new StreamGeometry();
                using (var g = line.Open())
                {
                    g.BeginFigure(P(s.Points[0]), false);
                    foreach (var q in s.Points.Skip(1))
                        g.LineTo(P(q));
                    g.EndFigure(false);
                }
                context.DrawGeometry(null, pen, line);
                break;
            case TextLabel t:
                context.DrawText(Text(t.Text, t.Style), P(t.At));
                break;
            case HighlightBox h:
                context.DrawRectangle(new SolidColorBrush(ToColor(h.Style.Colour.WithAlpha(HighlightBox.Alpha))), null, R(h.Rect));
                break;
            case StepBadge b:
                var radius = b.Style.StepRadius;
                context.DrawEllipse(brush, new Pen(Brushes.White, Math.Max(1.5, radius / 8)), P(b.At), radius, radius);
                var number = new FormattedText(b.Number.ToString(CultureInfo.InvariantCulture), CultureInfo.CurrentUICulture,
                    FlowDirection.LeftToRight, new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.Bold), radius * 1.15,
                    Light(b.Style.Colour) ? Brushes.Black : Brushes.White);
                context.DrawText(number, new Point(b.At.X - number.Width / 2, b.At.Y - number.Height / 2));
                break;
        }
    }

    /// <summary>How big a text label lays out, in image pixels.</summary>
    public static Pt Measure(string text, MarkStyle style)
    {
        var formatted = Text(text.Length == 0 ? " " : text, style);
        return new Pt(Math.Ceiling(formatted.WidthIncludingTrailingWhitespace), Math.Ceiling(formatted.Height));
    }

    public static Typeface TextTypeface => TextFace;

    static FormattedText Text(string text, MarkStyle style) =>
        new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, TextFace, style.FontSize, new SolidColorBrush(ToColor(style.Colour)));

    static bool Light(Rgba c) => 0.299 * c.R + 0.587 * c.G + 0.114 * c.B > 170;

    public static Color ToColor(Rgba c) => Color.FromArgb(c.A, c.R, c.G, c.B);
    public static Point P(Pt p) => new(p.X, p.Y);
    public static Rect R(RectD r) => new(r.X, r.Y, r.W, r.H);
}
