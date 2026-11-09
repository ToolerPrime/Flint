using System.Drawing.Imaging;

namespace Flint;

public static partial class FireworksTest
{
    private static void SaveEditablePng(string? fixturePath)
    {
        string path = fixturePath ?? Path.Combine(Path.GetTempPath(), $"flint-fireworks-{Guid.NewGuid():N}.png");
        var doc = new Document(180, 145);
        try
        {
            doc.Frames[0].Delay = 100;
            doc.InsertFrame(1, new FrameSpec { Delay = 253 });
            var baseLayer = doc.Layers[0];
            baseLayer.Name = "Red base {背景}";
            baseLayer.Shared = true;
            baseLayer.Locked = true;
            baseLayer.EnsureCel(0).Objects.Add(ColourBlock(142, 137, Color.Red, 3, 4));
            var moving = doc.AddLayer("Blue motion");
            moving.EnsureCel(0).Objects.Add(ColourBlock(35, 45, Color.FromArgb(160, 0, 0, 255), 125, 72));
            moving.EnsureCel(1).Objects.Add(ColourBlock(35, 45, Color.FromArgb(160, 0, 0, 255), 138, 84));
            var hidden = doc.AddLayer("Hidden green");
            hidden.Visible = false;
            hidden.EnsureCel(0).Objects.Add(ColourBlock(3, 3, Color.Green, 0, 0));

            FireworksPngWriter.Save(doc, path, CancellationToken.None);
            var sharedCells = FwContainer.Read(File.ReadAllBytes(path)).Root.Get("LYL")!.All("LAY").First()
                .Get("CLL")!.All("CEL").Select(cel => cel.Descendants("TID").Select(tile => tile.Value).ToArray()).ToArray();
            Check(sharedCells.Length == 2 && sharedCells[0].Length == 4 && sharedCells[0].SequenceEqual(sharedCells[1]),
                "shared layers store a cell for each native frame and reuse the bitmap tile IDs");
            var imported = FireworksPng.Open(path);
            try
            {
                Check(imported.Layers == 3 && imported.Frames == 2 && imported.Warnings.Count == 0,
                    "saved PNG reopens with three editable layers and two frames");
                var savedBase = imported.Doc.Layers[1];
                Check(savedBase.Name == baseLayer.Name && savedBase.Shared && savedBase.Locked &&
                    !imported.Doc.Layers[3].Visible && imported.Doc.Layers[3].CelAt(0)?.Objects.Count == 1,
                    "Unicode layer names, sharing, locks, and hidden artwork round-trip");
                Check(imported.Doc.Frames.Select(f => f.Delay).SequenceEqual(new[] { 100, 250 }),
                    "animation delays round to Fireworks hundredths of a second");
                Check(Enumerable.Range(0, 2).All(f => imported.Doc.RenderPixels(f).SequenceEqual(doc.RenderPixels(f))),
                    "layered pixels match across tile boundaries, with straight alpha and moved artwork");
                using var preview = new Bitmap(path);
                Check(ImageOps.GetPixels(preview).SequenceEqual(doc.RenderPixels(0)),
                    "ordinary PNG readers see the exact first-frame composite");
            }
            finally { imported.Doc.Dispose(); }

            using var cancel = new CancellationTokenSource();
            var saved = File.ReadAllBytes(path);
            cancel.Cancel();
            bool cancelled = false;
            try { FireworksPngWriter.Save(doc, path, cancel.Token); }
            catch (OperationCanceledException) { cancelled = true; }
            Check(cancelled && File.ReadAllBytes(path).SequenceEqual(saved),
                "cancellation leaves the existing destination intact");
        }
        finally
        {
            doc.Dispose();
            if (fixturePath == null) File.Delete(path);
        }

        static RasterObject ColourBlock(int width, int height, Color colour, int x, int y)
        {
            var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            var pixels = new byte[width * height * 4];
            for (int i = 0; i < pixels.Length; i += 4)
            {
                pixels[i] = colour.B;
                pixels[i + 1] = colour.G;
                pixels[i + 2] = colour.R;
                pixels[i + 3] = colour.A;
            }
            ImageOps.SetPixels(bitmap, pixels);
            return new RasterObject(bitmap) { X = x, Y = y };
        }
    }
}
