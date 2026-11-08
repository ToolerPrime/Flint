/* Open a Fireworks 8 editable PNG as a Flint document.
 *
 * A Fireworks PNG is an ordinary PNG (the flattened picture) plus private
 * chunks: mkTS holds the document as a tag tree, mkBT holds 128x128 pixel
 * tiles. What is known about the format, and how each point was checked, is
 * in docs/FIREWORKS_PNG_FORMAT.md. Nothing here uses Fireworks code or assets.
 *
 * Bitmaps are rebuilt byte for byte. Vector paths become Flint vector objects
 * (filled, antialiased). Things Flint cannot reproduce yet are skipped and
 * listed in Warnings; the flattened picture is always kept on a hidden layer
 * so nothing is lost and the result can be compared.
 */
using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace Flint;

#region tag tree

/// <summary>One node of the Fireworks tag tree: TAG, a type letter, then a value or children.</summary>
public sealed class FwNode
{
    public string Tag = "";
    /// <summary>v list, i hex int, f float, b bool, s string; anything else is kept raw in Value.</summary>
    public char Type;
    public string Value = "";
    public readonly List<FwNode> Children = new();

    public FwNode? Get(string tag)
    {
        foreach (var c in Children) if (c.Tag == tag) return c;
        return null;
    }

    public IEnumerable<FwNode> All(string tag) => Children.Where(c => c.Tag == tag);

    /// <summary>Hex integer; a colour (AARRGGBB) comes back as its 32-bit pattern. A leading '-' is honoured.</summary>
    public int Int(string tag, int def = 0)
    {
        var n = Get(tag);
        if (n == null) return def;
        string s = n.Value.Trim();
        bool neg = s.StartsWith('-');
        if (neg) s = s[1..];
        if (!uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint v)) return def;
        return neg ? -(int)v : (int)v;
    }

    public double Float(string tag, double def = 0) =>
        Get(tag) is { } n && double.TryParse(n.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : def;

    public bool Bool(string tag, bool def = false) => Get(tag) is { } n ? n.Value.Trim() == "1" : def;

    public string Str(string tag, string def = "") => Get(tag)?.Value ?? def;

    /// <summary>Every descendant with this tag, depth first.</summary>
    public IEnumerable<FwNode> Descendants(string tag)
    {
        foreach (var c in Children)
        {
            if (c.Tag == tag) yield return c;
            foreach (var d in c.Descendants(tag)) yield return d;
        }
    }
}

public static class FwParser
{
    private const int MaxDepth = 400;

    /// <summary>Parse the decompressed body of an mkTS chunk. Throws InvalidDataException on malformed data.</summary>
    public static FwNode Parse(byte[] b)
    {
        int i = 0;
        var root = Node(b, ref i, 0);
        return root;
    }

    private static FwNode Node(byte[] b, ref int i, int depth)
    {
        if (depth > MaxDepth) throw new InvalidDataException("Fireworks tree is nested too deeply.");
        if (i + 5 > b.Length) throw new InvalidDataException("Fireworks tree ends in the middle of a tag.");
        var n = new FwNode { Tag = Encoding.ASCII.GetString(b, i, 3), Type = (char)b[i + 3] };
        if (b[i + 4] != '{') throw new InvalidDataException($"Fireworks tree: expected '{{' after {n.Tag}{n.Type} at byte {i + 4}.");
        i += 5;
        switch (n.Type)
        {
            case 'v':
                while (true)
                {
                    if (i >= b.Length) throw new InvalidDataException($"Fireworks tree: list {n.Tag} is never closed.");
                    if (b[i] == '}') { i++; break; }
                    n.Children.Add(Node(b, ref i, depth + 1));
                }
                break;
            case 's':
                // 2-byte big-endian character count, UTF-16BE characters, then '}'. Read by length:
                // the text itself may contain braces.
                if (i + 2 > b.Length) throw new InvalidDataException($"Fireworks tree: string {n.Tag} is cut off.");
                int len = (b[i] << 8) | b[i + 1];
                i += 2;
                if (i + len * 2 + 1 > b.Length || b[i + len * 2] != '}')
                    throw new InvalidDataException($"Fireworks tree: string {n.Tag} has a bad length.");
                n.Value = Encoding.BigEndianUnicode.GetString(b, i, len * 2);
                i += len * 2 + 1;
                break;
            default:   // i, f, b and any type we do not know: text up to the closing brace
                int j = Array.IndexOf(b, (byte)'}', i);
                if (j < 0) throw new InvalidDataException($"Fireworks tree: value of {n.Tag} is never closed.");
                n.Value = Encoding.Latin1.GetString(b, i, j - i);
                i = j + 1;
                break;
        }
        return n;
    }
}

#endregion

#region container

/// <summary>The private chunks of a Fireworks PNG: the document tree and the pixel tiles.</summary>
public sealed class FwContainer
{
    public FwNode Root = new();
    private readonly Dictionary<long, (byte[] data, int offset)> _tiles = new();
    private readonly Dictionary<long, byte[]?> _decoded = new();

    public static FwContainer Read(byte[] file)
    {
        if (file.Length < 8 || file[1] != 'P' || file[2] != 'N' || file[3] != 'G') throw new InvalidDataException("Not a PNG file.");
        var c = new FwContainer();
        byte[]? tree = null;
        int pos = 8;
        while (pos + 8 <= file.Length)
        {
            int len = (file[pos] << 24) | (file[pos + 1] << 16) | (file[pos + 2] << 8) | file[pos + 3];
            string type = Encoding.ASCII.GetString(file, pos + 4, 4);
            int body = pos + 8;
            if (len < 0 || body + len > file.Length) break;   // truncated: use what we have
            if (type == "mkTS" && tree == null) tree = Inflate(file, body, len, 0);
            else if (type == "mkBT" && len >= 77)
            {
                long id = ((long)file[body + 4] << 24) | ((long)file[body + 5] << 16) | ((long)file[body + 6] << 8) | file[body + 7];
                c._tiles[id] = (file[body..(body + len)], 76);
            }
            if (type == "IEND") break;
            pos = body + len + 4;
        }
        if (tree == null) throw new InvalidDataException("This PNG has no Fireworks document data (mkTS chunk).");
        c.Root = FwParser.Parse(tree);
        return c;
    }

