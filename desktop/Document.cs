/* The editable document model.
 *
 * Fireworks' structure, which is what makes layers and frames independent:
 *
 *   Document -> Layers (bottom..top)
 *                 Layer -> Cels, one per frame (sparse; a shared layer has one
 *                          cel used by every frame)
 *                            Cel -> Objects (raster today, vector/text later)
 *
 * Frames are an axis, not a container. That is the whole point: a background
 * layer can be drawn once and shared across every frame, while a character
 * layer changes per frame.
 *
 * Nothing here touches WinForms beyond System.Drawing types, so the model and
 * its tests run headless.
 */
using System.Drawing.Imaging;

namespace Flint;

/// <summary>Names are stored in files, so existing names never change; new modes are appended. See Blending.cs for the formulas.</summary>
public enum BlendMode
{
    Normal, Multiply, Screen, Add, Subtract,
    Overlay, Darken, Lighten, ColorDodge, ColorBurn, HardLight, SoftLight, Difference, Exclusion,
    Hue, Saturation, Color, Luminosity,
    Invert, Tint, Erase,
}

public enum ObjectKind { Raster, Vector, Text }

/// <summary>One reusable image in the document's library.</summary>
public sealed class LibraryItem
{
    public string Name;
    public Bitmap Pixels;
    /// <summary>Where it came from, if a file. Informational only.</summary>
    public string? Source;

    public LibraryItem(string name, Bitmap pixels, string? source = null) { Name = name; Pixels = pixels; Source = source; }
}

/// <summary>Base for anything that can sit in a cel.</summary>
public abstract class DocObject
{
    public string Name = "Object";
    public bool Visible = true;
    public byte Opacity = 255;
    public BlendMode Blend = BlendMode.Normal;
    /// <summary>Offset of this object within the canvas.</summary>
    public int X, Y;

    public abstract ObjectKind Kind { get; }
    public abstract int Width { get; }
    public abstract int Height { get; }
    public abstract DocObject CloneObject();
    public abstract void Draw(Graphics g, byte layerOpacity);
}

public sealed class RasterObject : DocObject
{
    public Bitmap Pixels;

    public RasterObject(Bitmap pixels) { Pixels = pixels; }

    /// <summary>
    /// Untouched source pixels of a placed or transformed image. When set,
    /// <see cref="Pixels"/> is a rendering of Original through
    /// <see cref="Matrix"/>, and every further transform re-renders from
    /// Original, so repeated rotation and scaling never degrade the image.
    /// </summary>
    public Bitmap? Original;
    public Affine Matrix = Affine.Identity;
    public Resample Mode = Resample.Nearest;

