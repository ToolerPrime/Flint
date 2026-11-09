/* Fireworks PNG import: tag parser, a synthetic document, and real files if present.
 * Flint.exe --fwtest */
using System.Drawing.Imaging;
using System.IO.Compression;
using System.Text;

namespace Flint;

public static partial class FireworksTest
{
    private static int _fails;

    private static void Check(bool ok, string what)
    {
        Console.WriteLine((ok ? "  ok   " : "  FAIL ") + what);
        if (!ok) _fails++;
    }

    #region tree writer

    private static byte[] Raw(string s) => Encoding.Latin1.GetBytes(s);
    private static byte[] Cat(params byte[][] parts) => parts.SelectMany(p => p).ToArray();
    private static byte[] I(string tag, int v) => Raw($"{tag}i{{{v:x}}}");
    private static byte[] F(string tag, double v) => Raw($"{tag}f{{{v.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture)}}}");
    private static byte[] B(string tag, bool v) => Raw($"{tag}b{{{(v ? 1 : 0)}}}");
    private static byte[] S(string tag, string v)
    {
        var u = Encoding.BigEndianUnicode.GetBytes(v);
        return Cat(Raw(tag + "s{"), new[] { (byte)(v.Length >> 8), (byte)v.Length }, u, Raw("}"));
    }
    private static byte[] V(string tag, params byte[][] kids) => Cat(Raw(tag + "v{"), Cat(kids), Raw("}"));

    private static byte[] Zlib(byte[] data)
    {
        using var ms = new MemoryStream();
        using (var z = new ZLibStream(ms, CompressionLevel.Optimal, true)) z.Write(data);
        return ms.ToArray();
    }

    private static uint[]? _crcTable;
    private static uint Crc(byte[] type, byte[] data)
    {
        if (_crcTable == null)
        {
            _crcTable = new uint[256];
            for (uint n = 0; n < 256; n++) { uint c = n; for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1; _crcTable[n] = c; }
        }
        uint crc = 0xFFFFFFFF;
        foreach (var b in type) crc = _crcTable[(crc ^ b) & 255] ^ (crc >> 8);
        foreach (var b in data) crc = _crcTable[(crc ^ b) & 255] ^ (crc >> 8);
        return ~crc;
    }

    private static byte[] Chunk(string type, byte[] data)
    {
        var t = Raw(type);
        var ms = new MemoryStream();
        void BE(uint v) { ms.WriteByte((byte)(v >> 24)); ms.WriteByte((byte)(v >> 16)); ms.WriteByte((byte)(v >> 8)); ms.WriteByte((byte)v); }
        BE((uint)data.Length); ms.Write(t); ms.Write(data); BE(Crc(t, data));
        return ms.ToArray();
    }

    /// <summary>mkBT chunks for a bitmap: 128x128 tiles, A,R,G,B, row-major, ids first..first+n-1.</summary>
    private static List<byte[]> TileChunks(byte[] bgra, int w, int h, int firstId, out int[] ids)
    {
        int cols = (w + 127) / 128, rows = (h + 127) / 128;
        var chunks = new List<byte[]>();
        ids = new int[cols * rows];
        for (int t = 0; t < cols * rows; t++)
        {
            var raw = new byte[128 * 128 * 4];
            for (int y = 0; y < 128; y++)
                for (int x = 0; x < 128; x++)
                {
                    int sx = (t % cols) * 128 + x, sy = (t / cols) * 128 + y;
                    if (sx >= w || sy >= h) continue;
                    int s = (sy * w + sx) * 4, d = (y * 128 + x) * 4;
                    raw[d] = bgra[s + 3]; raw[d + 1] = bgra[s + 2]; raw[d + 2] = bgra[s + 1]; raw[d + 3] = bgra[s];
                }
            ids[t] = firstId + t;
            var head = new byte[76];
            head[0] = 0xFA; head[1] = 0xCE; head[2] = 0xCA; head[3] = 0xFE;
            head[4] = (byte)(ids[t] >> 24); head[5] = (byte)(ids[t] >> 16); head[6] = (byte)(ids[t] >> 8); head[7] = (byte)ids[t];
            chunks.Add(Chunk("mkBT", Cat(head, Zlib(raw))));
        }
        return chunks;
    }

    private static byte[] Img(string extraTag, int x, int y, int w, int h, int[] ids, string? name = null) =>
        V("IMG", F("XLC", x), F("YLC", y), F("XOF", 0), F("YOF", 0), I("WPX", w), I("HPX", h), I("TSZ", 128),
          V("TIL", ids.Select(id => I("TID", id)).ToArray()), V("DTA"));

    private static byte[] Noise(int w, int h, int seed, bool alpha)
    {
        var px = new byte[w * h * 4];
        uint s = (uint)seed;
        for (int i = 0; i < w * h; i++)
        {
            s = s * 1664525 + 1013904223;
            px[i * 4] = (byte)(s >> 8); px[i * 4 + 1] = (byte)(s >> 16); px[i * 4 + 2] = (byte)(s >> 24);
            px[i * 4 + 3] = alpha ? (byte)(i % 7 == 0 ? 0 : (i % 5 == 0 ? 100 : 255)) : (byte)255;
        }
        return px;
    }