    private static byte[]? Inflate(byte[] data, int offset, int len, int skip)
    {
        try
        {
            using var zs = new ZLibStream(new MemoryStream(data, offset + skip, len - skip), CompressionMode.Decompress);
            using var ms = new MemoryStream();
            zs.CopyTo(ms);
            return ms.ToArray();
        }
        catch (InvalidDataException) { return null; }
    }

    public int TileCount => _tiles.Count;

    /// <summary>A colour tile as straight-alpha BGRA, 128x128 (size × size). Null if missing or not a colour tile.</summary>
    public byte[]? Tile(long id, int size)
    {
        if (_decoded.TryGetValue(id, out var cached)) return cached;
        byte[]? bgra = null;
        if (_tiles.TryGetValue(id, out var t) && Inflate(t.data, 0, t.data.Length, t.offset) is { } raw && raw.Length == size * size * 4)
        {
            // Stored A,R,G,B; Flint uses B,G,R,A. Straight alpha (not premultiplied).
            bgra = new byte[raw.Length];
            for (int i = 0; i < raw.Length; i += 4)
            {
                bgra[i] = raw[i + 3]; bgra[i + 1] = raw[i + 2]; bgra[i + 2] = raw[i + 1]; bgra[i + 3] = raw[i];
            }
        }
        _decoded[id] = bgra;
        return bgra;
    }
}

#endregion

#region document builder

public sealed class FwImport
{
    public Document Doc = null!;
    public readonly List<string> Warnings = new();
    public int Layers, Bitmaps, TextImages, Paths, Groups, Frames, SymbolInstances, Effects;
    /// <summary>Fireworks canvas colour (AARRGGBB). Not painted into the document: the Flint canvas stays transparent.</summary>
    public uint CanvasColour;
    public string Summary =>
        $"{Layers} layer{(Layers == 1 ? "" : "s")}, {Frames} frame{(Frames == 1 ? "" : "s")}: " +
        $"{Bitmaps} bitmap{(Bitmaps == 1 ? "" : "s")}, {Paths} vector path{(Paths == 1 ? "" : "s")}, " +
        $"{TextImages} text object{(TextImages == 1 ? "" : "s")} (as pictures), {Groups} group{(Groups == 1 ? "" : "s")}";
}

public static class FireworksPng
{
    public const string ReferenceLayerName = "Fireworks flattened (reference)";

    public static FwImport Open(string path) => Build(File.ReadAllBytes(path));

    public static FwImport Build(byte[] file)
    {
        var fw = FwContainer.Read(file);
        var root = fw.Root;
        if (root.Tag != "MKB") throw new InvalidDataException("Fireworks document does not start with the expected MKB tag.");

        int w = (int)Math.Round(root.Float("WID")), h = (int)Math.Round(root.Float("HIT"));
        if (w <= 0 || h <= 0 || (long)w * h > 400_000_000) throw new InvalidDataException($"Fireworks canvas size {w}x{h} is not usable.");

        var res = new FwImport();
        var doc = new Document(w, h);
        doc.Layers.Clear();
        doc.Frames.Clear();
        res.Doc = doc;
        doc.Guides = GuideSet.FromFireworks(root);
        res.CanvasColour = (uint)root.Int("BGC", unchecked((int)0xFFFFFFFF));

        // Frames: FRC of them. Delays are not in the frames themselves; the export
        // settings list one FNM per frame, with FND in 1/100 s.
        int frames = Math.Max(1, root.Int("FRC", 1));
        var delays = root.Get("EXP")?.Get("FNL")?.All("FNM").Select(f => f.Int("FND", 0)).ToList() ?? new List<int>();
        for (int f = 0; f < frames; f++)
            doc.Frames.Add(new FrameSpec { Delay = f < delays.Count && delays[f] > 0 ? delays[f] * 10 : 100 });
        res.Frames = frames;

        // The flattened picture, exactly as stored, on a hidden locked layer at the bottom.
        using (var ms = new MemoryStream(file))
        using (var img = Image.FromStream(ms, false, false))
        {
            var flat = ImageOps.ReadExact(img);
            var refLayer = doc.AddLayer(ReferenceLayerName);
            refLayer.Visible = false; refLayer.Locked = true; refLayer.Shared = true;
            refLayer.EnsureCel(0).Objects.Add(new RasterObject(flat) { Name = "Flattened picture" });
        }

        var ctx = new Ctx { Fw = fw, Res = res, W = w, H = h };
        foreach (var mst in root.Get("MSL")?.All("MST") ?? Enumerable.Empty<FwNode>())
            ctx.Symbols[mst.Str("MSN").Trim()] = mst;
        var lyl = root.Get("LYL");
        var layers = lyl?.All("LAY").ToList() ?? new List<FwNode>();
        // Fireworks lists layers bottom-most first (verified), which is Flint's order too.
        foreach (var lay in layers)
        {
            if (lay.Bool("URL")) { ctx.WebLayers++; continue; }
            var layer = new Layer { Name = lay.Str("LNM", "Layer"), Shared = lay.Bool("MSL") };
            var vis = lay.Get("VIF")?.All("VIS").Select(v => v.Value.Trim() == "1").ToList() ?? new List<bool>();
            var cells = lay.Get("CLL")?.All("CEL").ToList() ?? new List<FwNode>();
            bool anyVisible = cells.Count == 0 || Enumerable.Range(0, cells.Count).Any(f => f >= vis.Count || vis[f]);
            bool allLocked = cells.Count > 0;
            int hiddenCells = 0;
            for (int f = 0; f < cells.Count && (!layer.Shared || f < 1); f++)
            {
                bool v = f < vis.Count ? vis[f] : true;
                allLocked &= cells[f].Bool("LCK");
                // Flint hides a whole layer, not one frame of it. A layer hidden in some frames loses those
                // cells (noted below); a layer hidden everywhere keeps them and is simply hidden.
                if (!v && anyVisible && !layer.Shared) { hiddenCells++; continue; }
                var elm = cells[f].Get("ELM");
                if (elm == null) continue;
                var cel = new Cel();
                ctx.Layer = layer.Name;
                ctx.Frame = layer.Shared ? 0 : f;
                AddElements(ctx, elm, cel, 1000, Affine.Identity, null);
                if (!cel.IsEmpty) layer.Cels[layer.Shared ? 0 : f] = cel;
            }
            layer.Visible = anyVisible;
            layer.Locked = allLocked;
            if (hiddenCells > 0) res.Warnings.Add($"layer '{layer.Name}' is hidden in {hiddenCells} frame(s); those frames are left empty");
            doc.Layers.Add(layer);
        }
        res.Layers = doc.Layers.Count - 1;
        // Fireworks blends against its canvas colour too (a Screen square on white canvas vanishes outside the object below it),
        // so when any blend mode is used the canvas colour becomes a real bottom layer.
        if (ctx.BlendKnown > 0 && res.CanvasColour >> 24 != 0)
        {
            uint cc = res.CanvasColour;
            var bgpx = new byte[w * h * 4];
            for (int i = 0; i < bgpx.Length; i += 4) { bgpx[i] = (byte)cc; bgpx[i + 1] = (byte)(cc >> 8); bgpx[i + 2] = (byte)(cc >> 16); bgpx[i + 3] = (byte)(cc >> 24); }
            var canvasLayer = new Layer { Name = "Canvas colour (backdrop for blend modes)", Shared = true };
            canvasLayer.EnsureCel(0).Objects.Add(new RasterObject(ImageOps.FromPixels(w, h, bgpx)) { Name = "Canvas colour" });
            doc.Layers.Insert(1, canvasLayer);
            res.Layers = doc.Layers.Count - 1;
        }

        if (ctx.SymMissing > 0) res.Warnings.Add($"{ctx.SymMissing} symbol instance{(ctx.SymMissing == 1 ? "" : "s")} refer to a missing symbol and were skipped");
        if (ctx.SymCycle > 0) res.Warnings.Add($"{ctx.SymCycle} symbol instance{(ctx.SymCycle == 1 ? "" : "s")} skipped (a symbol contains itself, or nests too deeply)");
        if (ctx.FxSkipped.Count > 0) res.Warnings.Add($"{ctx.FxSkipped.Values.Sum()} effect{(ctx.FxSkipped.Values.Sum() == 1 ? "" : "s")} not reproduced ({string.Join(", ", ctx.FxSkipped.Select(kv => kv.Key + " x" + kv.Value))})");
        if (ctx.TextOverrides > 0) res.Warnings.Add($"{ctx.TextOverrides} text label{(ctx.TextOverrides == 1 ? "" : "s")} inside button instances left out (the instance's own label text cannot be drawn yet)");
        if (ctx.FxApprox) res.Warnings.Add("shadows, glows and bevels are approximations of Fireworks' look");
        if (ctx.GradientsFlat > 0) res.Warnings.Add($"{ctx.GradientsFlat} gradient or pattern fill{(ctx.GradientsFlat == 1 ? "" : "s")} of a kind Flint cannot draw yet, drawn as one flat colour");
        if (ctx.BlendUnknown.Count > 0) res.Warnings.Add($"{ctx.BlendUnknown.Values.Sum()} object(s) use a Fireworks blend mode Flint cannot identify yet ({string.Join(", ", ctx.BlendUnknown.Select(kv => "BLD " + kv.Key + " x" + kv.Value))}), drawn as Normal");
        if (ctx.GradientsRare > 0) res.Warnings.Add($"{ctx.GradientsRare} rare gradient kind{(ctx.GradientsRare == 1 ? "" : "s")} approximated (Waves, Satin and Folds only roughly match Fireworks' own)");
        if (ctx.Other.Count > 0) res.Warnings.Add($"{ctx.Other.Values.Sum()} other object(s) not imported ({string.Join(", ", ctx.Other.Select(kv => kv.Key + " x" + kv.Value))})");
        if (ctx.NoImage > 0) res.Warnings.Add($"{ctx.NoImage} text or bitmap object(s) had no picture data and were skipped");
        if (ctx.MissingTiles > 0) res.Warnings.Add($"{ctx.MissingTiles} bitmap tile(s) were missing and left transparent");
        if (ctx.TooBig > 0) res.Warnings.Add($"{ctx.TooBig} path(s) too large to draw were skipped");
        return res;
    }

