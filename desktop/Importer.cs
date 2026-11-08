/* Image import: decoding, placement, and optional palette conversion.
 *
 * By default nothing about the pixels changes: size, transparency and every
 * colour are kept, and a larger image grows the canvas rather than being
 * shrunk. Embedded colour profiles are not applied — the stored values are
 * kept, and the user is told — so a sprite's colours never shift on the way in.
 */
using System.Drawing.Imaging;

namespace Flint;

public enum ImportMode { Frames, Layers, OneImage, SheetAuto, SheetGrid }

public sealed class ImportOptions
{
    public ImportMode Mode = ImportMode.Frames;
    /// <summary>Keep size, transparency and colour exactly. On by default.</summary>
    public bool PreservePixels = true;
    /// <summary>Only when not preserving: shrink to fit the canvas.</summary>
    public bool FitToCanvas;
    public bool SmoothScaling;
    /// <summary>Map colours to the project's palette. Off by default; lossy.</summary>
    public bool ConvertToPalette;
    public int PaletteColours = 32;
    public bool Dither;

    public int GapPixels = 2;
    public SliceAnchor Anchor = SliceAnchor.BottomCentre;
    public int CellW = 32, CellH = 32, Margin, Spacing;
}

public sealed class ImportedFile : IDisposable
{
    public string Path = "";
    public string Name => System.IO.Path.GetFileName(Path);
    public readonly List<(Bitmap bmp, int delay)> Frames = new();
    public bool Lossy;            // JPEG and other lossy formats
    public bool HasProfile;       // an embedded ICC profile we deliberately did not apply
    /// <summary>A Fireworks document: a PNG carrying layers and objects in private chunks.</summary>
    public bool IsFireworksDocument;
    public string Format = "";
    public int Width => Frames.Count > 0 ? Frames[0].bmp.Width : 0;
    public int Height => Frames.Count > 0 ? Frames[0].bmp.Height : 0;

    public void Dispose() { foreach (var f in Frames) f.bmp.Dispose(); }
}

public static class Importer
{
    /// <summary>Extensions Flint reads as images.</summary>
    public static bool IsImage(string path) =>
        System.IO.Path.GetExtension(path).ToLowerInvariant() is ".png" or ".gif" or ".jpg" or ".jpeg" or ".jfif" or ".bmp" or ".tif" or ".tiff";

    public static ImportedFile Load(string path)
    {
        var f = new ImportedFile { Path = path };
        string ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
        f.Lossy = ext is ".jpg" or ".jpeg" or ".jfif";
        if (ext == ".png") f.IsFireworksDocument = HasFireworksChunks(path);
        // useEmbeddedColorManagement: false keeps stored values as they are.
        using var img = Image.FromFile(path, false);
        f.Format = new ImageFormatConverter().ConvertToString(img.RawFormat) ?? ext.TrimStart('.').ToUpperInvariant();
        try { f.HasProfile = img.PropertyIdList.Contains(0x8773); } catch { }

        var dims = img.FrameDimensionsList.Length > 0 ? new FrameDimension(img.FrameDimensionsList[0]) : FrameDimension.Page;
        int count;
        try { count = img.GetFrameCount(dims); } catch { count = 1; }
        var delays = ReadDelays(img, count);
        for (int i = 0; i < count; i++)
        {
            if (count > 1) img.SelectActiveFrame(dims, i);
            f.Frames.Add((ImageOps.ReadExact(img), delays[i]));
        }
        return f;
    }