    private static byte[] Pt(double x, double y) => V("PBT", F("XLC", x), F("YLC", y));

    #endregion

    public static int Run(string? exportFixture = null)
    {
        Console.WriteLine("Fireworks tag parser");
        Parser();
        Console.WriteLine("Synthetic Fireworks document");
        Synthetic();
        Console.WriteLine("Symbols, gradients, effects");
        Features();
        Console.WriteLine("Button labels, blend ids, rare gradients");
        LabelsAndBlends();
        Console.WriteLine("Fireworks-made samples");
        RealSamples();
        Console.WriteLine("Real files");
        Real();
        Console.WriteLine("Save editable Fireworks PNG");
        SaveEditablePng(exportFixture);
        Console.WriteLine(_fails == 0 ? "all fireworks checks passed" : $"{_fails} fireworks check(s) FAILED");
        return _fails == 0 ? 0 : 1;
    }

    private static void Parser()
    {
        var tree = V("MKB",
            I("WID", 0x2c3), I("NEG", -5), F("FLT", 643.000061), B("YES", true), B("NOP", false),
            S("STR", "a{b}c\u00e9\u4e2d"),
            Raw("ZZZq{some}"),                       // unknown value type: skipped to the brace
            V("UNK", V("DEE", I("AAA", 1)), S("SSS", "}}{{")),   // unknown list tag
            V("LST", V("CHD", I("NNN", 1)), V("CHD", I("NNN", 2)), V("CHD", I("NNN", 3))),
            S("EMP", ""));
        var r = FwParser.Parse(tree);
        Check(r.Tag == "MKB" && r.Children.Count == 10, "tree parses with every child");
        Check(r.Int("WID") == 0x2c3 && r.Int("NEG") == -5 && r.Int("MISSING", 7) == 7, "hex ints, negative ints and defaults");
        Check(Math.Abs(r.Float("FLT") - 643.000061) < 1e-9, "float");
        Check(r.Bool("YES") && !r.Bool("NOP") && !r.Bool("MISSING"), "bools");
        Check(r.Str("STR") == "a{b}c\u00e9\u4e2d", "UTF-16 string containing braces");
        Check(r.Str("EMP") == "" && r.Get("EMP") != null, "empty string");
        Check(r.Get("UNK")?.Get("SSS")?.Value == "}}{{", "string of only braces inside an unknown list");
        Check(r.Get("LST")!.All("CHD").Select(c => c.Int("NNN")).SequenceEqual(new[] { 1, 2, 3 }), "repeated tags in a nested list");
        Check(r.Get("ZZZ") is { Value: "some" }, "unknown value type kept raw and skipped over");
        Check(r.Get("UNK")!.Descendants("AAA").Count() == 1, "Descendants finds nested tags");
        bool threw = false;
        try { FwParser.Parse(Raw("MKBv{WIDi{5}")); } catch (InvalidDataException) { threw = true; }
        Check(threw, "unclosed list throws InvalidDataException");
        threw = false;
        try { FwParser.Parse(Raw("MKBv{ABCs{\0\u0009ab}}")); } catch (InvalidDataException) { threw = true; }
        Check(threw, "string with a bad length throws InvalidDataException");
        threw = false;
        try { FwParser.Parse(Raw("XY")); } catch (InvalidDataException) { threw = true; }
        Check(threw, "garbage throws InvalidDataException");
    }

    /// <summary>Insert extra chunks before IEND of a PNG saved by GDI+.</summary>
    private static byte[] WithChunks(byte[] png, IEnumerable<byte[]> chunks)
    {
        int iend = png.Length - 12;
        return Cat(png[..iend], Cat(chunks.ToArray()), png[iend..]);
    }