    private sealed class Ctx
    {
        public FwContainer Fw = null!;
        public FwImport Res = null!;
        public int W, H;
        public string Layer = "";
        public int WebLayers, GradientsFlat, GradientsRare, BlendKnown, NoImage, MissingTiles, TooBig, SymMissing, SymCycle, Frame;
        public bool FxApprox, TextOverridden;
        public string? Label;
        public int TextOverrides;
        public string Prefix = "";
        public readonly Dictionary<string, FwNode> Symbols = new();
        public readonly HashSet<string> Active = new();
        public readonly SortedDictionary<string, int> Other = new();
        public readonly SortedDictionary<string, int> FxSkipped = new();
        public readonly SortedDictionary<string, int> BlendUnknown = new();
    }

    /// <summary>
    /// An ELM list is stored front to back (verified), so it is added in reverse to get back-to-front stacking.
    /// <paramref name="m"/> places the elements: identity on the page, the instance matrix inside a symbol.
    /// <paramref name="inherited"/> are effects of the symbol instance that holds these elements.
    /// </summary>
    private static void AddElements(Ctx c, FwNode elm, Cel cel, int groupOpacity, Affine m, List<ObjectEffect>? inherited)
    {
        var kids = elm.Children.ToList();
        kids.Reverse();
        foreach (var e in kids)
        {
            if (e.Type != 'v') continue;
            int op = groupOpacity;
            // OPA is 0..1000 (1000 = fully opaque, 800 = 80%); verified by comparing scores against the flattened picture.
            if (e.Get("OPA") != null) op = op * Math.Clamp(e.Int("OPA", 1000), 0, 1000) / 1000;
            bool visible = e.Get("VIS") == null || e.Bool("VIS", true);
            int before = cel.Objects.Count;
            var fx = Merge(Effects(c, e), inherited);
            switch (e.Tag)
            {
                case "IMG":
                    if (Bitmap(c, e, "Bitmap", m, fx) is { } bi) { cel.Objects.Add(bi); c.Res.Bitmaps++; }
                    break;
                case "TXT" when c.TextOverridden:
                    // the instance has its own label (BUT); the symbol's cached picture would show the wrong words, so draw the label
                    if (Label(c, e, m, fx) is { } lo) { cel.Objects.Add(lo); c.Res.TextImages++; }
                    else c.TextOverrides++;
                    break;
                case "TXT":
                    {
                        var runs = string.Concat(e.Descendants("TRN").Select(t => t.Value));
                        string name = "Text: " + (runs.Length > 20 ? runs[..20] : runs);
                        // the cached picture of a text already has its effects drawn into it (verified: adding them again lowers the score)
                        if (e.Get("IMG") is { } img && Bitmap(c, img, name, m, inherited) is { } ti) { cel.Objects.Add(ti); c.Res.TextImages++; }
                        else c.NoImage++;
                    }
                    break;
                case "PTH":
                    if (Path(c, e, m, fx) is { } po) { cel.Objects.Add(po); c.Res.Paths++; }
                    break;
                case "GRP":
                    c.Res.Groups++;
                    if (e.Get("EFL") != null) Skip(c, "effects on a group");
                    if (e.Get("ELM") is { } inner) AddElements(c, inner, cel, op, m, inherited);
                    // a group's own opacity is applied through its children, so skip the shared step below
                    for (int i = before; i < cel.Objects.Count; i++) cel.Objects[i].Visible &= visible;
                    continue;
                case "ALS":
                    Instance(c, e, cel, op, m, fx);
                    for (int i = before; i < cel.Objects.Count; i++) cel.Objects[i].Visible &= visible;
                    continue;
                default: c.Other[e.Tag] = c.Other.GetValueOrDefault(e.Tag) + 1; break;
            }
            var blend = BlendOf(c, e);
            for (int i = before; i < cel.Objects.Count; i++)
            {
                cel.Objects[i].Blend = blend;
                cel.Objects[i].Name = c.Prefix + cel.Objects[i].Name;
                cel.Objects[i].Visible = visible;
                cel.Objects[i].Opacity = (byte)((op * 255 + 500) / 1000);
            }
        }
    }