    /// <summary>
    /// Traced vector art (see Tracer). When set, <see cref="Pixels"/> is a rendering
    /// of it through <see cref="Matrix"/> and every transform re-renders from the
    /// art, never from pixels. Immutable after creation, so it is shared freely.
    /// </summary>
    public VectorArt? Vector;
    public PrimitiveSpec? Primitive { get; init; }
    public TextSource? TextSource { get; init; }
    public IReadOnlyList<string> GroupPath { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Live effects, applied in order after the object is rendered from Original or Vector through
    /// Matrix, and re-applied on every Retransform. Only valid together with Original or Vector (the
    /// source the effects start from). Never changed after creation; clones share the entries.
    /// </summary>
    public List<ObjectEffect>? Effects;

    // Effects are re-applied to a source; without one, Pixels already hold them.
    private bool HasEffects => Effects is { Count: > 0 } && (Original != null || Vector != null);

    /// <summary>Apply the effect list to a freshly rendered source.</summary>
    private (byte[] px, int x, int y, int w, int h) WithEffects((byte[] px, int x, int y, int w, int h) r)
    {
        if (!HasEffects) return r;
        var e = ObjectEffects.Apply(Effects!, r.px, r.w, r.h, r.x, r.y);
        return (e.px, e.x, e.y, e.w, e.h);
    }

    /// <summary>Cached 8× Scale2x of Original for RotSprite; rebuilt when needed, never saved.</summary>
    private byte[]? _up8;

    /// <summary>The pixels and matrix a transform starts from.</summary>
    public (Bitmap src, Affine m) Source => Original != null ? (Original, Matrix) : (Pixels, Affine.Translate(X, Y));

    /// <summary>
    /// A new object showing this one's source through (current matrix, then
    /// <paramref name="t"/>). Whole-pixel moves reuse the pixels untouched.
    /// </summary>
    public RasterObject Retransform(Affine t, Resample mode, bool keepVectorEdges = true)
    {
        // A vector keeps its own edge setting (chosen when it was traced): the
        // transform tool's resample choice is for bitmaps and would undo it.
        if (Vector != null) return RetransformVector(t, keepVectorEdges ? Mode : mode);
        var (src, m) = Source;
        var next = m.Then(t);
        if (t.IsIntegerTranslation)
            return new RasterObject(Pixels)
            {
                Name = Name, Visible = Visible, Opacity = Opacity, Blend = Blend,
                X = X + (int)t.Tx, Y = Y + (int)t.Ty, Original = src, Matrix = next, Mode = Mode, _up8 = _up8, Effects = Effects, Primitive = Primitive, TextSource = TextSource, GroupPath = GroupPath,
            };
        var srcPx = ImageOps.GetPixels(src);
        byte[]? up = Original != null ? _up8 : null;
        var r0 = Transform.Render(srcPx, src.Width, src.Height, next, mode,
                                 () => up ??= Transform.Up8(srcPx, src.Width, src.Height));
        var r = WithEffects((r0.px, r0.x, r0.y, r0.w, r0.h));
        return new RasterObject(ImageOps.FromPixels(r.w, r.h, r.px))
        {
            Name = Name, Visible = Visible, Opacity = Opacity, Blend = Blend,
            X = r.x, Y = r.y, Original = src, Matrix = next, Mode = mode, _up8 = up, Effects = Effects, Primitive = Primitive, TextSource = TextSource, GroupPath = GroupPath,
        };
    }

    private RasterObject RetransformVector(Affine t, Resample mode)
    {
        var next = Matrix.Then(t);
        if (t.IsIntegerTranslation)
            return new RasterObject(Pixels)
            {
                Name = Name, Visible = Visible, Opacity = Opacity, Blend = Blend,
                X = X + (int)t.Tx, Y = Y + (int)t.Ty, Vector = Vector, Matrix = next, Mode = Mode, Effects = Effects, Primitive = Primitive, TextSource = TextSource, GroupPath = GroupPath,
            };
        var r = WithEffects(Tracer.Render(Vector!, next, mode == Resample.Smooth));
        return new RasterObject(ImageOps.FromPixels(r.w, r.h, r.px))
        {
            Name = Name, Visible = Visible, Opacity = Opacity, Blend = Blend,
            X = r.x, Y = r.y, Vector = Vector, Matrix = next, Mode = mode, Effects = Effects, Primitive = Primitive, TextSource = TextSource, GroupPath = GroupPath,
        };
    }

    public bool IsTransformed => Original != null || Vector != null || HasEffects;

    public override ObjectKind Kind => ObjectKind.Raster;
    public override int Width => Pixels.Width;
    public override int Height => Pixels.Height;

    public override DocObject CloneObject() => new RasterObject(ImageOps.Clone(Pixels))
    {
        Name = Name, Visible = Visible, Opacity = Opacity, Blend = Blend, X = X, Y = Y,
        Original = Original == null ? null : ImageOps.Clone(Original), Matrix = Matrix, Mode = Mode, Vector = Vector,
        Effects = Effects == null ? null : new List<ObjectEffect>(Effects), Primitive = Primitive, TextSource = TextSource, GroupPath = GroupPath,
    };

    /// <summary>
    /// A new object with these pixels and position, keeping everything else.
    /// Structural edits must use this instead of changing an object, because
    /// undo snapshots share objects with the live document. The new pixels are
    /// no longer a rendering of any original, so the result is plain pixels.
    /// For a pure move use <see cref="Moved"/>, which keeps the original.
    /// </summary>
    public RasterObject With(Bitmap pixels, int? x = null, int? y = null) => new(pixels)
    {
        Name = Name, Visible = Visible, Opacity = Opacity, Blend = Blend, X = x ?? X, Y = y ?? Y, GroupPath = GroupPath,
    };

    /// <summary>
    /// A new object showing the same source with this effect list (null or empty = none), re-rendered from the source.
    /// A plain bitmap first gets its current pixels as its Original (a copy: the old object stays untouched for undo), so the
    /// effects stay live. Never changes this object.
    /// </summary>
    public RasterObject WithEffectList(List<ObjectEffect>? fx)
    {
        if (fx is { Count: 0 }) fx = null;
        if (Original == null && Vector == null && fx == null) return this;
        var orig = Original;
        var m = Matrix;
        if (Vector == null && orig == null) { orig = ImageOps.Clone(Pixels); m = Affine.Translate(X, Y); }
        (byte[] px, int x, int y, int w, int h) r;
        if (Vector != null) r = Tracer.Render(Vector, m, Mode == Resample.Smooth);
        else
        {
            var srcPx = ImageOps.GetPixels(orig!);
            var t = Transform.Render(srcPx, orig!.Width, orig.Height, m, Mode, () => Transform.Up8(srcPx, orig.Width, orig.Height));
            r = (t.px, t.x, t.y, t.w, t.h);
        }
        if (fx != null) { var e = ObjectEffects.Apply(fx, r.px, r.w, r.h, r.x, r.y); r = (e.px, e.x, e.y, e.w, e.h); }
        return new RasterObject(ImageOps.FromPixels(r.w, r.h, r.px))
        {
            Name = Name, Visible = Visible, Opacity = Opacity, Blend = Blend,
            X = r.x, Y = r.y, Original = orig, Vector = Vector, Matrix = m, Mode = Mode, Effects = fx, Primitive = Primitive, TextSource = TextSource, GroupPath = GroupPath,
        };
    }

    public RasterObject WithGroups(IEnumerable<string> path) => new(Pixels)
    {
        Name = Name, Visible = Visible, Opacity = Opacity, Blend = Blend, X = X, Y = Y,
        Original = Original, Matrix = Matrix, Mode = Mode, Vector = Vector, Effects = Effects,
        Primitive = Primitive, TextSource = TextSource, GroupPath = ObjectGroups.Validate(path), _up8 = _up8,
    };

    /// <summary>The same object shifted by whole pixels; keeps its original and matrix.</summary>
    public RasterObject Moved(int dx, int dy) => Retransform(Affine.Translate(dx, dy), Mode);

    public override void Draw(Graphics g, byte layerOpacity)
    {
        if (!Visible) return;
        float a = Opacity / 255f * (layerOpacity / 255f);
        if (a <= 0) return;

        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;

        if (a >= 1f)
        {
            g.DrawImageUnscaled(Pixels, X, Y);
            return;
        }
        var m = new ColorMatrix { Matrix33 = a };
        using var attrs = new ImageAttributes();
        attrs.SetColorMatrix(m);
        g.DrawImage(Pixels, new Rectangle(X, Y, Pixels.Width, Pixels.Height),
            0, 0, Pixels.Width, Pixels.Height, GraphicsUnit.Pixel, attrs);
    }
}

public sealed class Cel
{
    public readonly List<DocObject> Objects = new();