    private static byte[] PlainPng(int w, int h)
    {
        using var bmp = ImageOps.FromPixels(w, h, new byte[w * h * 4]);
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    private static bool Same(byte[] a, byte[] b)
    {
        if (a.AsSpan().SequenceEqual(b)) return true;
        int i = 0;
        while (i < Math.Min(a.Length, b.Length) && a[i] == b[i]) i++;
        Console.WriteLine($"        first difference at byte {i} of {a.Length}/{b.Length}");
        return false;
    }

    private static void Synthetic()
    {
        const int W = 260, H = 200;
        var big = Noise(200, 150, 1, true);     // 2x2 tiles
        var txt = Noise(30, 10, 2, true);
        var small = Noise(8, 8, 3, false);
        var tiles = new List<byte[]>();
        tiles.AddRange(TileChunks(big, 200, 150, 0x100, out var bigIds));
        tiles.AddRange(TileChunks(txt, 30, 10, 0x200, out var txtIds));
        tiles.AddRange(TileChunks(small, 8, 8, 0x300, out var smallIds));

        var square = V("PTH",
            V("PAT", Raw("BCLi{ff000000}"), Raw("FCLi{ffff8000}"), V("FPL", S("CAT", "fc_Solid"))),
            V("PBL", V("PBP", B("ISC", true), I("PPC", 4), Pt(220, 10), Pt(250, 10), Pt(250, 40), Pt(220, 40))));
        var curve = V("PTH",
            V("PAT", Raw("FCLi{ff00ff00}"), V("FPL", S("CAT", "fc_Linear"))),
            V("PBL", V("PBP", B("ISC", true),
                V("PBT", F("XLC", 100), F("YLC", 180), F("XSC", 100), F("YSC", 195)),
                V("PBT", F("XLC", 130), F("YLC", 180), F("XPC", 130), F("YPC", 195)))));
        var lyl = V("LYL",
            V("LAY", S("LNM", "Bottom"), B("URL", false), B("MSL", true),
                V("CLL", V("CEL", B("LCK", false), V("ELM", curve, square, Img("", 10, 20, 200, 150, bigIds)))),
                V("VIF", B("VIS", true))),
            V("LAY", S("LNM", "Top"), B("URL", false), B("MSL", false),
                V("CLL", V("CEL", B("LCK", false), V("ELM",
                    V("ALS", S("ALN", "sym")),
                    V("GRP", I("GRT", 0), V("ELM", Img("", 100, 180, 8, 8, smallIds))),
                    V("TXT", F("LFT", 0), V("TFS", S("TRN", "Hello {world} and more text"), S("TRN", "!")), Img("", 20, 175, 30, 10, txtIds), V("EFL"))))),
                V("VIF", B("VIS", true))),
            V("LAY", S("LNM", "Web Layer"), B("URL", true), B("MSL", true),
                V("CLL", V("CEL", V("ELM", Img("", 0, 0, 8, 8, smallIds)))),
                V("VIF", B("VIS", true))));
        var root = V("MKB", I("FRC", 1), F("WID", W), F("HIT", H), lyl);

        var flat = Noise(W, H, 9, true);
        byte[] png;
        using (var bmp = ImageOps.FromPixels(W, H, flat))
        using (var ms = new MemoryStream())
        {
            bmp.Save(ms, ImageFormat.Png);
            png = WithChunks(ms.ToArray(), new[] { Chunk("mkBF", new byte[72]), Chunk("mkTS", Zlib(root)) }.Concat(tiles));
        }

        FwImport imp;
        try { imp = FireworksPng.Build(png); }
        catch (Exception ex) { Check(false, "synthetic document opens: " + ex.Message); return; }
        var d = imp.Doc;
        Check(d.Width == W && d.Height == H && d.FrameCount == 1, "canvas size and frame count");
        Check(d.Layers.Count == 3 && d.Layers[0].Name == FireworksPng.ReferenceLayerName && d.Layers[1].Name == "Bottom" && d.Layers[2].Name == "Top",
            "layers: reference, Bottom, Top (web layer skipped), bottom-first order");
        Check(d.Layers[1].Shared && !d.Layers[2].Shared, "shared flag read from MSL");

        var bottom = d.Layers[1].CelAt(0)!.Objects;
        Check(bottom.Count == 3 && bottom[0] is RasterObject, "Bottom holds bitmap and two paths in order");
        var bm = (RasterObject)bottom[0];
        Check(bm.X == 10 && bm.Y == 20 && bm.Width == 200 && bm.Height == 150 && Same(ImageOps.GetPixels(bm.Pixels), big),
            "multi-tile 200x150 bitmap: position and exact pixels (straight alpha)");

        var sq = (RasterObject)bottom[1];
        Check(sq.Vector != null && sq.Mode == Resample.Smooth, "closed path is a smooth vector object");
        var rend = d.RenderPixels(0, 0);   // hide nothing else: the reference layer is hidden already
        int Px(int x, int y, int c) => rend[(y * W + x) * 4 + c];
        Check(Px(235, 25, 3) == 255 && Px(235, 25, 2) == 0xff && Px(235, 25, 1) == 0x80 && Px(235, 25, 0) == 0, "square fill colour inside");
        Check(Px(215, 25, 3) == 0 && Px(255, 25, 3) == 0 && Px(235, 5, 3) == 0 && Px(235, 45, 3) == 0, "transparent outside the square");
        Check(sq.X == 220 && sq.Y == 10 && sq.Width == 30 && sq.Height == 30, "square covers exactly its bounds");
        var cv = (RasterObject)bottom[2];
        Check(cv.Vector != null && cv.Width >= 30 && cv.Height > 10, "curved path (with handles) flattens to an object");

        var top = d.Layers[2].CelAt(0)!.Objects;
        Check(top.Count == 2, "Top holds the text picture and the group's child (symbol skipped)");
        var t = (RasterObject)top[0];
        Check(t.Name == "Text: Hello {world} and mo" && t.X == 20 && t.Y == 175 && Same(ImageOps.GetPixels(t.Pixels), txt),
            "text keeps its cached picture, exact, named from its runs");
        var g = (RasterObject)top[1];
        Check(g.X == 100 && g.Y == 180 && Same(ImageOps.GetPixels(g.Pixels), small), "group child imported at its position");

        Check(imp.Warnings.Any(x => x.Contains("1 symbol instance") && x.Contains("missing symbol")), "warning for the symbol instance with no symbol");
        Check(!imp.Warnings.Any(x => x.Contains("effect")), "an empty effect list is not an effect");
        Check(imp.Warnings.Any(x => x.Contains("gradient")), "warning for the gradient fill that has no geometry (stays flat)");
        Check(bm.Original == null && bm.Effects == null && !bm.IsTransformed && sq.Effects == null, "objects without effects stay plain: no Original, no effect list");
        Check(!imp.Warnings.Any(x => x.Contains("web")), "web layer is skipped quietly");

        // A thin fill with a wide stroke: every stroke quad is larger than the fill, yet must paint over it.
        var thin = V("PTH",
            V("PAT", Raw("BCLi{ff0000ff}"), Raw("FCLi{ffff0000}"), V("FPL", S("CAT", "fc_Solid")), V("BPL", S("CAT", "bc_Basic"), I("BDI", 6))),
            V("PBL", V("PBP", B("ISC", true), Pt(10, 10), Pt(60, 10), Pt(60, 12), Pt(10, 12))));
        var thinDoc = FireworksPng.Build(WithChunks(PlainPng(30, 30), new[] { Chunk("mkTS", Zlib(V("MKB", I("FRC", 1), F("WID", 70), F("HIT", 30), V("LYL", V("LAY", S("LNM", "L"), V("CLL", V("CEL", V("ELM", thin)))))))) })).Doc;
        var tp = thinDoc.RenderPixels(0, 0);
        int TP(int x, int y, int c) => tp[(y * 70 + x) * 4 + c];
        Check(TP(35, 11, 3) == 255 && TP(35, 11, 0) == 255 && TP(35, 11, 2) == 0, "stroke paints over a fill smaller than its own stroke segments");

        var rl = d.Layers[0];
        var rb = (RasterObject)rl.CelAt(0)!.Objects[0];
        Check(!rl.Visible && rl.Locked && Same(ImageOps.GetPixels(rb.Pixels), flat), "reference layer present, hidden, locked, flattened picture exact");
        Check(d.Layers[0].CelAt(0) != null && d.Layers[0].CelAt(0)!.Objects.Count == 1, "reference layer has one object");

        bool threw = false;
        try { FireworksPng.Build(Array.Empty<byte>()); } catch (InvalidDataException) { threw = true; }
        Check(threw, "empty file throws InvalidDataException");
        threw = false;
        using (var bmp = ImageOps.FromPixels(4, 4, new byte[64])) using (var ms = new MemoryStream())
        {
            bmp.Save(ms, ImageFormat.Png);
            try { FireworksPng.Build(ms.ToArray()); } catch (InvalidDataException) { threw = true; }
        }
        Check(threw, "plain PNG throws InvalidDataException");
    }

    private static byte[] Dce(string k, string v) => V("DCE", S("DCK", k), S("DCV", v));
    private static byte[] Eps(string moa, params (string k, string v)[] kv) =>
        V("EPS", Cat(new[] { Dce("EffectIsVisible", "true"), Dce("EffectMoaID", "{" + moa + "-0000-0000-0000-000000000000}") }.Concat(kv.Select(x => Dce(x.k, x.v))).ToArray()));
    private static byte[] Mtx(double a, double d, double tx, double ty) =>
        V("MTX", F("M00", a), F("M01", 0), F("M02", 0), F("M10", 0), F("M11", d), F("M12", 0), F("M20", tx), F("M21", ty), F("M22", 1));
    private static byte[] Rect(double x0, double y0, double x1, double y1, uint col, byte[]? fill = null) =>
        V("PTH", V("PAT", Raw($"FCLi{{{col:x}}}"), fill ?? V("FPL", S("CAT", "fc_Solid"))),
          V("PBL", V("PBP", B("ISC", true), Pt(x0, y0), Pt(x1, y0), Pt(x1, y1), Pt(x0, y1))));
    private static byte[] Stop(double p, uint c) => V("FGI", F("FGP", p), Raw($"FGCi{{{c:x}}}"));
    private static byte[] Grad(string kind) =>
        V("FPL", S("CAT", kind),
          V("FGL", V("FGY", V("FG0", Stop(0, 0xffff0000), Stop(1, 0xff0000ff)), V("FG1", Stop(0, 0xff000000), Stop(1, 0)))));
    private static byte[] WithGeo(byte[] pth, double sx, double sy, double ex, double ey, double fx, double fy) =>
        Cat(pth[..^1], F("PSX", sx), F("PSY", sy), F("PEX", ex), F("PEY", ey), F("PFX", fx), F("PFY", fy), Raw("}"));

    private static Document Build1(byte[] elements, int w, int h, byte[]? symbols, byte[]? tile, out FwImport imp)
    {
        var root = V("MKB", I("FRC", 1), F("WID", w), F("HIT", h), symbols ?? V("MSL"),
            V("LYL", V("LAY", S("LNM", "L"), V("CLL", V("CEL", V("ELM", elements))), V("VIF", B("VIS", true)))));
        var chunks = new List<byte[]> { Chunk("mkTS", Zlib(root)) };
        if (tile != null) chunks.Add(tile);
        imp = FireworksPng.Build(WithChunks(PlainPng(w, h), chunks));
        return imp.Doc;
    }

    private static byte[] Sym(string name, int id, params byte[][] elms) =>
        V("MST", S("UNM", name), I("MSN", id), V("LYL", V("LAY", S("LNM", "s"), V("CLL", V("CEL", V("ELM", elms))), V("VIF", B("VIS", true)))));
    private static byte[] Als(int id, double a, double d, double tx, double ty) => V("ALS", I("MSN", id), Mtx(a, d, tx, ty), I("ANN", 1));

    private static void Features()
    {
        // --- symbols: placement by M20/M21, scale by M00/M11, nesting, cycle guard
        var syms = V("MSL",
            Sym("box", 0x10, Rect(0, 0, 10, 10, 0xffff0000)),
            Sym("outer", 0x11, Als(0x10, 1, 1, 5, 0)),
            Sym("loop", 0x12, Als(0x12, 1, 1, 0, 0), Rect(0, 0, 4, 4, 0xff00ff00)));
        var d = Build1(Cat(Als(0x10, 1, 1, 20, 30), Als(0x10, 2, 2, 100, 100), Als(0x11, 1, 1, 50, 50), Als(0x12, 1, 1, 150, 10)), 200, 200, syms, null, out var imp);
        var px = d.RenderPixels(0, 0);
        int A(int x, int y) => px[(y * 200 + x) * 4 + 3];
        int R(int x, int y) => px[(y * 200 + x) * 4 + 2];
        Check(R(25, 35) == 255 && A(25, 35) == 255 && A(19, 35) == 0 && A(31, 35) == 0 && A(25, 29) == 0 && A(25, 41) == 0, "symbol instance lands at M20/M21 (x'=x+M20, y'=y+M21)");
        Check(A(110, 110) == 255 && A(119, 119) == 255 && A(121, 110) == 0 && A(99, 110) == 0, "M00/M11 scale the symbol (10x10 becomes 20x20)");
        Check(A(60, 55) == 255 && A(54, 55) == 0 && A(66, 55) == 0, "nested symbol: outer at (50,50) holding box at (5,0) puts it at (55,50)");
        Check(A(151, 11) == 255 && imp.Warnings.Any(w => w.Contains("contains itself")), "a symbol that contains itself is drawn once and the cycle is reported");
        var names = d.Layers[1].CelAt(0)!.Objects.Select(o => o.Name).ToList();
        Check(names.Contains("box: Path") && names.Contains("outer: box: Path"), "objects are named <symbol>: <element>");
        Check(d.Layers[1].CelAt(0)!.Objects.OfType<RasterObject>().All(o => o.Vector != null) && imp.SymbolInstances >= 5, "symbol paths stay vector");

        // a bitmap in a symbol
        var bmpPx = Noise(8, 8, 5, false);
        var tc = TileChunks(bmpPx, 8, 8, 0x400, out var bids);
        var bsym = V("MSL", Sym("pic", 0x20, Img("", 0, 0, 8, 8, bids)));
        var d2 = Build1(Cat(Als(0x20, 1, 1, 30, 40), Als(0x20, 2, 2, 100, 40)), 200, 100, bsym, tc[0], out _);
        var pics = d2.Layers[1].CelAt(0)!.Objects.OfType<RasterObject>().OrderBy(o => o.X).ToList();
        Check(pics.Count == 2 && pics[0].X == 30 && pics[0].Y == 40 && pics[0].Width == 8 && !pics[0].IsTransformed && ImageOps.GetPixels(pics[0].Pixels).SequenceEqual(bmpPx), "bitmap in a symbol moved by a whole-pixel matrix keeps its exact pixels");
        Check(pics[1].X == 100 && pics[1].Width == 16 && pics[1].Original != null && pics[1].Mode == Resample.Smooth, "bitmap in a scaled instance is a transformed object (smooth)");

        // --- gradients, sampled at known points
        byte[] gd(byte[] pth) => Build1(pth, 120, 120, null, null, out _).RenderPixels(0, 0);
        int LB(byte[] p, int x, int y, int c) => p[(y * 120 + x) * 4 + c];
        var lin = gd(WithGeo(Rect(0, 0, 100, 10, 0xffffffff, Grad("fc_Linear")), 0, 0, 100, 0, 0, 0));
        Check(LB(lin, 50, 5, 2) > 250 && LB(lin, 50, 5, 0) < 6 && Math.Abs(LB(lin, 50, 5, 3) - 128) <= 3, "linear gradient at 50%: half opacity, and (premultiplied) the colour leans to the more opaque red stop");
        Check(LB(lin, 2, 5, 2) > 240 && LB(lin, 2, 5, 3) > 240 && LB(lin, 97, 5, 3) < 12 && LB(lin, 110, 5, 3) == 0, "linear gradient ends: red and opaque at the start, clear at the end, nothing past the shape");
        var cir = gd(WithGeo(Rect(0, 0, 100, 100, 0xffffffff, Grad("fc_Circular")), 50, 50, 100, 50, 0, 0));
        Check(LB(cir, 50, 50, 2) > 250 && LB(cir, 50, 50, 3) > 250 && Math.Abs(LB(cir, 75, 50, 3) - 128) <= 6 && LB(cir, 2, 2, 3) < 3, "circular gradient: centre opaque red, half the radius half opacity, past the radius clamped (clear)");
        var ell = gd(WithGeo(Rect(0, 0, 100, 100, 0xffffffff, Grad("fc_Elliptical")), 50, 50, 100, 50, 50, 75));
        Check(Math.Abs(LB(ell, 50, 62, 3) - 128) <= 8 && Math.Abs(LB(ell, 75, 50, 3) - 128) <= 6 && LB(ell, 50, 80, 3) < 6, "elliptical gradient: radius 50 across, 25 down");
        var gdoc = Build1(WithGeo(Rect(0, 0, 100, 100, 0xffffffff, Grad("fc_Circular")), 50, 50, 100, 50, 0, 0), 120, 120, null, null, out _);
        var go = (RasterObject)gdoc.Layers[1].CelAt(0)!.Objects[0];
        var scaled = go.Retransform(Affine.Scale(2, 2), Resample.Smooth);
        var sp = ImageOps.GetPixels(scaled.Pixels);
        Check(go.Vector!.Shapes[0].Gradient != null && sp[((scaled.Height / 2) * scaled.Width + scaled.Width / 2) * 4 + 3] > 250
              && Math.Abs(sp[((scaled.Height / 2) * scaled.Width + scaled.Width * 3 / 4) * 4 + 3] - 128) <= 8, "gradient follows a Retransform (scaled 2x: same shape, bigger)");
        string gpath = Path.Combine(Path.GetTempPath(), "flint_fw_grad.flint");
        FlintFormat.Save(gdoc, gpath);
        var gl = FlintFormat.Load(gpath);
        var gg = ((RasterObject)gl.Document!.Layers[1].CelAt(0)!.Objects[0]).Vector!.Shapes[0].Gradient;
        var g0 = go.Vector!.Shapes[0].Gradient!;
        Check(gl.Error == null && gg != null && gg.Kind == g0.Kind && gg.Pos.SequenceEqual(g0.Pos) && gg.Col.SequenceEqual(g0.Col) && gg.X1 == g0.X1 && gg.Y0 == g0.Y0, "gradient survives .flint save and load");
        Check(((RasterObject)go.CloneObject()).Vector == go.Vector, "cloning keeps the gradient (shared art)");

        // --- effects on a 20x20 red bitmap at (50,50)
        var red = new byte[20 * 20 * 4];
        for (int i = 0; i < 400; i++) { red[i * 4 + 2] = 255; red[i * 4 + 3] = 255; }
        var rt = TileChunks(red, 20, 20, 0x500, out var rids);
        byte[] Shadow(string col, int dist, int ang, int blur) => Eps("a7944db8", ("ShadowColor", col), ("ShadowDistance", dist.ToString()), ("ShadowAngle", ang.ToString()), ("ShadowBlur", blur.ToString()));
        (Document doc, RasterObject o) One(params byte[][] eps)
        {
            var img = Img("", 50, 50, 20, 20, rids);
            var dd = Build1(Cat(img[..^1], V("EFL", eps), Raw("}")), 120, 120, null, rt[0], out _);
            return (dd, (RasterObject)dd.Layers[1].CelAt(0)!.Objects[0]);
        }
        int[] At(Document dd, int x, int y) { var p = dd.RenderPixels(0, 0); int o = (y * 120 + x) * 4; return new int[] { p[o], p[o + 1], p[o + 2], p[o + 3] }; }

        var (sd, so) = One(Shadow("#0000ffff", 6, 315, 0));
        Check(At(sd, 72, 72).SequenceEqual(new[] { 255, 0, 0, 255 }) && At(sd, 48, 48)[3] == 0 && At(sd, 72, 52)[3] == 0 && At(sd, 52, 72)[3] == 0, "drop shadow at 315 degrees falls down and right, in its own colour (blue)");
        Check(so.Effects is { Count: 1 } && so.Original != null && so.Width > 20 && At(sd, 60, 60).SequenceEqual(new[] { 0, 0, 255, 255 }), "the object grew to hold the shadow and the object itself is untouched on top");
        var (sd2, _) = One(Shadow("#0000ffff", 6, 135, 0));
        Check(At(sd2, 48, 48).SequenceEqual(new[] { 255, 0, 0, 255 }) && At(sd2, 72, 72)[3] == 0, "angle 135 puts the shadow up and to the left");
        var (sd3, _) = One(Shadow("#00000080", 4, 270, 0));
        Check(At(sd3, 60, 73)[3] is > 100 and < 160 && At(sd3, 60, 48)[3] == 0, "shadow colour alpha (80 hex) sets its opacity; 270 degrees is straight down");
        var (gd2, _) = One(Eps("2ba87123", ("GlowWidth", "3"), ("MaskSoftness", "0"), ("BevelContrast", "100"), ("OuterBevelColor", "#00ff00")));
        Check(At(gd2, 48, 60).SequenceEqual(new[] { 0, 255, 0, 255 }) && At(gd2, 44, 60)[3] == 0 && At(gd2, 60, 60)[2] == 255, "glow draws a ring of its colour around the object, width 3");
        var (bl, blo) = One(Eps("d04ef8c0", ("gaussian_blur_radius", "3")));
        Check(At(bl, 50, 60)[3] is > 20 and < 235 && At(bl, 60, 60)[3] == 255 && blo.Width > 20, "gaussian blur softens the edge and spreads it outwards");
        var (hs, _) = One(Eps("3439b08d", ("hue_amount", "120"), ("saturation_amount", "0"), ("lightness_amount", "0"), ("hls_colorize", "false")));
        var hp = At(hs, 60, 60);
        Check(hp[1] > 200 && hp[2] < 60 && hp[3] == 255, "hue shift of 120 turns red into green");
        var (_, hio) = One(Eps("a7944db8", ("ShadowColor", "#0000ffff"), ("ShadowDistance", "6"), ("ShadowAngle", "315"), ("ShadowBlur", "0"), ("EffectIsVisible", "false")));
        Check(hio.Effects == null && !hio.IsTransformed && hio.Width == 20, "an effect marked not visible is skipped");

        // survive Retransform, clone, save/load
        var moved = so.Retransform(Affine.Translate(10, 0), Resample.Nearest);
        Check(moved.Effects != null && moved.Width == so.Width && ReferenceEquals(moved.Pixels, so.Pixels), "a whole-pixel move reuses the pixels and keeps the effects");
        var rot = so.Retransform(Affine.Rotate(90).About(60, 60), Resample.Nearest);
        var rd = new Document(120, 120); rd.Layers[0].EnsureCel(0).Objects.Add(rot);
        var rp = rd.RenderPixels(0);
        int dr = ((rot.Y + rot.Height - 2) * 120 + (rot.X + rot.Width - 2)) * 4;
        Check(rot.Effects != null && rot.Width > 20 && rp[dr + 3] == 255 && rp[dr] == 255, "after a rotation the effects are re-applied: the shadow still points down-right (the light does not turn)");
        var cl = (RasterObject)so.CloneObject();
        Check(cl.Effects != null && cl.Effects.Count == 1 && cl.Effects[0].P["dist"] == 6 && ImageOps.GetPixels(cl.Pixels).SequenceEqual(ImageOps.GetPixels(so.Pixels)), "clone keeps the effects");
        string fpath = Path.Combine(Path.GetTempPath(), "flint_fw_fx.flint");
        FlintFormat.Save(sd, fpath);
        var fl = FlintFormat.Load(fpath);
        var lo = (RasterObject)fl.Document!.Layers[1].CelAt(0)!.Objects[0];
        Check(fl.Error == null && lo.Effects is { Count: 1 } && lo.Effects[0].Kind == EffectKind.DropShadow && lo.Effects[0].P["angle"] == 315
              && ImageOps.GetPixels(lo.Pixels).SequenceEqual(ImageOps.GetPixels(so.Pixels))
              && ImageOps.GetPixels(lo.Retransform(Affine.Rotate(90).About(60, 60), Resample.Nearest).Pixels).SequenceEqual(ImageOps.GetPixels(rot.Pixels)), "effects survive .flint save and load, and re-render identically after");
        Check(so.With(so.Pixels).Effects == null && so.With(so.Pixels).Original == null, "With() gives plain pixels without effects");
        FlintFormat.Save(new Document(8, 8), fpath);
        Check(FlintFormat.CurrentVersion >= 8 && FlintFormat.Load(fpath).Error == null, "format version is 8 and a plain file loads");
    }

    private static readonly string[] Roots =
    {
        @"C:\Program Files (x86)\Macromedia\Fireworks 8\Samples",
        @"C:\Program Files (x86)\Macromedia\Fireworks 8\Configuration\Libraries",
        @"C:\Program Files (x86)\Macromedia\Fireworks 8\Tutorial_assets",
    };

    /// <summary>Composite every layer except the reference; compare with the flattened picture.</summary>
    private static (double within, int exactDiff, FwImport imp) Score(string path, int tol)
    {
        var imp = FireworksPng.Open(path);
        var d = imp.Doc;
        var got = d.RenderPixels(0, 0);
        // Fireworks shows its canvas colour behind the layers; the flattened picture includes it.
        uint bg = imp.CanvasColour;
        int ba = (int)(bg >> 24), br = (int)((bg >> 16) & 255), bgg = (int)((bg >> 8) & 255), bb = (int)(bg & 255);
        for (int i = 0; i < got.Length; i += 4)
        {
            int a = got[i + 3];
            if (a == 255 || ba == 0) continue;
            int oa = a * 255 + ba * (255 - a);
            if (a > 0) { got[i] = (byte)((got[i] * a * 255 + bb * ba * (255 - a) + oa / 2) / oa); got[i + 1] = (byte)((got[i + 1] * a * 255 + bgg * ba * (255 - a) + oa / 2) / oa); got[i + 2] = (byte)((got[i + 2] * a * 255 + br * ba * (255 - a) + oa / 2) / oa); }
            else { got[i] = (byte)bb; got[i + 1] = (byte)bgg; got[i + 2] = (byte)br; }
            got[i + 3] = (byte)((oa + 127) / 255);
        }
        var want = ImageOps.GetPixels(((RasterObject)d.Layers[0].CelAt(0)!.Objects[0]).Pixels);
        if (Environment.GetEnvironmentVariable("FW_DUMP") is { Length: > 0 } dump)
        {
            string stem = Path.Combine(dump, Path.GetFileNameWithoutExtension(path) + (path.Contains("Complete") ? "_c" : ""));
            using (var b = ImageOps.FromPixels(d.Width, d.Height, got)) b.Save(stem + "_got.png", ImageFormat.Png);
            using (var b = ImageOps.FromPixels(d.Width, d.Height, want)) b.Save(stem + "_want.png", ImageFormat.Png);
        }
        int ok = 0, diff = 0, n = d.Width * d.Height;
        for (int i = 0; i < n; i++)
        {
            bool near = true, same = true;
            bool bothClear = got[i * 4 + 3] == 0 && want[i * 4 + 3] == 0;
            for (int c = 0; c < 4; c++)
            {
                int dv = Math.Abs(got[i * 4 + c] - want[i * 4 + c]);
                if (dv > tol) near = false;
                if (dv != 0) same = false;
            }
            if (near || bothClear) ok++;
            if (!same && !bothClear) diff++;
        }
        return (100.0 * ok / n, diff, imp);
    }

    private static void Real()
    {
        foreach (var p in new[]
        {
            @"C:\Users\Techn\Pictures\Untitled-1.png",
            @"C:\Users\Techn\Documents\Codex\2026-11-06\i-x20\outputs\Tyler_base_user_original_64x126.png",
        })
        {
            string name = Path.GetFileName(p);
            if (!File.Exists(p)) { Console.WriteLine($"  skip  {name} not found"); continue; }
            try
            {
                var (_, diff, imp) = Score(p, 0);
                Check(diff == 0, $"{name}: layers composite to the flattened picture exactly ({imp.Summary})");
            }
            catch (Exception ex) { Check(false, $"{name}: {ex.Message}"); }
        }

        bool any = false;
        foreach (var root in Roots)
        {
            if (!Directory.Exists(root)) continue;
            foreach (var f in Directory.EnumerateFiles(root, "*.png", SearchOption.AllDirectories))
            {
                if (!Importer.HasFireworksChunks(f)) continue;
                try
                {
                    var (score, _, imp) = Score(f, 8);
                    any = true;
                    Console.WriteLine($"  score {score,6:F1}%  {Path.GetFileName(f)}  [{imp.Summary}]");
                    foreach (var w in imp.Warnings) Console.WriteLine($"         - {w}");
                    // Symbol-only libraries have nothing Flint imports yet, so a low score is expected there.
                    // The Libraries files hold only symbols, every instance at the identity matrix; their flattened picture is a
                    // preview arranged some other way, so it cannot be matched.
                    bool explained = f.Contains("Libraries") || imp.Warnings.Any(w => w.Contains("gradient"));
                    if (score > 50 || !explained) Check(score > 50, $"{Path.GetFileName(f)} parses and scores over 50%");
                    else Console.WriteLine("         (low score expected: symbol-library preview or unsupported gradients; not asserted)");
                }
                catch (InvalidDataException ex) when (ex.Message.Contains("no Fireworks document"))
                {
                    Console.WriteLine($"  skip  {Path.GetFileName(f)}: {ex.Message}");
                }
                catch (Exception ex) { Check(false, $"{Path.GetFileName(f)}: {ex.Message}"); }
            }
        }
        if (!any) Console.WriteLine("  skip  no Fireworks 8 install to test against");
    }
}