    /// <summary>
    /// An object's blend mode (BLDi, hex), verified against a Fireworks-made file with one square per mode (blends.png):
    /// 2 Multiply, 3 Difference, 5 Screen, 9 Darken, a Lighten, b Hue, c Saturation, d Color, e Luminosity, 11 Invert, 1d Tint,
    /// 19 Erase; absent = Normal. Any other value (the Adobe samples carry 4) is counted, warned about and drawn as Normal.
    /// </summary>
    private static BlendMode BlendOf(Ctx c, FwNode e)
    {
        if (e.Get("BLD") == null || e.Int("BLD") == 0) return BlendMode.Normal;
        int v = e.Int("BLD");
        if (v is 2 or 3 or 5 or 9 or 0xa or 0xb or 0xc or 0xd or 0xe or 0x11 or 0x1d or 0x19) c.BlendKnown++;
        switch (v)
        {
            case 0x2: return BlendMode.Multiply;
            case 0x3: return BlendMode.Difference;
            case 0x5: return BlendMode.Screen;
            case 0x9: return BlendMode.Darken;
            case 0xa: return BlendMode.Lighten;
            case 0xb: return BlendMode.Hue;
            case 0xc: return BlendMode.Saturation;
            case 0xd: return BlendMode.Color;
            case 0xe: return BlendMode.Luminosity;
            case 0x11: return BlendMode.Invert;
            case 0x1d: return BlendMode.Tint;
            case 0x19: return BlendMode.Erase;
        }
        string k = v.ToString("x");
        c.BlendUnknown[k] = c.BlendUnknown.GetValueOrDefault(k) + 1;
        return BlendMode.Normal;
    }

    /// <summary>
    /// A button instance's own label (BUTs), drawn in the style of the symbol's text element: FON font, PTS size (pixels),
    /// BOL/ITL, the fill colour FCL, JST alignment (0 left, 1 centre, 2 right) inside the text box LFT/TOP/RIT/BOT, all in
    /// symbol space and carried by the instance matrix. Rendered smooth (Fireworks' text is antialiased) by TextRender.
    /// </summary>
    private static RasterObject? Label(Ctx c, FwNode e, Affine m, List<ObjectEffect>? fx)
    {
        string text = c.Label ?? "";
        if (text.Trim().Length == 0) return null;
        var style = (e.Bool("BOL") ? FontStyle.Bold : FontStyle.Regular) | (e.Bool("ITL") ? FontStyle.Italic : FontStyle.Regular);
        float size = (float)Math.Max(e.Float("PTS", 12), 1);
        uint col = (uint)(e.Get("PAT")?.Int("FCL", unchecked((int)0xFF000000)) ?? unchecked((int)0xFF000000));
        double left = e.Float("LFT"), top = e.Float("TOP"), right = e.Float("RIT");
        using var font = new Font(e.Str("FON", "Arial"), size, style, GraphicsUnit.Pixel);
        var sz = TextRender.Measure(text, font);
        var px = TextRender.Pixels(text, font, Color.FromArgb((int)col), true, out int w, out int h, true);
        double x = e.Int("JST") switch { 1 => (left + right) / 2 - sz.Width / 2, 2 => right - sz.Width, _ => left };
        double y = top + LabelDy;
        var box = Sprites.ContentRect(px, w, h);
        if (box.IsEmpty) return null;
        return Place(ImageOps.CropRegion(px, w, box), box.Width, box.Height, (int)Math.Round(x) - TextRender.Pad + box.X, (int)Math.Round(y) - TextRender.Pad + box.Y, "Label: " + (text.Length > 20 ? text[..20] : text), m, fx);
    }

    /// <summary>Fireworks puts the line this far above where GDI+ would (calibrated on global.png; the best of the whole-pixel offsets tried).</summary>
    private const double LabelDy = -1;

    private static void Skip(Ctx c, string what) => c.FxSkipped[what] = c.FxSkipped.GetValueOrDefault(what) + 1;

    private static List<ObjectEffect>? Merge(List<ObjectEffect>? own, List<ObjectEffect>? outer) =>
        own == null ? outer : outer == null ? own : own.Concat(outer).ToList();

    #region symbols