    /// <summary>
    /// Fireworks saved its editable document inside the PNG as private chunks
    /// (mkTS for the object tree, mkBT for bitmap tiles). Only the chunk list
    /// is read, not the data, so this is cheap even for large files.
    /// </summary>
    public static bool HasFireworksChunks(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            var head = new byte[8];
            if (fs.Read(head, 0, 8) != 8 || head[1] != 'P' || head[2] != 'N' || head[3] != 'G') return false;
            var buf = new byte[8];
            while (fs.Read(buf, 0, 8) == 8)
            {
                int len = (buf[0] << 24) | (buf[1] << 16) | (buf[2] << 8) | buf[3];
                string type = System.Text.Encoding.ASCII.GetString(buf, 4, 4);
                if (type is "mkTS" or "mkBS" or "mkBF" or "mkBT") return true;
                if (type == "IEND" || len < 0) return false;
                fs.Seek(len + 4, SeekOrigin.Current);   // data and CRC
            }
        }
        catch { }
        return false;
    }

    /// <summary>GIF per-frame delays live in property 0x5100, in centiseconds.</summary>
    private static int[] ReadDelays(Image img, int count)
    {
        var delays = Enumerable.Repeat(100, count).ToArray();
        try
        {
            var pi = img.GetPropertyItem(0x5100);
            if (pi?.Value == null) return delays;
            for (int i = 0; i < count && (i + 1) * 4 <= pi.Value.Length; i++)
            {
                int cs = BitConverter.ToInt32(pi.Value, i * 4);
                delays[i] = cs <= 0 ? 100 : cs * 10;
            }
        }
        catch { /* not an animation */ }
        return delays;
    }

    /// <summary>Plain-English notes about what import will and will not do.</summary>
    public static List<string> Notes(IEnumerable<ImportedFile> files, ImportOptions o)
    {
        var notes = new List<string>();
        var list = files.ToList();
        foreach (var fw in list.Where(f => f.IsFireworksDocument))
            notes.Add($"{fw.Name} is a Fireworks document. Importing places only the flattened picture Fireworks saved inside it. " +
                      "To get its layers and objects, use File > Open (or drop the file on the window). Your original file is not changed.");
        if (list.Any(f => f.Lossy))
            notes.Add("JPEG is lossy. Flint keeps its pixels exactly as decoded, but cannot restore detail lost when it was saved as JPEG.");
        if (list.Any(f => f.HasProfile))
            notes.Add("An image has an embedded colour profile. Flint keeps the stored colour values and does not convert them, so a colour-managed viewer may show it slightly differently.");
        if (o.Mode == ImportMode.SheetAuto && list.Count > 0
            && list[0].Frames.Count > 0 && list[0].Frames[0].bmp is var sb
            && Sprites.SolidBackground(ImageOps.GetPixels(sb), sb.Width, sb.Height) is { } bg)
            notes.Add($"This sheet has a solid background (#{(bg >> 16) & 0xFF:X2}{(bg >> 8) & 0xFF:X2}{bg & 0xFF:X2}). " +
                      "Sprites are found by ignoring it, but it is kept in each frame. Use Background removal to make it transparent.");
        if (o.ConvertToPalette)
            notes.Add($"Palette conversion changes colours to the nearest of {o.PaletteColours}. Transparency is kept. This cannot be undone except with Undo.");
        if (!o.PreservePixels && o.FitToCanvas)
            notes.Add(o.SmoothScaling
                ? "Smooth scaling blends pixels; hard pixel-art edges will soften."
                : "Images larger than the canvas are shrunk with nearest-neighbour; detail is dropped, no new colours appear.");
        return notes;
    }

    /// <summary>The palette to convert to: the document's own colours, or, for an empty document, the imports'.</summary>
    public static Palette ProjectPalette(Document doc, IEnumerable<ImportedFile> files, int colours)
    {
        var sources = new List<byte[]>();
        for (int f = 0; f < doc.FrameCount; f++) sources.Add(doc.RenderPixels(f));
        if (!sources.Any(HasOpaque))
        {
            sources.Clear();
            foreach (var file in files)
                foreach (var (bmp, _) in file.Frames) sources.Add(ImageOps.GetPixels(bmp));
        }
        var hist = Quantizer.BuildHistogram(sources, out double total);
        return Quantizer.Quantize(hist, total, Math.Clamp(colours, 2, 256));
    }

    private static bool HasOpaque(byte[] px)
    {
        for (int i = 3; i < px.Length; i += 4) if (px[i] > 0) return true;
        return false;
    }

    /// <summary>Map RGB to the palette, keeping each pixel's alpha. Returns pixels changed.</summary>
    public static int ApplyPalette(byte[] px, int w, int h, Palette pal, bool dither)
    {
        var ix = new Quantizer.Indexer(pal);
        var before = (byte[])px.Clone();
        if (dither)
        {
            var idx = ImageOps.Dither(px, w, h, pal, ix, 0);
            for (int p = 0; p < idx.Length; p++)
            {
                if (px[p * 4 + 3] == 0) continue;
                int c = idx[p];
                px[p * 4] = pal.B[c]; px[p * 4 + 1] = pal.G[c]; px[p * 4 + 2] = pal.R[c];
            }
        }
        else
        {
            for (int p = 0; p < w * h; p++)
            {
                int o = p * 4;
                if (px[o + 3] == 0) continue;
                int c = ix.Nearest(px[o + 2], px[o + 1], px[o]);
                px[o] = pal.B[c]; px[o + 1] = pal.G[c]; px[o + 2] = pal.R[c];
            }
        }
        int changed = 0;
        for (int i = 0; i < px.Length; i += 4)
            if (px[i] != before[i] || px[i + 1] != before[i + 1] || px[i + 2] != before[i + 2]) changed++;
        return changed;
    }

    /// <summary>
    /// Put the imported images into the document. Returns a one-line summary.
    /// Every object added is new, so the caller's undo snapshot stays valid.
    /// </summary>
    public static string Apply(Document doc, int currentFrame, List<ImportedFile> files, ImportOptions o,
                               out int firstFrame, out int layerIndex)
    {
        firstFrame = currentFrame;
        layerIndex = doc.Layers.Count - 1;
        if (files.Count == 0 || files.All(f => f.Frames.Count == 0)) return "nothing to import";

        // A blank document (one empty frame, nothing drawn) is filled from
        // frame 0 and takes the import's size; otherwise frames insert after
        // the current one and the canvas only ever grows.
        bool blank = doc.FrameCount == 1 && doc.ApproximateBytes() == 0;

        // Gather the list of images to place, one per output frame.
        var images = new List<(byte[] px, int w, int h, int delay, string name)>();
        if (o.Mode is ImportMode.SheetAuto or ImportMode.SheetGrid)
        {
            var src = files[0].Frames[0].bmp;
            var px = ImageOps.GetPixels(src);
            var rects = o.Mode == ImportMode.SheetAuto
                ? Sprites.Auto(px, src.Width, src.Height, joinGap: o.GapPixels)
                : Sprites.Grid(src.Width, src.Height, o.CellW, o.CellH, o.Margin, o.Spacing)
                      .Where(r => Sprites.ContentRect(ImageOps.GetPixels(ImageOps.Crop(src, r)), r.Width, r.Height) != Rectangle.Empty)
                      .ToList();
            if (rects.Count == 0) return "no sprites found on that sheet";
            if (o.Mode == ImportMode.SheetGrid)
            {
                // A grid keeps its cells exactly, no re-anchoring.
                foreach (var r in rects)
                    images.Add((ImageOps.GetPixels(ImageOps.Crop(src, r)), r.Width, r.Height, 100, files[0].Name));
            }
            else
            {
                var (fw, fh, cut) = Sprites.Cut(px, src.Width, src.Height, rects, o.Anchor);
                foreach (var c in cut) images.Add((c, fw, fh, 100, files[0].Name));
            }
        }
        else if (o.Mode == ImportMode.OneImage)
        {
            var b = files[0].Frames[0].bmp;
            images.Add((ImageOps.GetPixels(b), b.Width, b.Height, files[0].Frames[0].delay, files[0].Name));
        }
        else
        {
            foreach (var f in files)
                foreach (var (bmp, delay) in f.Frames)
                    images.Add((ImageOps.GetPixels(bmp), bmp.Width, bmp.Height, delay, f.Name));
        }

        // Size: preserve grows the canvas; fit shrinks the images.
        int maxW = images.Max(i => i.w), maxH = images.Max(i => i.h);
        if (blank) { doc.Resize(maxW, maxH, 0, 0); }
        else if (o.PreservePixels || !o.FitToCanvas)
        {
            if (maxW > doc.Width || maxH > doc.Height)
                doc.Resize(Math.Max(doc.Width, maxW), Math.Max(doc.Height, maxH), 0, 0);
        }
        else
        {
            for (int i = 0; i < images.Count; i++)
            {
                var (px, w, h, delay, name) = images[i];
                if (w <= doc.Width && h <= doc.Height) continue;
                double s = Math.Min(doc.Width / (double)w, doc.Height / (double)h);
                int nw = Math.Max(1, (int)(w * s)), nh = Math.Max(1, (int)(h * s));
                var scaled = ImageOps.Scale(ImageOps.FromPixels(w, h, px), nw, nh, o.SmoothScaling);
                images[i] = (ImageOps.GetPixels(scaled), nw, nh, delay, name);
            }
        }

        int changedPixels = 0;
        if (o.ConvertToPalette)
        {
            var pal = ProjectPalette(doc, files, o.PaletteColours);
            foreach (var im in images) changedPixels += ApplyPalette(im.px, im.w, im.h, pal, o.Dither);
        }

        var layer = new Layer { Name = System.IO.Path.GetFileNameWithoutExtension(files[0].Path) };
        doc.Layers.Add(layer);
        layerIndex = doc.Layers.Count - 1;

        bool asFrames = o.Mode is ImportMode.Frames or ImportMode.SheetAuto or ImportMode.SheetGrid;
        if (o.Mode == ImportMode.Layers)
        {
            // Each file on its own layer, its frames from the current frame on.
            doc.Layers.RemoveAt(layerIndex);
            int idx = 0;
            foreach (var f in files)
            {
                var l = new Layer { Name = System.IO.Path.GetFileNameWithoutExtension(f.Path) };
                doc.Layers.Add(l);
                for (int k = 0; k < f.Frames.Count; k++, idx++)
                {
                    int frame = currentFrame + k;
                    while (doc.FrameCount <= frame) doc.InsertFrame(doc.FrameCount);
                    var im = images[idx];
                    l.EnsureCel(frame).Objects.Add(new RasterObject(ImageOps.FromPixels(im.w, im.h, im.px)) { Name = im.name });
                }
            }
            layerIndex = doc.Layers.Count - 1;
            firstFrame = currentFrame;
            return $"imported {files.Count} image(s) as layers";
        }

        if (!asFrames)
        {
            var im = images[0];
            layer.EnsureCel(currentFrame).Objects.Add(new RasterObject(ImageOps.FromPixels(im.w, im.h, im.px)) { Name = im.name });
            return $"imported {im.name} as a new layer";
        }

        // Frames: fill from 0 in a blank document, else insert after the current frame.
        int at = blank ? 0 : currentFrame + 1;
        firstFrame = at;
        for (int i = 0; i < images.Count; i++)
        {
            int frame = at + i;
            if (blank && frame == 0) doc.Frames[0].Delay = images[0].delay;
            else doc.InsertFrame(frame, new FrameSpec { Delay = images[i].delay });
            var im = images[i];
            layer.EnsureCel(frame).Objects.Add(new RasterObject(ImageOps.FromPixels(im.w, im.h, im.px)) { Name = im.name });
        }
        string palNote = o.ConvertToPalette ? $"; {changedPixels} pixels changed to the palette" : "";
        return $"imported {images.Count} frame(s){palNote}";
    }
}
