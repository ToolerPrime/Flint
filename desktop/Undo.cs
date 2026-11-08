/* Undo/redo that stores deltas rather than whole-document snapshots.
 *
 * The old editor cloned every frame for each undo step, so a 20-frame project
 * cost 20 full bitmaps per level. Here a paint stroke stores only the pixels
 * inside its dirty rectangle, which for a typical brush stroke is a few KB.
 * Whole-document operations (scale, trim, canvas resize) genuinely change
 * everything, so those still snapshot — and say so, via Bytes.
 *
 * The stack is bounded by a byte budget, not a step count: small edits give
 * you hundreds of levels, expensive ones fewer.
 */
namespace Flint;

public interface IEdit
{
    string Label { get; }
    long Bytes { get; }
    void Undo(Document doc);
    void Redo(Document doc);
}

/// <summary>Pixels inside one rectangle of one raster object.</summary>
public sealed class PixelEdit : IEdit
{
    private readonly int _layer, _frameKey, _objIndex;
    private readonly Rectangle _rect;
    private readonly byte[] _before, _after;

    public string Label { get; }
    public long Bytes => _before.LongLength + _after.LongLength;

    public PixelEdit(string label, int layer, int frameKey, int objIndex,
                     Rectangle rect, byte[] before, byte[] after)
    {
        Label = label;
        _layer = layer; _frameKey = frameKey; _objIndex = objIndex;
        _rect = rect; _before = before; _after = after;
    }

    private RasterObject? Target(Document doc)
    {
        if (_layer < 0 || _layer >= doc.Layers.Count) return null;
        if (!doc.Layers[_layer].Cels.TryGetValue(_frameKey, out var cel)) return null;
        if (_objIndex == Cel.MaskIndex) return cel.Mask;
        if (_objIndex < 0 || _objIndex >= cel.Objects.Count) return null;
        return cel.Objects[_objIndex] as RasterObject;
    }

    public void Undo(Document doc) => Target(doc)?.Let(r => ImageOps.SetRegion(r.Pixels, _rect, _before));
    public void Redo(Document doc) => Target(doc)?.Let(r => ImageOps.SetRegion(r.Pixels, _rect, _after));
}

internal static class LetExtension
{
    public static void Let<T>(this T self, Action<T> action) where T : class => action(self);
}

/// <summary>
/// Anything that restructures the document: layers, frames, tags, transforms.
/// Holds two snapshots that share pixel objects with each other and with the
/// live document, so adding a layer or a frame costs almost nothing. Its cost
/// is only the pixels the before-state holds that the after-state does not —
/// for example the original images that a flip replaced.
/// </summary>
public sealed class StructureEdit : IEdit
{
    private readonly Document _before, _after;

    public string Label { get; }
    public long Bytes { get; }

    public StructureEdit(string label, Document before, Document after)
    {
        Label = label; _before = before; _after = after;
        var kept = after.RasterBitmaps();
        long unique = 0;
        foreach (var b in before.RasterBitmaps())
            if (!kept.Contains(b)) unique += (long)b.Width * b.Height * 4;
        var keptArt = after.VectorArts();
        foreach (var v in before.VectorArts())
            if (!keptArt.Contains(v)) unique += v.PointCount * 16L;
        Bytes = 512 + unique;
    }

    public void Undo(Document doc) => Restore(doc, _before);
    public void Redo(Document doc) => Restore(doc, _after);

    private static void Restore(Document doc, Document from)
    {
        // No Dispose: the objects being replaced may live on in other snapshots.
        doc.Layers.Clear();
        doc.Frames.Clear();
        doc.Tags.Clear();
        // A fresh snapshot, so later edits to the live containers never reach
        // back into the stored one.
        var copy = from.Snapshot();
        doc.Width = copy.Width;
        doc.Height = copy.Height;
        doc.Layers.AddRange(copy.Layers);
        doc.Frames.AddRange(copy.Frames);
        doc.Tags.AddRange(copy.Tags);
        doc.Audio = copy.Audio;
    }
}

/// <summary>
/// Any change to tracks or clips. Audio sources are immutable and shared
/// between clones, so this costs only the clip lists, not the samples.
/// </summary>
public sealed class AudioEdit : IEdit
{
    private readonly AudioProject _before, _after;
    public string Label { get; }
    public long Bytes { get; }

    public AudioEdit(string label, AudioProject before, AudioProject after, long newSampleBytes = 0)
    {
        Label = label; _before = before; _after = after;
        Bytes = 256 + newSampleBytes;
    }

    public void Undo(Document doc) => doc.Audio = _before.Clone();
    public void Redo(Document doc) => doc.Audio = _after.Clone();
}