    /// <summary>
    /// Optional layer mask: canvas-sized, grey in the colour channels (white shows, black hides), alpha 255.
    /// Painted only through a PaintRecorder (object index <see cref="MaskIndex"/>); otherwise replaced, never changed.
    /// </summary>
    public RasterObject? Mask;
    public const int MaskIndex = -2;

    public Cel Clone()
    {
        var c = new Cel();
        foreach (var o in Objects) c.Objects.Add(o.CloneObject());
        c.Mask = (RasterObject?)Mask?.CloneObject();
        return c;
    }

    /// <summary>
    /// The raster object the paint tools write into, created on demand. A cel
    /// that has never been drawn on holds nothing, so an empty frame costs
    /// almost no memory.
    /// </summary>
    public RasterObject EnsureRaster(int w, int h)
    {
        foreach (var o in Objects)
            // Never a transformed object: painting it would make its pixels
            // disagree with the original it is re-rendered from.
            if (o is RasterObject r && !r.IsTransformed && r.X == 0 && r.Y == 0 && r.Width == w && r.Height == h)
                return r;
        var fresh = new RasterObject(ImageOps.NewBitmap(w, h)) { Name = "Bitmap" };
        Objects.Add(fresh);
        return fresh;
    }

    public RasterObject? FindRaster()
    {
        for (int i = Objects.Count - 1; i >= 0; i--)
            if (Objects[i] is RasterObject r) return r;
        return null;
    }

