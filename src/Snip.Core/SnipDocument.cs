using System.Collections.Immutable;

namespace Snip;

/// <summary>
/// What's being edited: the image's size, the crop (null: all of it) and the marks over it, bottom first. Immutable,
/// so <see cref="UndoStack{T}"/> keeps each version as it was. The pixels themselves live with the editor.
/// </summary>
public sealed record SnipDocument(int Width, int Height, RectD? Crop, ImmutableList<Annotation> Items)
{
    /// <summary>A crop smaller than this (either way, in pixels) is taken as a slip and ignored.</summary>
    public const double MinCrop = 4;

    public static SnipDocument For(int width, int height) => new(width, height, null, []);

    public RectD Image => new(0, 0, Width, Height);

    /// <summary>The image and every mark, even one drawn or moved past its edge.</summary>
    public RectD Extent
    {
        get
        {
            var r = Image;
            foreach (var item in Items)
            {
                var b = item.Bounds;
                r = RectD.Between(new Pt(Math.Min(r.X, b.X), Math.Min(r.Y, b.Y)), new Pt(Math.Max(r.Right, b.Right), Math.Max(r.Bottom, b.Bottom)));
            }
            return r;
        }
    }

    /// <summary>The part that gets copied or saved: the crop, or the image with every mark (the parts past the image
    /// on white).</summary>
    public RectD Output => Crop ?? Extent;

    /// <summary>Whether <see cref="Output"/> reaches past the image, so the export needs a background.</summary>
    public bool OutputPastImage => Output.X < 0 || Output.Y < 0 || Output.Right > Width || Output.Bottom > Height;

    public SnipDocument Add(Annotation item) => this with { Items = Items.Add(item) };

    public SnipDocument Replace(int index, Annotation item) => this with { Items = Items.SetItem(index, item) };

    /// <summary>Removes a mark; the steps after it close up (1, 2, 3 stays 1, 2, 3).</summary>
    public SnipDocument Remove(int index) => (this with { Items = Items.RemoveAt(index) }).RenumberSteps();

    /// <summary>A crop within the image and its marks, or no crop for one too small to mean anything.</summary>
    public SnipDocument WithCrop(RectD? crop)
    {
        if (crop is not { } c)
            return this with { Crop = null };
        var r = c.Normalised.Intersect(Extent);
        return r.W < MinCrop || r.H < MinCrop ? this : this with { Crop = r };
    }

    /// <summary>The number the next step badge gets.</summary>
    public int NextStep => Items.OfType<StepBadge>().Count() + 1;

    public SnipDocument RenumberSteps()
    {
        var n = 0;
        var items = Items.Select(a => a is StepBadge s ? (++n == s.Number ? s : s with { Number = n }) : a).ToImmutableList();
        return this with { Items = items };
    }

    /// <summary>The topmost mark at a point, or -1.</summary>
    public int TopmostAt(Pt p, double tolerance)
    {
        for (var i = Items.Count - 1; i >= 0; i--)
            if (Items[i].Hit(p, tolerance))
                return i;
        return -1;
    }

    public IEnumerable<PixelateBox> Pixelated => Items.OfType<PixelateBox>();
}
