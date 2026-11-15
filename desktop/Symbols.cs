/* Symbols (Fireworks graphic/animation symbols, Animate graphic symbols).
 *
 * A master is a small document of its own (frames, layers, cels) kept in
 * Document.Symbols. It is never changed once stored: editing a symbol works
 * on a clone and stores a new master. An instance is an ordinary
 * RasterObject whose Symbol says which master and how it plays; its Original
 * is the master frame it shows and its Matrix places it, so rendering,
 * exports, onion skin and transforms work on it like any placed image.
 * Instance pixels are rebuilt from the master at explicit points (leaving
 * symbol edit, changing options, copying across frames, loading a file). */
using System.Runtime.CompilerServices;

namespace Flint;

public enum SymbolPlay { Loop, Once, Single }

/// <summary>
/// Which master frame an instance shows. Offset is how many frames after its
/// start this copy sits; it travels with the object through frame moves.
/// </summary>
public sealed record SymbolRef(string Id, SymbolPlay Play = SymbolPlay.Loop, int First = 0, int Offset = 0)
{
    public int FrameIn(int n)
    {
        if (n <= 0) return 0;
        return Play switch
        {
            SymbolPlay.Single => Math.Clamp(First, 0, n - 1),
            SymbolPlay.Once => (int)Math.Clamp((long)First + Offset, 0, n - 1),
            _ => (int)((((long)First + Offset) % n + n) % n),
        };
    }
}

/// <summary>A symbol master. Content is never mutated once stored.</summary>
public sealed record SymbolMaster(string Id, string Name, Document Content);

public static class SymbolOps
{
    // One rendered bitmap per master frame, keyed by the (immutable) content; freed with it.
    private static readonly ConditionalWeakTable<Document, Bitmap?[]> Cache = new();

    public static Bitmap FrameBitmap(SymbolMaster m, int frame)
    {
        var c = m.Content;
        var arr = Cache.GetValue(c, d => new Bitmap?[Math.Max(1, d.FrameCount)]);
        frame = Math.Clamp(frame, 0, arr.Length - 1);
        lock (arr) return arr[frame] ??= ImageOps.FromPixels(c.Width, c.Height, c.RenderPixels(frame));
    }

    public static SymbolMaster? Find(Document doc, string id) => doc.Symbols.FirstOrDefault(s => s.Id == id);

    public static int CountInstances(Document doc, string id) =>
        doc.Layers.Sum(l => l.Cels.Values.Sum(c => c.Objects.Count(o => o is RasterObject { Symbol: { } s } && s.Id == id)));

    /// <summary>A new instance showing <paramref name="s"/>'s frame, keeping <paramref name="like"/>'s name, look and placement.</summary>
    public static RasterObject Make(SymbolMaster m, SymbolRef s, RasterObject like)
    {
        var orig = FrameBitmap(m, s.FrameIn(m.Content.FrameCount));
        var seed = new RasterObject(orig)
        {
            Name = like.Name, Visible = like.Visible, Opacity = like.Opacity, Blend = like.Blend,
            Original = orig, Matrix = like.Matrix, Mode = like.Mode, GroupPath = like.GroupPath, Symbol = s,
            X = (int)like.Matrix.Tx, Y = (int)like.Matrix.Ty,
        };
        // Whole-pixel placement without effects shows the cached frame itself: copies share one bitmap.
        if (like.Matrix.IsIntegerTranslation && like.Effects is not { Count: > 0 }) return seed;
        return seed.WithEffectList(like.Effects);           // re-rendered from the frame, effects applied
    }

    /// <summary>A fresh instance placed with <paramref name="matrix"/>: loops from its first frame.</summary>
    public static RasterObject Create(SymbolMaster m, Affine matrix, string name) =>
        Make(m, new SymbolRef(m.Id), new RasterObject(FrameBitmap(m, 0)) { Name = name, Matrix = matrix });

    /// <summary>Rebuild every instance of one symbol (after its master changed). Returns how many.</summary>
    public static int Refresh(Document doc, string id)
    {
        var m = Find(doc, id);
        return m == null ? 0 : doc.MapRasters(r => r.Symbol?.Id == id ? Make(m, r.Symbol, r) : null);
    }

    /// <summary>The same instance <paramref name="by"/> frames later in its own timeline.</summary>
    /// <remarks>
    /// The offset always moves on, even for a one-frame or single-frame symbol, so frames added to
    /// the symbol later play, and runs of copies stay recognisable. Always a new object.
    /// </remarks>
    public static RasterObject Advance(Document doc, RasterObject o, int by)
    {
        if (o.Symbol is not { } s) return o;
        if (Find(doc, s.Id) is not { } m) return (RasterObject)o.CloneObject();
        return Make(m, s with { Offset = s.Offset + by }, o);
    }

    /// <summary>Advance every instance in a cel copy that is not yet in any undo snapshot.</summary>
    public static void AdvanceCel(Document doc, Cel freshCopy, int by)
    {
        for (int i = 0; i < freshCopy.Objects.Count; i++)
            if (freshCopy.Objects[i] is RasterObject { Symbol: not null } r) freshCopy.Objects[i] = Advance(doc, r, by);
    }
}