    public bool IsEmpty => Objects.Count == 0;
}

public sealed class Layer
{
    public string Name = "Layer";
    public bool Visible = true;
    public bool Locked;
    /// <summary>One cel reused by every frame — Fireworks' shared layer.</summary>
    public bool Shared;
    public byte Opacity = 255;
    public BlendMode Blend = BlendMode.Normal;
    /// <summary>Masks (see <see cref="Cel.Mask"/>) only apply while enabled. Linked: a whole-layer move by whole pixels (Move tool, no selection) shifts the mask too; other transforms leave it.</summary>
    public bool MaskEnabled = true, MaskLinked = true;

    /// <summary>Sparse: frame index to cel. A missing key means an empty frame.</summary>
    public readonly Dictionary<int, Cel> Cels = new();

    public Cel? CelAt(int frame)
    {
        int key = Shared ? 0 : frame;
        return Cels.TryGetValue(key, out var c) ? c : null;
    }

    public Cel EnsureCel(int frame)
    {
        int key = Shared ? 0 : frame;
        if (!Cels.TryGetValue(key, out var c)) { c = new Cel(); Cels[key] = c; }
        return c;
    }

    public Layer Clone()
    {
        var l = CopyProps();
        foreach (var (k, v) in Cels) l.Cels[k] = v.Clone();
        return l;
    }

    /// <summary>New layer and cel containers, same objects. Costs no pixels.</summary>
    public Layer ShallowCopy()
    {
        var l = CopyProps();
        foreach (var (k, v) in Cels)
        {
            var c = new Cel();
            c.Objects.AddRange(v.Objects);
            c.Mask = v.Mask;
            l.Cels[k] = c;
        }
        return l;
    }

    private Layer CopyProps() => new()
    {
        Name = Name, Visible = Visible, Locked = Locked,
        Shared = Shared, Opacity = Opacity, Blend = Blend, MaskEnabled = MaskEnabled, MaskLinked = MaskLinked,
    };

    /// <summary>Shift cel keys when a frame is inserted or removed.</summary>
    public void RemapFrames(Func<int, int?> map)
    {
        if (Shared) return;
        var next = new Dictionary<int, Cel>();
        foreach (var (k, v) in Cels)
        {
            int? to = map(k);
            // A dropped cel is not disposed: an undo snapshot may still hold it.
            if (to.HasValue) next[to.Value] = v;
        }
        Cels.Clear();
        foreach (var (k, v) in next) Cels[k] = v;
    }
}

public sealed class Hitbox
{
    public string Name = "hit";
    public Rectangle Rect;
    public Hitbox Clone() => new() { Name = Name, Rect = Rect };
}

public sealed class FrameSpec
{
    public int Delay = 100;
    public string Name = "";
    /// <summary>Origin point for game engines, in canvas pixels. Null = none set.</summary>
    public Point? Pivot;
    public readonly List<Hitbox> Hitboxes = new();

    public FrameSpec Clone()
    {
        var f = new FrameSpec { Delay = Delay, Name = Name, Pivot = Pivot };
        foreach (var h in Hitboxes) f.Hitboxes.Add(h.Clone());
        return f;
    }
}

/// <summary>A named run of frames, e.g. Idle 0-3, Walk 4-11.</summary>
public sealed class AnimTag
{
    public string Name = "Tag";
    public int Start, End;
    public Color Colour = Color.FromArgb(40, 170, 120);   // mint family; stored colours are left alone
    public AnimTag Clone() => new() { Name = Name, Start = Start, End = End, Colour = Colour };
}

public sealed class Document
{
    public int Width { get; internal set; }
    public int Height { get; internal set; }
    public readonly List<Layer> Layers = new();
    public readonly List<FrameSpec> Frames = new();
    public readonly List<AnimTag> Tags = new();
    /// <summary>
    /// Images kept for reuse, like Animate's library. Outside undo on purpose:
    /// snapshots share this same list, so undoing a placement never removes
    /// the image from the library.
    /// </summary>
    public List<LibraryItem> Library = new();
    /// <summary>Optional. Empty for silent sprites, and then costs nothing.</summary>
    public AudioProject Audio = new();
    /// <summary>Ruler guides. Always replaced, never edited in place, so undo closures can hold the old list.</summary>
    public IReadOnlyList<Guide> Guides = Array.Empty<Guide>();

