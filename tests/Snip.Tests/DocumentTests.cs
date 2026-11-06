namespace Snip.Tests;

public class SnipDocumentTests
{
    static readonly MarkStyle Red = MarkStyle.Default;

    [Fact]
    public void Steps_number_themselves_and_close_up_when_one_goes()
    {
        var doc = SnipDocument.For(100, 50);
        for (var i = 0; i < 3; i++)
            doc = doc.Add(new StepBadge(Red, new Pt(10 * i, 10), doc.NextStep));
        doc = doc.Add(new RectShape(Red, new RectD(0, 0, 5, 5)));
        Assert.Equal([1, 2, 3], doc.Items.OfType<StepBadge>().Select(s => s.Number));

        doc = doc.Remove(0);
        Assert.Equal([1, 2], doc.Items.OfType<StepBadge>().Select(s => s.Number));
        Assert.Equal(3, doc.NextStep);
    }

    [Fact]
    public void A_crop_stays_inside_the_image_and_a_tiny_one_is_ignored()
    {
        var doc = SnipDocument.For(100, 50);
        Assert.Equal(new RectD(0, 0, 100, 50), doc.Output);
        var cropped = doc.WithCrop(new RectD(90, 40, -40, -60));               // dragged up and left, past the top
        Assert.Equal(new RectD(50, 0, 40, 40), cropped.Output);
        Assert.Same(cropped, cropped.WithCrop(new RectD(10, 10, 2, 30)));
        Assert.Null(cropped.WithCrop(null).Crop);
    }

    [Fact]
    public void Marks_past_the_image_widen_what_gets_copied_unless_it_is_cropped()
    {
        var doc = SnipDocument.For(100, 50);
        Assert.False(doc.OutputPastImage);
        doc = doc.Add(new StepBadge(new MarkStyle(Rgba.Red, 0), new Pt(-5, 20), 1));   // radius 10, past the left edge
        Assert.Equal(new RectD(-15, 0, 115, 50), doc.Output);
        Assert.True(doc.OutputPastImage);

        var cropped = doc.WithCrop(new RectD(-30, 10, 60, 20));                        // a crop can take in the mark
        Assert.Equal(new RectD(-15, 10, 45, 20), cropped.Output);
        Assert.Equal(new RectD(20, 10, 30, 20), cropped.WithCrop(new RectD(20, 10, 30, 20)).Output);
    }

    [Fact]
    public void The_topmost_mark_under_the_pointer_is_picked()
    {
        var doc = SnipDocument.For(200, 200)
            .Add(new RectShape(Red, new RectD(10, 10, 100, 100)))
            .Add(new ArrowShape(Red, new Pt(0, 50), new Pt(200, 50)))
            .Add(new EllipseShape(Red, new RectD(150, 150, 40, 20)));
        Assert.Equal(1, doc.TopmostAt(new Pt(60, 51), 2));                      // the arrow, over the box
        Assert.Equal(0, doc.TopmostAt(new Pt(60, 90), 2));
        Assert.Equal(2, doc.TopmostAt(new Pt(170, 160), 2));
        Assert.Equal(-1, doc.TopmostAt(new Pt(152, 152), 0));                   // the ellipse's corner is outside it
        Assert.Equal(-1, doc.TopmostAt(new Pt(190, 5), 2));
    }

    [Fact]
    public void Marks_move_and_restyle_without_changing_the_old_version()
    {
        var pen = new PenStroke(Red, [new Pt(0, 0), new Pt(10, 10)]);
        var moved = (PenStroke)pen.Offset(5, -2);
        Assert.Equal([new Pt(5, -2), new Pt(15, 8)], moved.Points.ToArray());
        Assert.Equal(new Pt(0, 0), pen.Points[0]);
        var blue = pen.Restyled(new MarkStyle(Rgba.Blue, 6));
        Assert.Equal(Rgba.Blue, blue.Style.Colour);
        Assert.IsType<PenStroke>(blue);
    }

    [Fact]
    public void Shift_makes_squares_and_snaps_lines_to_45_degrees()
    {
        Assert.Equal(new Pt(30, -30), Shapes.Constrain(new Pt(0, 0), new Pt(30, -12), line: false));
        var snapped = Shapes.Constrain(new Pt(0, 0), new Pt(100, 8), line: true);
        Assert.Equal(0, snapped.Y, 6);
        Assert.Equal(100.32, snapped.X, 2);
    }

    [Fact]
    public void Colours_round_trip_through_their_hex()
    {
        Assert.Equal("#E53935", Rgba.Red.ToString());
        Assert.True(Rgba.TryParse("#66FFD600", out var c));
        Assert.Equal(Rgba.Yellow.WithAlpha(0x66), c);
        Assert.False(Rgba.TryParse("red", out _));
    }
}

public class UndoStackTests
{
    [Fact]
    public void Undo_redo_and_dirty_follow_the_saved_version()
    {
        var a = SnipDocument.For(10, 10);
        var stack = new UndoStack<SnipDocument>(a);
        Assert.False(stack.IsDirty);
        var b = a.Add(new StepBadge(MarkStyle.Default, new Pt(1, 1), 1));
        stack.Push(b);
        stack.Push(b);                                                           // the same again: no step
        Assert.True(stack.IsDirty);
        Assert.True(stack.Undo());
        Assert.Same(a, stack.Current);
        Assert.False(stack.IsDirty);
        Assert.True(stack.Redo());
        stack.MarkSaved();
        Assert.False(stack.IsDirty);
        Assert.True(stack.Undo());
        Assert.True(stack.IsDirty);
        stack.Push(a.WithCrop(new RectD(0, 0, 5, 5)));
        Assert.False(stack.CanRedo);                                             // a new edit drops the redo list
    }
}

public class PixelateTests
{
    [Fact]
    public void Each_block_becomes_its_average_and_nothing_outside_changes()
    {
        // 4x2: left 2x2 block is black and white (averages to grey), right half untouched
        var px = new byte[4 * 2 * 4];
        void Set(int x, int y, byte v) { var i = (y * 4 + x) * 4; px[i] = px[i + 1] = px[i + 2] = v; px[i + 3] = 255; }
        Set(0, 0, 0); Set(1, 0, 255); Set(0, 1, 255); Set(1, 1, 0);
        Set(2, 0, 10); Set(3, 0, 20); Set(2, 1, 30); Set(3, 1, 40);
        var image = new PixelBuffer(4, 2, px);
        var done = image.WithPixelate([new PixelateBox(MarkStyle.Default, new RectD(0, 0, 2, 2), 2)]);
        Assert.All(Enumerable.Range(0, 2).SelectMany(y => Enumerable.Range(0, 2).Select(x => done.Bgra[(y * 4 + x) * 4])), v => Assert.Equal(127, v));
        Assert.Equal(10, done.Bgra[2 * 4]);
        Assert.Equal(40, done.Bgra[(4 + 3) * 4]);
        Assert.Equal(0, image.Bgra[0]);                                          // the original is left alone
    }
}