    /// <summary>
    /// A symbol instance: the symbol's chosen frame (all visible layers, back to front) drawn through the instance matrix.
    /// The matrix is Fireworks' row-vector form: x' = x*M00 + y*M10 + M20, y' = x*M01 + y*M11 + M21 (translation in M20/M21).
    /// Verified by Halo Lite (symbols with negative local coordinates placed by M20/M21) and global.png. MOX/MOY are 0 in every sample,
    /// so whether they shift the origin is not known; they are ignored.
    /// </summary>
    private static void Instance(Ctx c, FwNode als, Cel cel, int op, Affine outer, List<ObjectEffect>? fx)
    {
        string id = als.Str("MSN").Trim();
        if (!c.Symbols.TryGetValue(id, out var sym)) { c.SymMissing++; return; }
        if (c.Active.Contains(id) || c.Active.Count >= 12) { c.SymCycle++; return; }
        var mtx = als.Get("MTX");
        var im = mtx == null ? Affine.Identity : new Affine(mtx.Float("M00", 1), mtx.Float("M01"), mtx.Float("M10"), mtx.Float("M11", 1), mtx.Float("M20"), mtx.Float("M21"));
        var total = im.Then(outer);
        c.Res.SymbolInstances++;
        // Graphic and button symbols show their first frame (a button's Up state); an animation symbol shows the frame
        // matching the document frame (MSY: absent graphic, 1 button, 2 animation).
        bool anim = sym.Int("MSY") == 2;
        string oldPrefix = c.Prefix;
        c.Prefix += sym.Str("UNM", "Symbol") + ": ";
        c.Active.Add(id);
        bool oldOver = c.TextOverridden;
        string? oldLabel = c.Label;
        if (als.Get("BUT") != null) c.Label = als.Str("BUT");
        c.TextOverridden |= als.Get("BUT") != null;
        try
        {
            foreach (var lay in sym.Get("LYL")?.All("LAY") ?? Enumerable.Empty<FwNode>())
            {
                if (lay.Bool("URL")) continue;
                var cells = lay.Get("CLL")?.All("CEL").ToList() ?? new List<FwNode>();
                if (cells.Count == 0) continue;
                int f = lay.Bool("MSL") ? 0 : Math.Min(anim ? c.Frame : 0, cells.Count - 1);
                var vis = lay.Get("VIF")?.All("VIS").ToList();
                if (vis != null && f < vis.Count && vis[f].Value.Trim() != "1") continue;
                if (cells[f].Get("ELM") is { } elm) AddElements(c, elm, cel, op, total, fx);
            }
        }
        finally { c.Active.Remove(id); c.Prefix = oldPrefix; c.TextOverridden = oldOver; c.Label = oldLabel; }
    }

    #endregion

    #region effects

    private static double Num(Dictionary<string, string> kv, string k, double def = 0) =>
        kv.TryGetValue(k, out var v) && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : def;