/// <summary>A property change small enough to store directly.</summary>
public sealed class PropertyEdit : IEdit
{
    private readonly Action<Document> _undo, _redo;
    public string Label { get; }
    public long Bytes => 64;

    public PropertyEdit(string label, Action<Document> undo, Action<Document> redo)
    {
        Label = label; _undo = undo; _redo = redo;
    }

    public void Undo(Document doc) => _undo(doc);
    public void Redo(Document doc) => _redo(doc);
}

public sealed class UndoStack
{
    private readonly List<IEdit> _done = new();
    private readonly List<IEdit> _undone = new();

    /// <summary>Budget in bytes. Oldest edits are dropped when exceeded.</summary>
    public long Budget = 512L * 1024 * 1024;

    public long Used { get; private set; }
    public int DoneCount => _done.Count;
    public int UndoneCount => _undone.Count;
    public bool CanUndo => _done.Count > 0;
    public bool CanRedo => _undone.Count > 0;

    public IEnumerable<string> Labels => _done.Select(e => e.Label);

    /// <summary>Steps that can be redone, in the order they were first made.</summary>
    public IEnumerable<string> RedoLabels => Enumerable.Reverse(_undone).Select(e => e.Label);

    /// <summary>Raised after a new step is pushed, so lists of steps can refresh.</summary>
    public event Action? Pushed;

    public void Push(IEdit edit)
    {
        _done.Add(edit);
        Used += edit.Bytes;
        foreach (var e in _undone) Used -= e.Bytes;
        _undone.Clear();
        Trim();
        Pushed?.Invoke();
    }

    private void Trim()
    {
        // Always keep at least one level, however large it is: dropping to
        // zero would make an expensive operation silently unundoable.
        while (Used > Budget && _done.Count > 1)
        {
            Used -= _done[0].Bytes;
            _done.RemoveAt(0);
        }
    }

    public string? Undo(Document doc)
    {
        if (_done.Count == 0) return null;
        var e = _done[^1];
        _done.RemoveAt(_done.Count - 1);
        e.Undo(doc);
        _undone.Add(e);
        return e.Label;
    }

    public string? Redo(Document doc)
    {
        if (_undone.Count == 0) return null;
        var e = _undone[^1];
        _undone.RemoveAt(_undone.Count - 1);
        e.Redo(doc);
        _done.Add(e);
        return e.Label;
    }

    public void Clear()
    {
        _done.Clear();
        _undone.Clear();
        Used = 0;
        // Dropped snapshots may hold the only reference to large bitmaps whose
        // memory is unmanaged; collect so it is actually returned.
        GC.Collect();
        GC.WaitForPendingFinalizers();
    }
}

/// <summary>
/// Records a paint operation: snapshot the target before, let the caller draw,
/// then diff. The dirty rectangle is measured, not assumed, so a stroke in one
/// corner of a large canvas stores only that corner.
/// </summary>
public sealed class PaintRecorder
{
    private readonly Document _doc;
    private readonly int _layer, _frameKey, _objIndex;
    private readonly RasterObject _target;
    private readonly byte[] _before;

    public PaintRecorder(Document doc, int layerIndex, int frameKey, RasterObject target)
    {
        _doc = doc;
        _layer = layerIndex;
        _frameKey = frameKey;
        _target = target;
        var cel = doc.Layers[layerIndex].Cels[frameKey];
        _objIndex = ReferenceEquals(cel.Mask, target) ? Cel.MaskIndex : cel.Objects.IndexOf(target);
        _before = ImageOps.GetPixels(target.Pixels);
    }

    /// <summary>Null when nothing actually changed, so no-op strokes add no history.</summary>
    public PixelEdit? Finish(string label)
    {
        var after = ImageOps.GetPixels(_target.Pixels);
        var rect = DirtyRect(_before, after, _target.Width, _target.Height);
        if (rect.IsEmpty) return null;
        return new PixelEdit(label, _layer, _frameKey, _objIndex, rect,
            ImageOps.CropRegion(_before, _target.Width, rect),
            ImageOps.CropRegion(after, _target.Width, rect));
    }

    private static Rectangle DirtyRect(byte[] a, byte[] b, int w, int h)
    {
        int minX = w, minY = h, maxX = -1, maxY = -1;
        for (int y = 0; y < h; y++)
        {
            int row = y * w * 4;
            for (int x = 0; x < w; x++)
            {
                int o = row + x * 4;
                if (a[o] == b[o] && a[o + 1] == b[o + 1]
                    && a[o + 2] == b[o + 2] && a[o + 3] == b[o + 3]) continue;
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }
        }
        if (maxX < 0) return Rectangle.Empty;
        return new Rectangle(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }
}
