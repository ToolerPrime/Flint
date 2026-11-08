using System.Text.Json.Nodes;

namespace Flint;

/// <summary>Immutable source data; the vector and bitmap are replaceable render caches.</summary>
public sealed record PrimitiveSpec(bool Ellipse, int Width, int Height, uint Colour, bool Filled, int Stroke)
{
    public void Validate()
    {
        if (Width is < 1 or > 16384 || Height is < 1 or > 16384 || (long)Width * Height > 16_777_216)
            throw new ArgumentOutOfRangeException(nameof(Width), "Shape dimensions exceed the supported render size.");
        if (Stroke is < 1 or > 512) throw new ArgumentOutOfRangeException(nameof(Stroke));
    }
    public JsonObject ToJson() => new() { ["ellipse"] = Ellipse, ["width"] = Width, ["height"] = Height, ["colour"] = Colour, ["filled"] = Filled, ["stroke"] = Stroke };
    public static PrimitiveSpec? FromJson(JsonObject? j)
    {
        if (j == null) return null;
        var s = new PrimitiveSpec(j["ellipse"]!.GetValue<bool>(), j["width"]!.GetValue<int>(), j["height"]!.GetValue<int>(), j["colour"]!.GetValue<uint>(), j["filled"]!.GetValue<bool>(), j["stroke"]!.GetValue<int>());
        s.Validate(); return s;
    }
}

public sealed record TextSource(string Text, string Family, float Size, bool Bold, bool Italic, uint Colour, bool Smooth)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Text) || Text.Length > 4096) throw new ArgumentException("Enter 1–4096 characters of visible text.");
        if (string.IsNullOrWhiteSpace(Family) || Family.Length > 256) throw new ArgumentException("Choose a font family.");
        if (!float.IsFinite(Size) || Size is < 4 or > 512) throw new ArgumentOutOfRangeException(nameof(Size));
    }
    public JsonObject ToJson() => new() { ["text"] = Text, ["family"] = Family, ["size"] = Size, ["bold"] = Bold, ["italic"] = Italic, ["colour"] = Colour, ["smooth"] = Smooth };
    public static TextSource? FromJson(JsonObject? j)
    {
        if (j == null) return null;
        var t = new TextSource(j["text"]!.GetValue<string>(), j["family"]!.GetValue<string>(), j["size"]!.GetValue<float>(), j["bold"]!.GetValue<bool>(), j["italic"]!.GetValue<bool>(), j["colour"]!.GetValue<uint>(), j["smooth"]!.GetValue<bool>());
        t.Validate(); return t;
    }
}

public static class EditableSources
{
    public static RasterObject Shape(PrimitiveSpec spec, Affine matrix)
    {
        spec.Validate();
        var art = PrimitivePaths.Build(spec.Ellipse, spec.Width, spec.Height, spec.Colour, spec.Filled, spec.Stroke);
        var r = Tracer.Render(art, matrix, false);
        return new RasterObject(ImageOps.FromPixels(r.w, r.h, r.px))
        { Name = spec.Ellipse ? "Ellipse" : "Rectangle", Primitive = spec, Vector = art, Matrix = matrix, X = r.x, Y = r.y };
    }

    public static RasterObject Text(TextSource spec, Affine matrix)
    {
        spec.Validate();
        using var font = new Font(spec.Family, spec.Size, (spec.Bold ? FontStyle.Bold : 0) | (spec.Italic ? FontStyle.Italic : 0), GraphicsUnit.Pixel);
        // Measure with the same format as Pixels before allocating a render surface.
        using var probe = new Bitmap(1, 1);
        using var graphics = Graphics.FromImage(probe);
        using var format = new StringFormat();
        var size = graphics.MeasureString(spec.Text, font, int.MaxValue, format);
        double w = Math.Ceiling(size.Width) + 2 * TextRender.Pad, h = Math.Ceiling(size.Height) + 2 * TextRender.Pad;
        if (w > 16384 || h > 16384 || w * h > 16_777_216) throw new ArgumentException("Text is too large to render. Reduce its size or length.");
        var px = TextRender.Pixels(spec.Text, font, Color.FromArgb(unchecked((int)spec.Colour)), spec.Smooth, out int width, out int height);
        var box = Sprites.ContentRect(px, width, height);
        if (box.IsEmpty) throw new ArgumentException("That text draws no visible characters.");
        var source = ImageOps.FromPixels(box.Width, box.Height, ImageOps.CropRegion(px, width, box));
        var original = new RasterObject(source) { Name = "Text", TextSource = spec, Original = source, Matrix = Affine.Identity };
        return original.Retransform(matrix, spec.Smooth ? Resample.Smooth : Resample.Nearest);
    }

    public static RasterObject Replace(RasterObject old, PrimitiveSpec? shape, TextSource? text)
    {
        if ((shape == null) == (text == null)) throw new ArgumentException("Choose exactly one editable source.");
        var next = shape != null ? Shape(shape, old.Matrix) : Text(text!, old.Matrix);
        next = next.WithEffectList(old.Effects).WithGroups(old.GroupPath);
        next.Name = old.Name; next.Visible = old.Visible; next.Opacity = old.Opacity; next.Blend = old.Blend;
        return next;
    }
}