    /// <summary>'#rrggbb' or '#rrggbbaa' as BGRA with alpha on top.</summary>
    private static uint Hex(Dictionary<string, string> kv, string k, uint def)
    {
        if (!kv.TryGetValue(k, out var v) || !v.StartsWith('#') || (v.Length != 7 && v.Length != 9)) return def;
        if (!uint.TryParse(v[1..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint n)) return def;
        return v.Length == 7 ? 0xFF000000 | n : (n << 24) | (n >> 8);
    }

    /// <summary>
    /// The live effects of an element (EFL > EPS > DCE key/value pairs), identified by their EffectMoaID. Hidden effects
    /// (EffectIsVisible false) are dropped. Which MoaID is which was worked out from the keys each carries
    /// (see docs/FIREWORKS_PNG_FORMAT.md); anything else is counted as not reproduced.
    /// </summary>
    private static List<ObjectEffect>? Effects(Ctx c, FwNode e)
    {
        List<ObjectEffect>? list = null;
        foreach (var eps in e.All("EFL").SelectMany(efl => efl.All("EPS")))   // one EPS per effect, in order
        {
            var kv = new Dictionary<string, string>();
            foreach (var dce in eps.All("DCE")) kv[dce.Str("DCK")] = dce.Str("DCV");
            if (kv.Count == 0 || kv.GetValueOrDefault("EffectIsVisible") == "false") continue;
            string id = kv.GetValueOrDefault("EffectMoaID", "").TrimStart('{');
            id = id.Length >= 8 ? id[..8].ToLowerInvariant() : id;
            ObjectEffect? fx = null;
            switch (id)
            {
                case "a7944db8":   // Drop Shadow
                    fx = new ObjectEffect(EffectKind.DropShadow, ("dist", Num(kv, "ShadowDistance")), ("angle", Num(kv, "ShadowAngle", 315)),
                        ("blur", Num(kv, "ShadowBlur")), ("colour", Hex(kv, "ShadowColor", 0xFF000000)));
                    c.FxApprox = true;
                    break;
                case "5600f702":   // Inner Shadow
                    fx = new ObjectEffect(EffectKind.InnerShadow, ("dist", Num(kv, "ShadowDistance")), ("angle", Num(kv, "ShadowAngle", 315)),
                        ("blur", Num(kv, "ShadowBlur")), ("colour", Hex(kv, "ShadowColor", 0xFF000000)));
                    c.FxApprox = true;
                    break;
                case "2ba87123":   // Glow
                    fx = new ObjectEffect(EffectKind.Glow, ("width", Num(kv, "GlowWidth")), ("soft", Num(kv, "MaskSoftness")),
                        ("opacity", Num(kv, "BevelContrast", 100)), ("colour", Hex(kv, "OuterBevelColor", 0xFFFFFFFF)));
                    c.FxApprox = true;
                    break;
                case "7fe61102":   // bevel family; BevelType: 0 inner bevel, 1 outer bevel, 2 inset emboss, 3 raised emboss, 4 inner glow
                    {
                        int bt = (int)Num(kv, "BevelType");
                        fx = bt >= 4
                            ? new ObjectEffect(EffectKind.InnerGlow, ("width", Num(kv, "GlowWidth")), ("soft", Num(kv, "MaskSoftness")),
                                ("opacity", Num(kv, "BevelContrast", 100)), ("colour", Hex(kv, "OuterBevelColor", 0xFFFFFFFF)))
                            : new ObjectEffect(EffectKind.Bevel, ("type", bt), ("width", Num(kv, "BevelWidth", 10)), ("contrast", Num(kv, "BevelContrast", 75)),
                                ("light", Num(kv, "LightAngle", 135)), ("hilite", Hex(kv, "HiliteColor", 0xFFFFFFFF)), ("shadow", Hex(kv, "ShadowColor", 0xFF000000)));
                        c.FxApprox = true;
                    }
                    break;
                case "d04ef8c0": fx = new ObjectEffect(EffectKind.GaussianBlur, ("radius", Num(kv, "gaussian_blur_radius"))); break;
                case "f1cfce41": fx = new ObjectEffect(EffectKind.GaussianBlur, ("radius", 1)); break;   // Blur
                case "f1cfce42": fx = new ObjectEffect(EffectKind.GaussianBlur, ("radius", 2)); break;   // Blur More
                case "538016b6": fx = new ObjectEffect(EffectKind.MotionBlur, ("angle", Num(kv, "motion_blur_angle")), ("dist", Num(kv, "motion_blur_distance"))); break;
                case "3439b08d":
                    fx = new ObjectEffect(EffectKind.HueSat, ("hue", Num(kv, "hue_amount")), ("sat", Num(kv, "saturation_amount")),
                        ("light", Num(kv, "lightness_amount")), ("colorize", kv.GetValueOrDefault("hls_colorize") == "true" ? 1 : 0));
                    break;
                case "3439b08c": fx = new ObjectEffect(EffectKind.BrightContrast, ("bright", Num(kv, "brightness_amount")), ("contrast", Num(kv, "contrast_amount"))); break;
                case "dd54adc0" when (int)Num(kv, "Blendmode") == 0:
                    fx = new ObjectEffect(EffectKind.ColorFill, ("colour", Hex(kv, "Color", 0xFF000000)), ("opacity", Num(kv, "Opacity", 100)));
                    break;
            }
            if (fx == null) Skip(c, kv.GetValueOrDefault("name") is { Length: > 0 } n ? n.TrimEnd('.') : "effect " + id);
            else { (list ??= new()).Add(fx); c.Res.Effects++; }
        }
        return list;
    }

    #endregion

    #region bitmaps

    private static RasterObject? Bitmap(Ctx c, FwNode img, string name, Affine m, List<ObjectEffect>? fx)
    {
        int w = img.Int("WPX"), h = img.Int("HPX"), ts = img.Int("TSZ", 128);
        var ids = img.Get("TIL")?.All("TID").Select(t => long.TryParse(t.Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out long v) ? v : -1).ToList();
        if (w <= 0 || h <= 0 || ts <= 0 || (long)w * h > 200_000_000 || ids == null || ids.Count == 0) { c.NoImage++; return null; }

        var px = new byte[w * h * 4];
        int cols = (w + ts - 1) / ts;   // tiles run left to right, then down (verified)
        for (int t = 0; t < ids.Count; t++)
        {
            int tx = (t % cols) * ts, ty = (t / cols) * ts;
            if (ty >= h) break;
            var tile = c.Fw.Tile(ids[t], ts);
            if (tile == null) { c.MissingTiles++; continue; }
            int cw = Math.Min(ts, w - tx);
            for (int y = 0; y < ts && ty + y < h; y++)
                Buffer.BlockCopy(tile, y * ts * 4, px, ((ty + y) * w + tx) * 4, cw * 4);
        }
        string nm = img.Str("OBN") is { Length: > 0 } n && name == "Bitmap" ? n : name;
        int bx = (int)Math.Round(img.Float("XLC") + img.Float("XOF")), by = (int)Math.Round(img.Float("YLC") + img.Float("YOF"));
        return Place(px, w, h, bx, by, nm, m, fx);
    }

    /// <summary>Straight BGRA pixels with their top-left at (bx, by) in local space, placed through <paramref name="m"/> with live effects.</summary>
    private static RasterObject? Place(byte[] px, int w, int h, int bx, int by, string nm, Affine m, List<ObjectEffect>? fx)
    {
        var bmp = ImageOps.FromPixels(w, h, px);
        if (fx == null && m.IsIntegerTranslation)   // the common case: pixels exactly as stored
            return new RasterObject(bmp) { Name = nm, X = bx + (int)m.Tx, Y = by + (int)m.Ty };

        // Placed through a symbol matrix and/or with live effects: keep the picture as the Original so both stay live.
        var next = Affine.Translate(bx, by).Then(m);
        bool pure = next.A == 1 && next.B == 0 && next.C == 0 && next.D == 1;
        var mode = pure ? Resample.Nearest : Resample.Smooth;
        (byte[] px, int x, int y, int w, int h) r;
        if (next.IsIntegerTranslation) r = (px, (int)next.Tx, (int)next.Ty, w, h);
        else
        {
            if (Math.Abs(next.A * next.D - next.B * next.C) < 1e-9) return null;
            var t = Transform.Render(px, w, h, next, mode, () => Array.Empty<byte>());
            r = (t.px, t.x, t.y, t.w, t.h);
        }
        if (fx != null) { var e = ObjectEffects.Apply(fx, r.px, r.w, r.h, r.x, r.y); r = (e.px, e.x, e.y, e.w, e.h); }
        return new RasterObject(ImageOps.FromPixels(r.w, r.h, r.px)) { Name = nm, X = r.x, Y = r.y, Original = bmp, Matrix = next, Mode = mode, Effects = fx };
    }

    #endregion

    #region paths

    private static RasterObject? Path(Ctx c, FwNode pth, Affine m, List<ObjectEffect>? fx)
    {
        var pat = pth.Get("PAT");
        var subs = new List<(List<VPt> pts, bool closed)>();
        foreach (var pbp in pth.Get("PBL")?.All("PBP") ?? Enumerable.Empty<FwNode>())
        {
            var anchors = new List<(VPt a, VPt before, VPt after)>();
            foreach (var pt in pbp.All("PBT"))
            {
                double x = pt.Float("XLC"), y = pt.Float("YLC");
                anchors.Add((new VPt(x, y), new VPt(pt.Float("XPC", x), pt.Float("YPC", y)), new VPt(pt.Float("XSC", x), pt.Float("YSC", y))));
            }
            if (anchors.Count == 0) continue;
            bool closed = pbp.Bool("ISC");
            var poly = new List<VPt> { anchors[0].a };
            int segs = closed ? anchors.Count : anchors.Count - 1;
            for (int i = 0; i < segs; i++)
            {
                var p0 = anchors[i]; var p1 = anchors[(i + 1) % anchors.Count];
                bool lastToFirst = closed && i == anchors.Count - 1;
                Flatten(p0.a, p0.after, p1.before, p1.a, poly, 0);
                if (lastToFirst) poly.RemoveAt(poly.Count - 1);   // it is the first point again
            }
            subs.Add((poly, closed));
        }
        if (subs.Count == 0 || pat == null) return null;

        var shapes = new List<VShape>();
        var fpl = pat.Get("FPL");
        if (fpl != null && fpl.Str("CAT") != "fc_None")
        {
            uint col = (uint)pat.Int("FCL", unchecked((int)0xFF000000));
            VGradient? grad = null;
            if (fpl.Str("CAT") != "fc_Solid")
            {
                grad = Gradient(fpl, pth, col);
                if (grad == null) c.GradientsFlat++;
                else if (grad.Kind is GradientKind.Waves or GradientKind.Satin or GradientKind.Folds) c.GradientsRare++;
                col = GradientAverage(fpl, col);
            }
            var fill = new VShape { Colour = col, Gradient = grad };
            foreach (var (pts, _) in subs) if (pts.Count >= 3) fill.Contours.Add(pts);
            if (fill.Contours.Count > 0) shapes.Add(fill);
        }
        var bpl = pat.Get("BPL");
        if (bpl != null && bpl.Str("CAT") != "bc_None")
        {
            double width = Math.Max(bpl.Int("BDI", 1), 1);   // brush diameter in pixels (verified)
            uint col = (uint)pat.Int("BCL", unchecked((int)0xFF000000));
            foreach (var (pts, closed) in subs) Stroke(pts, closed, width, col, shapes);
        }
        shapes.RemoveAll(s => s.Contours.Count == 0);
        if (shapes.Count == 0) return null;
        if (!m.IsIdentity)   // a symbol instance: the matrix goes into the geometry (strokes were built in local space, so they scale with it)
            foreach (var s in shapes)
            {
                for (int k = 0; k < s.Contours.Count; k++) s.Contours[k] = s.Contours[k].Select(p => { var (x, y) = m.Apply(p.X, p.Y); return new VPt(x, y); }).ToList();
                s.Gradient = s.Gradient?.Mapped(m);
            }

        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var s in shapes) foreach (var ct in s.Contours) foreach (var p in ct)
        {
            minX = Math.Min(minX, p.X); maxX = Math.Max(maxX, p.X); minY = Math.Min(minY, p.Y); maxY = Math.Max(maxY, p.Y);
        }
        int x0 = (int)Math.Floor(minX), y0 = (int)Math.Floor(minY);
        int aw = (int)Math.Ceiling(maxX) - x0, ah = (int)Math.Ceiling(maxY) - y0;
        if (aw <= 0) aw = 1;
        if (ah <= 0) ah = 1;
        if ((long)aw * ah > 16_000_000) { c.TooBig++; return null; }
        foreach (var s in shapes)
            for (int k = 0; k < s.Contours.Count; k++)
                s.Contours[k] = s.Contours[k].Select(p => new VPt(p.X - x0, p.Y - y0)).ToList();
        foreach (var s in shapes) s.Gradient = s.Gradient?.Mapped(Affine.Translate(-x0, -y0));

        var art = new VectorArt { W = aw, H = ah, Shapes = shapes, KeepOrder = true };   // fill first, stroke on top
        var am = Affine.Translate(x0, y0);   // art is kept at its own origin; the matrix places it on the canvas
        var r = Tracer.Render(art, am, true);
        var rr = (r.px, r.x, r.y, r.w, r.h);
        if (fx != null) { var e = ObjectEffects.Apply(fx, r.px, r.w, r.h, r.x, r.y); rr = (e.px, e.x, e.y, e.w, e.h); }
        return new RasterObject(ImageOps.FromPixels(rr.w, rr.h, rr.px))
        {
            Name = pth.Str("OBN", "Path"), X = rr.x, Y = rr.y, Vector = art, Matrix = am, Mode = Resample.Smooth, Effects = fx,
        };
    }

