using System.Buffers.Binary;
using System.Drawing.Imaging;
using System.IO.Compression;
using System.Text;

namespace Flint;

/// <summary>Writes a Fireworks 8 document with one editable bitmap per layer and frame.</summary>
internal static class FireworksPngWriter
{
    private const int TileSide = 128;
    private const int PngHeaderSize = 33; // Signature and fixed-size IHDR chunk.

    public static bool HasCrossLayerBlending(Document doc) => doc.Layers.Any(layer =>
        layer.Blend != BlendMode.Normal || layer.Cels.Values.Any(cel =>
            cel.Objects.Any(obj => obj.Blend != BlendMode.Normal)));

    public static void Save(Document doc, string path, CancellationToken ct, IProgress<(double, string)>? progress = null)
    {
        ct.ThrowIfCancellationRequested();
        path = Path.GetFullPath(path);
        if (!Path.GetExtension(path).Equals(".png", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Choose a filename ending in .png for a Fireworks document.", nameof(path));
        string directory = Path.GetDirectoryName(path)!;
        string pending = Path.Combine(directory, $".flint-{Guid.NewGuid():N}.tmp");
        string scratch = Path.Combine(directory, $".flint-{Guid.NewGuid():N}.tmp");
        try
        {
            using (var output = new FileStream(pending, FileMode.CreateNew, FileAccess.Write))
            using (var tiles = new FileStream(scratch, FileMode.CreateNew, FileAccess.ReadWrite,
                FileShare.None, 81920, FileOptions.DeleteOnClose))
            {
                Write(doc, output, tiles, ct, progress);
            }
            ct.ThrowIfCancellationRequested();
            File.Move(pending, path, overwrite: true);
        }
        finally { File.Delete(pending); }
    }

    private static void Write(Document doc, Stream output, Stream tiles, CancellationToken ct,
        IProgress<(double, string)>? progress)
    {
        byte[] flat = doc.RenderPixels(0);
        using var png = new MemoryStream();
        using (var bitmap = ImageOps.FromPixels(doc.Width, doc.Height, flat))
            bitmap.Save(png, ImageFormat.Png);
        output.Write(png.GetBuffer(), 0, PngHeaderSize);

        // The native marker identifies version 4 of the private Fireworks document data.
        var marker = new byte[72];
        BinaryPrimitives.WriteUInt32BigEndian(marker, 0xFADECAFE);
        BinaryPrimitives.WriteUInt32BigEndian(marker.AsSpan(4), 4);
        ApngEncoder.WriteChunk(output, "mkBF", marker);
        var writer = new TileWriter(tiles, ct);

        var layers = doc.Layers.Where(layer => !IsImportReference(layer)).ToArray();
        WriteTree(output, "mkTS", doc.Width, doc.Height, doc.FrameCount, tree =>
        {
            tree.Begin("MSL");
            tree.End();
            tree.Begin("EXP");
            tree.Begin("FNL");
            for (int f = 0; f < doc.FrameCount; f++)
            {
                tree.Begin("FNM");
                tree.Int("FNP", f);
                tree.Int("FND", (int)Math.Max(1, (doc.Frames[f].Delay + 5L) / 10));
                tree.Bool("VIS", true);
                tree.End();
            }
            tree.End(); // FNL
            tree.End(); // EXP
            tree.Begin("LYL");
            for (int i = 0; i < layers.Length; i++)
            {
                var layer = layers[i];
                progress?.Report(((double)i / layers.Length, $"Writing {layer.Name}…"));
                WriteLayer(tree, layer.Name, layer.Shared, false, layer.Visible, layer.Locked, doc.FrameCount, (cellTree, f) =>
                {
                    ct.ThrowIfCancellationRequested();
                    if (layer.CelAt(f) is not { IsEmpty: false } cel) return;
                    var pixels = doc.RenderLayerPixels(layer, cel);
                    var bounds = Sprites.ContentRect(pixels, doc.Width, doc.Height);
                    if (!bounds.IsEmpty) writer.Bitmap(cellTree, pixels, doc.Width, bounds);
                });
            }
            WriteLayer(tree, "Web Layer", shared: false, web: true, visible: true, locked: false,
                doc.FrameCount, (_, _) => { });
            tree.End(); // LYL
        });

        WriteTree(output, "mkBS", doc.Width, doc.Height, 1, tree =>
        {
            tree.Begin("LYL");
            WriteLayer(tree, "Layer 1", shared: false, web: false, visible: true, locked: false, 1,
                (cellTree, _) => writer.Bitmap(cellTree, flat, doc.Width, new Rectangle(0, 0, doc.Width, doc.Height)));
            tree.End();
        });
        progress?.Report((1, "Writing PNG…"));
        tiles.Position = 0;
        tiles.CopyToAsync(output, ct).GetAwaiter().GetResult();
        output.Write(png.GetBuffer(), PngHeaderSize, checked((int)png.Length - PngHeaderSize));
    }

    private static bool IsImportReference(Layer layer) => layer.Name == FireworksPng.ReferenceLayerName &&
        !layer.Visible && layer.Shared && layer.Locked &&
        layer.CelAt(0) is { Objects.Count: 1 } cel && cel.Objects[0].Name == "Flattened picture";

    private static void WriteLayer(Tree tree, string name, bool shared, bool web, bool visible, bool locked,
        int frames, Action<Tree, int> writeElements)
    {
        tree.Begin("LAY");
        tree.Bool("BKG", false);
        tree.Bool("URL", web);
        tree.String("LNM", name);
        tree.Bool("MSL", shared);
        tree.Bool("MCL", true);
        tree.Bool("DIS", true);
        tree.Begin("CLL");
        if (shared)
        {
            // Fireworks reads a cell for each frame, including on shared layers.
            // Reuse the encoded cell and its tile IDs so the bitmap is stored once.
            using var cell = new MemoryStream();
            WriteCell(new Tree(cell), 0);
            for (int frame = 0; frame < frames; frame++)
                tree.Raw(cell.GetBuffer().AsSpan(0, checked((int)cell.Length)));
        }
        else
            for (int frame = 0; frame < frames; frame++) WriteCell(tree, frame);
        tree.End(); // CLL
        tree.Begin("VIF");
        for (int frame = 0; frame < frames; frame++) tree.Bool("VIS", visible);
        tree.End();
        tree.End(); // LAY

        void WriteCell(Tree cellTree, int frame)
        {
            cellTree.Begin("CEL");
            cellTree.Bool("LCK", locked);
            cellTree.Begin("ELM");
            writeElements(cellTree, frame);
            cellTree.End();
            cellTree.End();
        }
    }

    private static void WriteTree(Stream output, string chunk, int width, int height, int frames, Action<Tree> writeBody)
    {
        using var data = new MemoryStream();
        using (var zlib = new ZLibStream(data, CompressionLevel.Optimal, leaveOpen: true))
        {
            var tree = new Tree(zlib);
            tree.Begin("MKB");
            tree.Int("FRC", frames);
            tree.Float("XLC", 0);
            tree.Float("YLC", 0);
            tree.Float("WID", width);
            tree.Float("HIT", height);
            tree.Float("RES", 72);
            tree.Int("RSU", 0);
            tree.Int("BGC", 0);
            writeBody(tree);
            tree.End();
        }
        ApngEncoder.WriteChunk(output, chunk, data.ToArray());
    }

    private sealed class Tree(Stream output)
    {
        private void Write(string value) => output.Write(Encoding.ASCII.GetBytes(value));
        public void Raw(ReadOnlySpan<byte> data) => output.Write(data);
        public void Begin(string tag) => Write(tag + "v{");
        public void End() => output.WriteByte((byte)'}');
        public void Int(string tag, int value) => Write($"{tag}i{{{value:x}}}");
        public void Bool(string tag, bool value) => Write($"{tag}b{{{(value ? '1' : '0')}}}");
        public void Float(string tag, int value) => Write(FormattableString.Invariant($"{tag}f{{{value}}}"));

        public void String(string tag, string value)
        {
            if (value.Length > ushort.MaxValue) throw new InvalidDataException("A layer name is too long for Fireworks.");
            Write(tag + "s{");
            Span<byte> length = stackalloc byte[2];
            BinaryPrimitives.WriteUInt16BigEndian(length, (ushort)value.Length);
            output.Write(length);
            output.Write(Encoding.BigEndianUnicode.GetBytes(value));
            End();
        }
    }

    private sealed class TileWriter(Stream tiles, CancellationToken ct)
    {
        private readonly byte[] _pixels = new byte[TileSide * TileSide * 4];
        private readonly byte[] _header = new byte[76];
        private int _nextId;

        public void Bitmap(Tree tree, byte[] bgra, int stride, Rectangle bounds)
        {
            tree.Begin("IMG");
            tree.Float("XOF", 0);
            tree.Float("YOF", 0);
            tree.Float("XLC", bounds.X);
            tree.Float("YLC", bounds.Y);
            tree.Int("WPX", bounds.Width);
            tree.Int("HPX", bounds.Height);
            tree.Int("TSZ", TileSide);
            tree.Begin("TIL");
            for (int y = bounds.Top; y < bounds.Bottom; y += TileSide)
                for (int x = bounds.Left; x < bounds.Right; x += TileSide)
                    tree.Int("TID", WriteTile(bgra, stride, x, y, bounds));
            tree.End(); // TIL
            tree.Begin("DTA");
            tree.End();
            tree.End(); // IMG
        }

        private int WriteTile(byte[] bgra, int stride, int x, int y, Rectangle bounds)
        {
            ct.ThrowIfCancellationRequested();
            Array.Clear(_pixels);
            int width = Math.Min(TileSide, bounds.Right - x), height = Math.Min(TileSide, bounds.Bottom - y);
            for (int row = 0; row < height; row++)
            {
                int src = ((y + row) * stride + x) * 4;
                int dst = row * TileSide * 4;
                for (int col = 0; col < width; col++, src += 4, dst += 4)
                {
                    _pixels[dst] = bgra[src + 3];
                    _pixels[dst + 1] = bgra[src + 2];
                    _pixels[dst + 2] = bgra[src + 1];
                    _pixels[dst + 3] = bgra[src];
                }
            }
            int id = checked(++_nextId);
            BinaryPrimitives.WriteUInt32BigEndian(_header, 0xFACECAFE);
            BinaryPrimitives.WriteInt32BigEndian(_header.AsSpan(4), id);
            using var data = new MemoryStream();
            data.Write(_header);
            using (var zlib = new ZLibStream(data, CompressionLevel.Optimal, leaveOpen: true))
                zlib.Write(_pixels);
            ApngEncoder.WriteChunk(tiles, "mkBT", data.ToArray());
            return id;
        }
    }
}