    public int FrameCount => Frames.Count;
    public int LayerCount => Layers.Count;

    public Document(int width, int height)
    {
        Width = width;
        Height = height;
        Frames.Add(new FrameSpec());
        Layers.Add(new Layer { Name = "Layer 1" });
    }

    /// <summary>Empty shell for the loader, which fills in everything.</summary>
    internal Document() { }

    public Layer AddLayer(string? name = null, int? at = null)
    {
        var l = new Layer { Name = name ?? $"Layer {Layers.Count + 1}" };
        Layers.Insert(at ?? Layers.Count, l);
        return l;
    }

    public void InsertFrame(int at, FrameSpec? spec = null)
    {
        at = Math.Clamp(at, 0, Frames.Count);
        Frames.Insert(at, spec ?? new FrameSpec());
        foreach (var l in Layers)
            l.RemapFrames(k => k >= at ? k + 1 : k);
        foreach (var t in Tags)
        {
            if (t.Start >= at) t.Start++;
            if (t.End >= at) t.End++;
        }
    }

    public void RemoveFrame(int at)
    {
        if (Frames.Count <= 1 || at < 0 || at >= Frames.Count) return;
        Frames.RemoveAt(at);
        foreach (var l in Layers)
            l.RemapFrames(k => k == at ? null : k > at ? k - 1 : k);
        foreach (var t in Tags)
        {
            if (t.Start > at) t.Start--;
            if (t.End >= at) t.End = Math.Max(t.Start, t.End - 1);
        }
    }

    public void SwapFrames(int a, int b)
    {
        if (a == b || a < 0 || b < 0 || a >= Frames.Count || b >= Frames.Count) return;
        (Frames[a], Frames[b]) = (Frames[b], Frames[a]);
        foreach (var l in Layers)
        {
            if (l.Shared) continue;
            l.Cels.TryGetValue(a, out var ca);
            l.Cels.TryGetValue(b, out var cb);
            l.Cels.Remove(a);
            l.Cels.Remove(b);
            if (cb != null) l.Cels[a] = cb;
            if (ca != null) l.Cels[b] = ca;
        }
        var perm = Enumerable.Range(0, Frames.Count).ToArray();
        perm[a] = b; perm[b] = a;
        RemapTags(perm);
    }

    /// <summary>
    /// Move one frame to a new index, carrying its cels and keeping animation
    /// tags attached to the frames they named, not to fixed positions.
    /// </summary>
    public void MoveFrame(int from, int to)
    {
        int n = Frames.Count;
        if (from == to || from < 0 || to < 0 || from >= n || to >= n) return;

        // perm[old] = new index
        var order = Enumerable.Range(0, n).ToList();
        order.RemoveAt(from);
        order.Insert(to, from);
        var perm = new int[n];
        for (int i = 0; i < n; i++) perm[order[i]] = i;

        var frames = Frames.ToArray();
        for (int i = 0; i < n; i++) Frames[perm[i]] = frames[i];

        foreach (var l in Layers)
        {
            if (l.Shared) continue;
            var old = l.Cels.ToList();
            l.Cels.Clear();
            foreach (var (k, v) in old) l.Cels[perm[k]] = v;
        }

        RemapTags(perm);
    }

    /// <summary>Tags follow their frames through a reordering.</summary>
    private void RemapTags(int[] perm)
    {
        foreach (var t in Tags)
        {
            int lo = int.MaxValue, hi = int.MinValue;
            for (int i = t.Start; i <= t.End && i < perm.Length; i++)
            {
                lo = Math.Min(lo, perm[i]);
                hi = Math.Max(hi, perm[i]);
            }
            if (lo <= hi) { t.Start = lo; t.End = hi; }
        }
    }