    private static List<(double p, uint c)> Stops(FwNode? list) =>
        list == null ? new() : list.All("FGI").Select(st => (st.Float("FGP"), (uint)st.Int("FGC"))).OrderBy(x => x.Item1).ToList();

    private static uint Ramp(List<(double p, uint c)> st, double t, int shift)
    {
        if (t <= st[0].p) return (st[0].c >> shift) & 255;
        for (int i = 1; i < st.Count; i++)
            if (t <= st[i].p)
            {
                double f = st[i].p - st[i - 1].p < 1e-9 ? 1 : (t - st[i - 1].p) / (st[i].p - st[i - 1].p);
                return (uint)Math.Round(((st[i - 1].c >> shift) & 255) * (1 - f) + ((st[i].c >> shift) & 255) * f);
            }
        return (st[^1].c >> shift) & 255;
    }

    /// <summary>
    /// A real gradient for fc_Linear / fc_Circular / fc_Elliptical and fc_Rectangular / fc_Conical / fc_Bars / fc_Ripple / fc_Pinch (all verified against
    /// Fireworks-made files) and fc_Waves / fc_Satin / fc_Folds (approximated); null for fc_Pattern, fc_ContourGrad etc., which stay flat. Colour stops come
    /// from FGY>FG0 (older files: FGV), opacity stops from FG1 (alpha in the top byte); both are merged into one stop list.
    /// Geometry on the path: PSX,PSY start or centre; PEX,PEY end (linear) or radius end (circular); PFX,PFY second axis (elliptical).
    /// </summary>
    private static VGradient? Gradient(FwNode fpl, FwNode pth, uint fallback)
    {
        // fc_Rectangle .. fc_Folds are mapped to Flint's own kinds of the same names; no sample file uses them, so the caller warns.
        var kind = fpl.Str("CAT") switch
        {
            "fc_Linear" => GradientKind.Linear, "fc_Circular" => GradientKind.Circular, "fc_Elliptical" => GradientKind.Elliptical,
            "fc_Rectangular" => GradientKind.Rectangle, "fc_Conical" => GradientKind.Cone, "fc_Pinch" => GradientKind.Pinch,
            "fc_Bars" => GradientKind.Bars, "fc_Ripple" => GradientKind.Ripples, "fc_Waves" => GradientKind.Waves,
            "fc_Satin" => GradientKind.Satin, "fc_Folds" => GradientKind.Folds, _ => (GradientKind?)null,
        };
        if (kind == null || pth.Get("PSX") == null || pth.Get("PEX") == null) return null;
        var fgl = fpl.Get("FGL");
        var cs = Stops(fgl?.Get("FGY")?.Get("FG0") ?? fgl?.Get("FGV"));
        if (cs.Count == 0) return null;
        var os = Stops(fgl!.Get("FGY")?.Get("FG1"));
        var pos = cs.Select(x => x.p).Concat(os.Select(x => x.p)).Append(0).Append(1).Where(v => v >= 0 && v <= 1).Distinct().OrderBy(v => v).ToArray();
        double am = (fallback >> 24) / 255.0;
        var col = new uint[pos.Length];
        for (int i = 0; i < pos.Length; i++)
        {
            uint a = os.Count > 0 ? Ramp(os, pos[i], 24) : Ramp(cs, pos[i], 24);
            col[i] = (uint)Math.Round(a * am) << 24 | Ramp(cs, pos[i], 16) << 16 | Ramp(cs, pos[i], 8) << 8 | Ramp(cs, pos[i], 0);
        }
        double sx = pth.Float("PSX"), sy = pth.Float("PSY");
        return new VGradient
        {
            Kind = kind.Value, Pos = pos, Col = col, X0 = sx, Y0 = sy, X1 = pth.Float("PEX", sx), Y1 = pth.Float("PEY", sy),
            X2 = pth.Float("PFX", sx), Y2 = pth.Float("PFY", sy),
        };
    }

