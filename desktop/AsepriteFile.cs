/* Aseprite .aseprite / .ase files: read and write layers, frames, frame
 * durations and tags. Written for Flint from Aseprite's public file format
 * description (docs/ase-file-specs.md in the Aseprite repository); no
 * Aseprite code is used.
 *
 * Writing: RGBA, one layer chunk per Flint layer (bottom first, as both
 * programs order them), one zlib-compressed cel per layer per frame cropped
 * to its visible pixels, linked cels for Fireworks-style shared layers, a
 * tags chunk. Reading: RGBA, greyscale and indexed files; raw, linked and
 * compressed cels; group layers become plain layers; tilemaps are skipped
 * with a note. Every cel lands on a canvas-sized bitmap (pixels past the
 * canvas edge are cut off). */
using System.IO.Compression;
using System.Text;

namespace Flint;

public sealed class AsepriteImport
{
    public Document Doc = null!;
    public List<string> Warnings = new();
    public string Summary => $"{Doc.Width}×{Doc.Height}, {Doc.LayerCount} layers, {Doc.FrameCount} frames, {Doc.Tags.Count} tags";
}

public static class AsepriteFile
{
    public static bool IsAseprite(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".aseprite" or ".ase";

    // Aseprite blend mode ids (spec order) <-> Flint blend modes.
    private static readonly BlendMode[] Blends =
    {
        BlendMode.Normal, BlendMode.Multiply, BlendMode.Screen, BlendMode.Overlay, BlendMode.Darken, BlendMode.Lighten,
        BlendMode.ColorDodge, BlendMode.ColorBurn, BlendMode.HardLight, BlendMode.SoftLight, BlendMode.Difference,
        BlendMode.Exclusion, BlendMode.Hue, BlendMode.Saturation, BlendMode.Color, BlendMode.Luminosity,
        BlendMode.Add, BlendMode.Subtract, BlendMode.Divide,
    };

    // ---------- writing ----------

    public static string Write(Document doc, string path, int from = 0, int to = -1)
    {
        if (to < 0) to = doc.FrameCount - 1;
        int n = to - from + 1;
        if (doc.Width > 65535 || doc.Height > 65535) throw new InvalidOperationException("Aseprite files are limited to 65535 pixels a side");
        var lost = new HashSet<string>();
        using var ms = new MemoryStream();
        var w = new BinaryWriter(ms);

        // Header (128 bytes); file size patched at the end.
        w.Write(0u); w.Write((ushort)0xA5E0); w.Write((ushort)n);
        w.Write((ushort)doc.Width); w.Write((ushort)doc.Height); w.Write((ushort)32);
        w.Write(1u);                                     // layer opacity is valid
        w.Write((ushort)100); w.Write(0u); w.Write(0u);
        w.Write((byte)0); w.Write(new byte[3]); w.Write((ushort)1);
        w.Write((byte)1); w.Write((byte)1);             // square pixels
        w.Write((short)0); w.Write((short)0); w.Write((ushort)16); w.Write((ushort)16);
        w.Write(new byte[84]);

        for (int f = from; f <= to; f++)
        {
            var chunks = new List<byte[]>();
            if (f == from)
            {
                chunks.Add(Chunk(0x2019, c =>
                {
                    c.Write(1u); c.Write(0u); c.Write(0u); c.Write(new byte[8]);
                    c.Write((ushort)0); c.Write((byte)0); c.Write((byte)0); c.Write((byte)0); c.Write((byte)255);
                }));
                foreach (var l in doc.Layers)
                {
                    int blend = Array.IndexOf(Blends, l.Blend);
                    if (blend < 0) { blend = 0; lost.Add($"blend mode {l.Blend} (saved as Normal)"); }
                    chunks.Add(Chunk(0x2004, c =>
                    {
                        c.Write((ushort)((l.Visible ? 1 : 0) | (l.Locked ? 0 : 2)));
                        c.Write((ushort)0); c.Write((ushort)0); c.Write((ushort)0); c.Write((ushort)0);
                        c.Write((ushort)blend); c.Write(l.Opacity); c.Write(new byte[3]);
                        Str(c, l.Name);
                    }));
                }
                if (doc.Tags.Count > 0)
                {
                    var tags = doc.Tags.Where(t => t.End >= from && t.Start <= to).ToList();
                    chunks.Add(Chunk(0x2018, c =>
                    {
                        c.Write((ushort)tags.Count); c.Write(new byte[8]);
                        foreach (var t in tags)
                        {
                            c.Write((ushort)(Math.Max(t.Start, from) - from)); c.Write((ushort)(Math.Min(t.End, to) - from));
                            c.Write((byte)0); c.Write((ushort)0); c.Write(new byte[6]);
                            c.Write(t.Colour.R); c.Write(t.Colour.G); c.Write(t.Colour.B); c.Write((byte)0);
                            Str(c, t.Name);
                        }
                    }));
                }
            }
            for (int li = 0; li < doc.Layers.Count; li++)
            {
                var l = doc.Layers[li];
                var cel = l.CelAt(f);
                if (cel == null) continue;
                if (l.Shared && f > from)
                {
                    // Fireworks' shared layer is Aseprite's linked cel.
                    chunks.Add(Chunk(0x2005, c => { CelHead(c, li, 0, 0, 1); c.Write((ushort)0); }));
                    continue;
                }
                var flat = l.ShallowCopy();
                flat.Opacity = 255;                          // layer opacity is stored on the layer
                if (cel.Objects.Any(o => o is not RasterObject)) lost.Add("vector and text objects (saved as pixels)");
                var px = doc.RenderLayerPixels(flat, cel);
                var box = ImageOps.OpaqueBounds(px, doc.Width, doc.Height);
                if (box.IsEmpty) continue;
                var rgba = new byte[box.Width * box.Height * 4];
                for (int y = 0; y < box.Height; y++)
                    for (int x = 0; x < box.Width; x++)
                    {
                        int s = ((box.Y + y) * doc.Width + box.X + x) * 4, d = (y * box.Width + x) * 4;
                        rgba[d] = px[s + 2]; rgba[d + 1] = px[s + 1]; rgba[d + 2] = px[s]; rgba[d + 3] = px[s + 3];
                    }
                byte[] z;
                using (var zs = new MemoryStream())
                {
                    using (var zl = new ZLibStream(zs, CompressionLevel.Optimal, true)) zl.Write(rgba);
                    z = zs.ToArray();
                }
                chunks.Add(Chunk(0x2005, c =>
                {
                    CelHead(c, li, box.X, box.Y, 2);
                    c.Write((ushort)box.Width); c.Write((ushort)box.Height); c.Write(z);
                }));
            }
            int size = 16 + chunks.Sum(c => c.Length);
            w.Write((uint)size); w.Write((ushort)0xF1FA);
            w.Write((ushort)Math.Min(chunks.Count, 0xFFFF));
            w.Write((ushort)Math.Clamp(doc.Frames[f].Delay, 1, 65535));
            w.Write(new byte[2]); w.Write((uint)chunks.Count);
            foreach (var c in chunks) w.Write(c);
        }
        w.Flush();
        var bytes = ms.ToArray();
        BitConverter.GetBytes((uint)bytes.Length).CopyTo(bytes, 0);
        File.WriteAllBytes(path, bytes);
        string note = lost.Count > 0 ? " (" + string.Join("; ", lost) + ")" : "";
        return $"wrote {Path.GetFileName(path)}, {doc.LayerCount} layers, {n} frames, {bytes.Length / 1024.0:0.0} KB{note}";
    }

    private static void CelHead(BinaryWriter c, int layer, int x, int y, int type)
    {
        c.Write((ushort)layer); c.Write((short)x); c.Write((short)y); c.Write((byte)255);
        c.Write((ushort)type); c.Write((short)0); c.Write(new byte[5]);
    }

    private static byte[] Chunk(int type, Action<BinaryWriter> body)
    {
        using var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        w.Write(0u); w.Write((ushort)type);
        body(w);
        w.Flush();
        var b = ms.ToArray();
        BitConverter.GetBytes((uint)b.Length).CopyTo(b, 0);
        return b;
    }

    private static void Str(BinaryWriter w, string s)
    {
        var b = Encoding.UTF8.GetBytes(s);
        w.Write((ushort)b.Length); w.Write(b);
    }

    // ---------- reading ----------

    private sealed class AseLayer { public string Name = ""; public int Type; public bool Visible; public byte Opacity; public int Blend; public Layer? Flint; }

    public static AsepriteImport Read(string path)
    {
        var data = File.ReadAllBytes(path);
        if (data.Length < 128 || BitConverter.ToUInt16(data, 4) != 0xA5E0) throw new InvalidDataException("not an Aseprite file");
        var r = new BinaryReader(new MemoryStream(data));
        r.ReadUInt32(); r.ReadUInt16();
        int frames = r.ReadUInt16(), w = r.ReadUInt16(), h = r.ReadUInt16(), depth = r.ReadUInt16();
        uint flags = r.ReadUInt32();
        r.ReadUInt16(); r.ReadUInt32(); r.ReadUInt32();
        int transparent = r.ReadByte();
        if (depth is not (32 or 16 or 8)) throw new InvalidDataException($"unsupported colour depth {depth}");
        if (w == 0 || h == 0 || frames == 0) throw new InvalidDataException("the file has no pixels or no frames");
        if ((long)w * h > 16_777_216) throw new InvalidDataException($"{w}×{h} is larger than Flint opens");

        var imp = new AsepriteImport();
        var doc = new Document(w, h);
        doc.Layers.Clear(); doc.Frames.Clear();
        var layers = new List<AseLayer>();
        var palette = new uint[256];                      // ARGB
        for (int i = 0; i < 256; i++) palette[i] = 0xFF000000u | (uint)(i << 16 | i << 8 | i);
        // Linked cels point at another frame's cel of the same layer.
        var cels = new Dictionary<(int layer, int frame), RasterObject>();
        long pixelBudget = 2L * 1024 * 1024 * 1024;
        var groupHidden = new bool[256];                  // by child level: a hidden group hides what is inside it
        var links = new Dictionary<int, int>();          // layer -> frames linked to frame 0
        bool groups = false, tiles = false;

        long pos = 128;
        for (int f = 0; f < frames; f++)
        {
            if (pos + 16 > data.Length) throw new InvalidDataException("the file ends early");
            r.BaseStream.Position = pos;
            uint frameBytes = r.ReadUInt32();
            if (r.ReadUInt16() != 0xF1FA || frameBytes < 16) throw new InvalidDataException($"frame {f + 1} is damaged");
            int oldCount = r.ReadUInt16(), dur = r.ReadUInt16();
            r.ReadBytes(2);
            uint newCount = r.ReadUInt32();
            long count = newCount != 0 ? newCount : oldCount;
            doc.Frames.Add(new FrameSpec { Delay = Math.Max(1, (int)dur) });
            long cp = pos + 16;
            for (long ci = 0; ci < count && cp + 6 <= pos + frameBytes; ci++)
            {
                r.BaseStream.Position = cp;
                uint csize = r.ReadUInt32();
                int type = r.ReadUInt16();
                if (csize < 6 || cp + csize > data.Length) throw new InvalidDataException("a chunk is damaged");
                long end = cp + csize;
                switch (type)
                {
                    case 0x2004:
                    {
                        int lflags = r.ReadUInt16(), ltype = r.ReadUInt16();
                        int level = Math.Min((int)r.ReadUInt16(), 254); r.ReadUInt16(); r.ReadUInt16();
                        bool parentHidden = level > 0 && groupHidden[level - 1];
                        int blend = r.ReadUInt16(); byte op = r.ReadByte(); r.ReadBytes(3);
                        var al = new AseLayer { Name = ReadStr(r), Type = ltype, Visible = (lflags & 1) != 0 && !parentHidden, Opacity = (flags & 1) != 0 ? op : (byte)255, Blend = blend };
                        if (ltype == 0)
                        {
                            al.Flint = new Layer
                            {
                                Name = al.Name, Visible = al.Visible, Opacity = al.Opacity, Locked = (lflags & 2) == 0,
                                Blend = blend < Blends.Length ? Blends[blend] : BlendMode.Normal,
                            };
                            doc.Layers.Add(al.Flint);
                        }
                        else if (ltype == 1) { groups = true; groupHidden[level] = !al.Visible; }
                        else tiles = true;
                        layers.Add(al);
                        break;
                    }
                    case 0x2005:
                    {
                        int li = r.ReadUInt16(); int x = r.ReadInt16(), y = r.ReadInt16(); byte op = r.ReadByte();
                        int ctype = r.ReadUInt16(); r.ReadInt16(); r.ReadBytes(5);
                        if (li >= layers.Count || layers[li].Flint is not { } fl) break;
                        byte[]? canvas = null;
                        if (ctype == 1)
                        {
                            int src = r.ReadUInt16();
                            if (cels.TryGetValue((li, src), out var linked))
                            {
                                if ((pixelBudget -= (long)w * h * 4) < 0)
                                    throw new InvalidDataException("this file needs more memory than Flint allows for one document (about 2 GB of pixels)");
                                var copy = linked.CloneObject();
                                cels[(li, f)] = (RasterObject)copy;
                                fl.EnsureCel(f).Objects.Add(copy);
                            }
                            if (src == 0) links[li] = links.GetValueOrDefault(li) + 1;
                            break;
                        }
                        else if (ctype is 0 or 2)
                        {
                            int cw = r.ReadUInt16(), ch = r.ReadUInt16();
                            int bpp = depth / 8;
                            if ((long)cw * ch > 16_777_216) throw new InvalidDataException("a cel is too large");
                            byte[] raw;
                            if (ctype == 0)
                            {
                                raw = r.ReadBytes(cw * ch * bpp);
                                if (raw.Length != cw * ch * bpp) throw new InvalidDataException("a cel is cut short");
                            }
                            else
                            {
                                var comp = r.ReadBytes((int)(end - r.BaseStream.Position));
                                using var zs = new ZLibStream(new MemoryStream(comp), CompressionMode.Decompress);
                                raw = new byte[cw * ch * bpp];
                                int got = 0, k;
                                while (got < raw.Length && (k = zs.Read(raw, got, raw.Length - got)) > 0) got += k;
                            }
                            if ((pixelBudget -= (long)w * h * 4) < 0)
                                throw new InvalidDataException("this file needs more memory than Flint allows for one document (about 2 GB of pixels)");
                            canvas = new byte[w * h * 4];
                            for (int yy = 0; yy < ch; yy++)
                                for (int xx = 0; xx < cw; xx++)
                                {
                                    int cx = x + xx, cy = y + yy;
                                    if (cx < 0 || cy < 0 || cx >= w || cy >= h) continue;
                                    int s = (yy * cw + xx) * bpp, d = (cy * w + cx) * 4;
                                    byte R, G, B, A;
                                    if (depth == 32) { R = raw[s]; G = raw[s + 1]; B = raw[s + 2]; A = raw[s + 3]; }
                                    else if (depth == 16) { R = G = B = raw[s]; A = raw[s + 1]; }
                                    else
                                    {
                                        int ix = raw[s];
                                        if (ix == transparent) continue;
                                        uint c = palette[ix];
                                        R = (byte)(c >> 16); G = (byte)(c >> 8); B = (byte)c; A = (byte)(c >> 24);
                                    }
                                    canvas[d] = B; canvas[d + 1] = G; canvas[d + 2] = R; canvas[d + 3] = A;
                                }
                        }
                        else { tiles = true; break; }
                        if (canvas == null) break;
                        var obj = new RasterObject(ImageOps.FromPixels(w, h, canvas)) { Name = "Bitmap", Opacity = op };
                        cels[(li, f)] = obj;
                        fl.EnsureCel(f).Objects.Add(obj);
                        break;
                    }
                    case 0x2018:
                    {
                        int tc = r.ReadUInt16(); r.ReadBytes(8);
                        for (int t = 0; t < tc; t++)
                        {
                            int a = r.ReadUInt16(), b = r.ReadUInt16(); r.ReadByte(); r.ReadUInt16(); r.ReadBytes(6);
                            byte cr = r.ReadByte(), cg = r.ReadByte(), cb = r.ReadByte(); r.ReadByte();
                            doc.Tags.Add(new AnimTag { Name = ReadStr(r), Start = a, End = Math.Max(a, b), Colour = Color.FromArgb(cr, cg, cb) });
                        }
                        break;
                    }
                    case 0x2019:
                    {
                        r.ReadUInt32(); uint first = r.ReadUInt32(), last = r.ReadUInt32(); r.ReadBytes(8);
                        for (uint i = first; i <= last && i < 256 && r.BaseStream.Position < end; i++)
                        {
                            int ef = r.ReadUInt16();
                            byte R = r.ReadByte(), G = r.ReadByte(), B = r.ReadByte(), A = r.ReadByte();
                            palette[i] = (uint)(A << 24 | R << 16 | G << 8 | B);
                            if ((ef & 1) != 0) ReadStr(r);
                        }
                        break;
                    }
                    case 0x0004:
                    case 0x0011:
                    {
                        int packets = r.ReadUInt16(), idx = 0;
                        for (int p = 0; p < packets; p++)
                        {
                            idx += r.ReadByte();
                            int nc = r.ReadByte(); if (nc == 0) nc = 256;
                            for (int i = 0; i < nc && idx < 256; i++, idx++)
                            {
                                int R = r.ReadByte(), G = r.ReadByte(), B = r.ReadByte();
                                if (type == 0x0011) { R = R * 255 / 63; G = G * 255 / 63; B = B * 255 / 63; }
                                palette[idx] = (uint)(0xFF << 24 | R << 16 | G << 8 | B);
                            }
                        }
                        break;
                    }
                }
                cp = end;
            }
            pos += frameBytes;
        }
        // A layer whose every later frame links to frame 0 is a shared layer.
        foreach (var (li, linkedCount) in links)
            if (layers[li].Flint is { } fl && frames > 1 && linkedCount == frames - 1 && fl.Cels.ContainsKey(0))
            {
                var keep = fl.Cels[0];
                fl.Cels.Clear(); fl.Cels[0] = keep; fl.Shared = true;
            }
        if (doc.Layers.Count == 0) doc.Layers.Add(new Layer { Name = "Layer 1" });
        foreach (var t in doc.Tags) { t.Start = Math.Min(t.Start, frames - 1); t.End = Math.Min(t.End, frames - 1); }
        if (groups) imp.Warnings.Add("layer groups became plain layers (Flint has no layer groups)");
        if (tiles) imp.Warnings.Add("tilemap layers were skipped");
        imp.Doc = doc;
        return imp;
    }

    private static string ReadStr(BinaryReader r)
    {
        int n = r.ReadUInt16();
        return Encoding.UTF8.GetString(r.ReadBytes(n));
    }
}