    /// <summary>Resize the canvas, moving every object by dx/dy.</summary>
    public void Resize(int w, int h, int dx, int dy)
    {
        int oldW = Width, oldH = Height;
        MapRasters(r =>
            // A full-canvas bitmap is reframed so it keeps covering the canvas;
            // a smaller placed object only moves, and keeps its pixels.
            !r.IsTransformed && r.X == 0 && r.Y == 0 && r.Width == oldW && r.Height == oldH
                ? r.With(ImageOps.Reframe(r.Pixels, w, h, dx, dy))
                : r.Moved(dx, dy));
        // Masks cover the canvas, so they are reframed (new bitmaps; the old ones may live on in undo snapshots).
        foreach (var l in Layers)
            foreach (var c in l.Cels.Values)
                if (c.Mask is { } m && m.Width == oldW && m.Height == oldH)
                    c.Mask = ReframeMask(m, w, h, dx, dy);
        Width = w;
        Height = h;
    }

    /// <summary>
    /// Replace raster objects through <paramref name="f"/> (return null to keep
    /// one). Existing objects are never modified, so undo snapshots that share
    /// them stay intact. Returns how many were replaced.
    /// </summary>
    public int MapRasters(Func<RasterObject, RasterObject?> f,
                          Func<int, bool>? layer = null, Func<int, bool>? celKey = null)
    {
        int n = 0;
        for (int li = 0; li < Layers.Count; li++)
        {
            if (layer != null && !layer(li)) continue;
            foreach (var (key, cel) in Layers[li].Cels)
            {
                if (celKey != null && !celKey(key)) continue;
                for (int i = 0; i < cel.Objects.Count; i++)
                {
                    if (cel.Objects[i] is not RasterObject r) continue;
                    var next = f(r);
                    if (next == null) continue;
                    cel.Objects[i] = next;
                    n++;
                }
            }
        }
        return n;
    }

    /// <summary>
    /// An undo snapshot: fresh layer, cel and frame containers that share the
    /// pixel objects. Cheap — it copies structure, not images. Safe because
    /// nothing mutates a shared object except a recorded paint stroke, and
    /// strokes are undone before the snapshots older than them.
    /// </summary>
    public Document Snapshot()
    {
        var d = new Document { Width = Width, Height = Height };
        foreach (var f in Frames) d.Frames.Add(f.Clone());
        foreach (var l in Layers) d.Layers.Add(l.ShallowCopy());
        foreach (var t in Tags) d.Tags.Add(t.Clone());
        d.Audio = Audio.Clone();
        d.Library = Library;
        return d;
    }

    /// <summary>Every distinct bitmap this document references.</summary>
    public HashSet<Bitmap> RasterBitmaps()
    {
        var set = new HashSet<Bitmap>(ReferenceEqualityComparer.Instance);
        foreach (var l in Layers)
            foreach (var c in l.Cels.Values)
            {
                if (c.Mask != null) set.Add(c.Mask.Pixels);
                foreach (var o in c.Objects)
                    if (o is RasterObject r)
                    {
                        set.Add(r.Pixels);
                        if (r.Original != null) set.Add(r.Original);
                    }
            }
        return set;
    }

    /// <summary>Every distinct traced vector art this document references.</summary>
    public HashSet<VectorArt> VectorArts()
    {
        var set = new HashSet<VectorArt>(ReferenceEqualityComparer.Instance);
        foreach (var l in Layers)
            foreach (var c in l.Cels.Values)
                foreach (var o in c.Objects)
                    if (o is RasterObject { Vector: { } v }) set.Add(v);
        return set;
    }

    /// <summary>Composite one frame into a new bitmap.</summary>
    public Bitmap RenderFrame(int frame, int? skipLayer = null) =>
        ImageOps.FromPixels(Width, Height, RenderPixels(frame, skipLayer));