    /// <summary>
    /// One flat colour for a gradient fill: its colour stops (FGV) and opacity stops (FGY/FG1) averaged over
    /// the gradient's area, weighted by opacity. Radial gradients cover more area further out.
    /// </summary>
    private static uint GradientAverage(FwNode fpl, uint fallback)
    {
        var fgl = fpl.Get("FGL");
        var cs = Stops(fgl?.Get("FGV"));
        if (cs.Count == 0) return fallback;
        var os = Stops(fgl!.Get("FGY")?.Get("FG1"));
        bool radial = fpl.Str("CAT") is "fc_Circular" or "fc_Elliptical";
        double sw = 0, sa = 0, sr = 0, sg = 0, sb = 0;
        const int N = 64;
        for (int i = 0; i < N; i++)
        {
            double t = (i + 0.5) / N, w = radial ? t : 1;
            double a = (os.Count > 0 ? Ramp(os, t, 24) : Ramp(cs, t, 24)) / 255.0;
            sw += w; sa += w * a;
            sr += w * a * Ramp(cs, t, 16); sg += w * a * Ramp(cs, t, 8); sb += w * a * Ramp(cs, t, 0);
        }
        if (sa <= 0) return fallback & 0xFFFFFF;
        uint A = (uint)Math.Round(255 * sa / sw * (fallback >> 24) / 255.0);
        return A << 24 | (uint)Math.Round(sr / sa) << 16 | (uint)Math.Round(sg / sa) << 8 | (uint)Math.Round(sb / sa);
    }

    /// <summary>Cubic Bézier into <paramref name="o"/> (start point already there), until within about 0.25 px.</summary>
    private static void Flatten(VPt p0, VPt p1, VPt p2, VPt p3, List<VPt> o, int depth)
    {
        // Distance of the two control points from the chord bounds the curve's error (error <= 3/4 of it).
        double dx = p3.X - p0.X, dy = p3.Y - p0.Y, len = Math.Sqrt(dx * dx + dy * dy);
        double d1, d2;
        if (len < 1e-9)
        {
            d1 = Math.Sqrt((p1.X - p0.X) * (p1.X - p0.X) + (p1.Y - p0.Y) * (p1.Y - p0.Y));
            d2 = Math.Sqrt((p2.X - p0.X) * (p2.X - p0.X) + (p2.Y - p0.Y) * (p2.Y - p0.Y));
        }
        else
        {
            d1 = Math.Abs((p1.X - p0.X) * dy - (p1.Y - p0.Y) * dx) / len;
            d2 = Math.Abs((p2.X - p0.X) * dy - (p2.Y - p0.Y) * dx) / len;
        }
        if (depth >= 12 || Math.Max(d1, d2) <= 0.3) { o.Add(p3); return; }
        VPt Mid(VPt a, VPt b) => new((a.X + b.X) / 2, (a.Y + b.Y) / 2);
        var p01 = Mid(p0, p1); var p12 = Mid(p1, p2); var p23 = Mid(p2, p3);
        var a2 = Mid(p01, p12); var b2 = Mid(p12, p23); var mid = Mid(a2, b2);
        Flatten(p0, p01, a2, mid, o, depth + 1);
        Flatten(mid, b2, p23, p3, o, depth + 1);
    }

    /// <summary>
    /// A stroke as many small shapes: one quad per segment plus a disc at each
    /// real corner. Shapes fill even-odd on their own, so separate shapes never cancel where they overlap.
    /// </summary>
    private static void Stroke(List<VPt> pts, bool closed, double width, uint col, List<VShape> o)
    {
        double hw = width / 2;
        int n = pts.Count;
        int segs = closed ? n : n - 1;
        for (int i = 0; i < segs; i++)
        {
            var a = pts[i]; var b = pts[(i + 1) % n];
            double dx = b.X - a.X, dy = b.Y - a.Y, len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 1e-9) continue;
            double nx = -dy / len * hw, ny = dx / len * hw;
            o.Add(new VShape
            {
                Colour = col,
                Contours = { new List<VPt> { new(a.X + nx, a.Y + ny), new(b.X + nx, b.Y + ny), new(b.X - nx, b.Y - ny), new(a.X - nx, a.Y - ny) } },
            });
        }
        if (width < 2) return;   // round corners and caps are invisible at this width
        int sides = Math.Clamp((int)Math.Ceiling(hw * 4), 8, 48);
        for (int i = 0; i < n; i++)
        {
            bool end = !closed && (i == 0 || i == n - 1);
            if (!end)
            {
                var p = pts[(i + n - 1) % n]; var q = pts[i]; var r = pts[(i + 1) % n];
                double ax = q.X - p.X, ay = q.Y - p.Y, bx = r.X - q.X, by = r.Y - q.Y;
                double la = Math.Sqrt(ax * ax + ay * ay), lb = Math.Sqrt(bx * bx + by * by);
                if (la < 1e-9 || lb < 1e-9 || (ax * bx + ay * by) / (la * lb) > 0.995) continue;   // nearly straight
            }
            var disc = new List<VPt>();
            for (int k = 0; k < sides; k++)
            {
                double t = 2 * Math.PI * k / sides;
                disc.Add(new VPt(pts[i].X + hw * Math.Cos(t), pts[i].Y + hw * Math.Sin(t)));
            }
            o.Add(new VShape { Colour = col, Contours = { disc } });
        }
    }

    #endregion
}

#endregion