    /// <summary>
    /// Composite one frame to straight-alpha BGRA, in bytes rather than through
    /// GDI+, so a pixel drawn on one layer at full opacity comes out exactly as
    /// drawn — partial alpha included. GDI+ altered most semi-transparent pixels.
    /// </summary>
    public byte[] RenderPixels(int frame, int? skipLayer = null)
    {
        var target = new byte[Width * Height * 4];
        for (int i = 0; i < Layers.Count; i++)
        {
            var l = Layers[i];
            if (!l.Visible || i == skipLayer) continue;
            var cel = l.CelAt(frame);
            if (cel == null) continue;

            var mask = ActiveMask(l, cel);
            if (l.Blend == BlendMode.Normal && mask == null)
            {
                DrawCel(target, cel, l.Opacity);
                continue;
            }
            // Masked or non-normal layers are flattened first, then masked and combined.
            var layer = new byte[target.Length];
            DrawCel(layer, cel, l.Opacity);
            if (mask != null) ApplyMask(layer, ImageOps.GetPixels(mask.Pixels));
            Blending.Layer(target, layer, l.Blend, Width, Height);
        }
        return target;
    }

    /// <summary>The cel's mask if it is enabled and fits the canvas (a stale size is ignored, never an error).</summary>
    private RasterObject? ActiveMask(Layer l, Cel cel) =>
        l.MaskEnabled && cel.Mask is { } m && m.Width == Width && m.Height == Height ? m : null;

    /// <summary>A new mask: the old one placed at dx,dy on a canvas of the new size, with the uncovered area revealed (white).</summary>
    public static RasterObject ReframeMask(RasterObject m, int w, int h, int dx, int dy)
    {
        var px = ImageOps.GetPixels(ImageOps.Reframe(m.Pixels, w, h, dx, dy));
        for (int i = 0; i < px.Length; i += 4)
            if (px[i + 3] == 0) { px[i] = px[i + 1] = px[i + 2] = px[i + 3] = 255; }
        return m.With(ImageOps.FromPixels(w, h, px));
    }

    /// <summary>
    /// Replace every cel's mask with <paramref name="f"/>'s result (null keeps it). Used by whole-canvas operations so masks follow
    /// the pixels; masks are always replaced by new bitmaps, never changed.
    /// </summary>
    public void MapMasks(Func<RasterObject, Bitmap?> f)
    {
        foreach (var l in Layers)
            foreach (var c in l.Cels.Values)
                if (c.Mask is { } m && f(m) is { } b) c.Mask = m.With(b);
    }

    /// <summary>Multiply alpha by the mask's grey (its green channel times its alpha).</summary>
    public static void ApplyMask(byte[] px, byte[] mask)
    {
        for (int i = 0; i < px.Length; i += 4)
        {
            int m = mask[i + 1] * mask[i + 3] / 255;
            if (m < 255) px[i + 3] = (byte)((px[i + 3] * m + 127) / 255);
        }
    }

    private void DrawCel(byte[] target, Cel cel, byte layerOpacity)
    {
        foreach (var o in cel.Objects)
        {
            if (o is not RasterObject r || !r.Visible) continue;
            int opacity = r.Opacity * layerOpacity / 255;
            var px = ImageOps.GetPixels(r.Pixels);
            if (r.Blend == BlendMode.Normal) ImageOps.Over(target, Width, Height, px, r.Width, r.Height, r.X, r.Y, opacity);
            else Blending.Over(target, Width, Height, px, r.Width, r.Height, r.X, r.Y, opacity, r.Blend);
        }
    }

    public Document Clone()
    {
        var d = new Document { Width = Width, Height = Height };
        foreach (var f in Frames) d.Frames.Add(f.Clone());
        foreach (var l in Layers) d.Layers.Add(l.Clone());
        foreach (var t in Tags) d.Tags.Add(t.Clone());
        d.Audio = Audio.Clone();
        d.Library = Library.Select(i => new LibraryItem(i.Name, ImageOps.Clone(i.Pixels), i.Source)).ToList();
        return d;
    }

    /// <summary>Rough bytes held by raster data, for the memory indicator.</summary>
    public long ApproximateBytes()
    {
        long n = 0;
        foreach (var l in Layers)
            foreach (var c in l.Cels.Values)
            {
                if (c.Mask != null) n += (long)c.Mask.Width * c.Mask.Height * 4;
                foreach (var o in c.Objects)
                    if (o is RasterObject r) n += (long)r.Width * r.Height * 4;
            }
        return n;
    }

    public void Dispose()
    {
        foreach (var l in Layers)
            foreach (var c in l.Cels.Values)
            {
                c.Mask?.Pixels.Dispose();
                foreach (var o in c.Objects)
                    if (o is RasterObject r) r.Pixels.Dispose();
            }
    }
}
