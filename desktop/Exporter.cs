/* Export, kept out of the window so it can run on a worker thread and be
 * tested without one. The caller blocks editing while it runs (a modal
 * progress dialog), which is what makes reading the document's bitmaps from
 * another thread safe: nothing else touches them meanwhile. */
using System.Drawing.Imaging;

namespace Flint;

public enum ExportKind { Gif, Apng, WebP, WebM, Mp4, Png, Sheet, Atlas, Sequence, Wav, Jpeg, Bmp, Tiff, Png8, WebPStill }

public sealed class ExportJob
{
    public Document Doc = null!;
    public ExportKind Kind;
    /// <summary>File, or folder for a PNG sequence.</summary>
    public string Path = "";
    public int From, To, Current;

    public int GifColours = 256;
    public bool Dither;
    public bool Loop = true;
    public bool IncludeAudio = true;
    /// <summary>WebM/MP4 CRF, or for WebP 0 = lossless, else quality 1-100.</summary>
    public int Quality = 30;
    public Color Matte = Color.Black;
    public int SheetCols;
}

public static class Exporter
{
    public static bool HasAudio(ExportKind k) => k is ExportKind.WebM or ExportKind.Mp4 or ExportKind.Wav or ExportKind.Atlas;
    public static bool NeedsFfmpeg(ExportKind k) => k is ExportKind.WebM or ExportKind.Mp4 or ExportKind.WebP or ExportKind.WebPStill;

    /// <summary>Run an export; returns a one-line result for the status bar.</summary>
    public static string Run(ExportJob j, IProgress<(double, string)>? p, CancellationToken ct)
    {
        if (j.Kind == ExportKind.Wav) return SaveWav(j);
        if (j.Kind == ExportKind.Atlas) return SaveAtlas(j, p);

        var frames = Render(j, p, ct);
        try
        {
            void Sub(double from, double to, double f, string m) => p?.Report((from + (to - from) * f, m));
            switch (j.Kind)
            {
                case ExportKind.Gif:
                    return SaveGif(j, frames, f => Sub(0.4, 1, f, "building GIF"), ct);

                case ExportKind.Apng:
                {
                    var bytes = ApngEncoder.Encode(frames, j.Loop, ct,
                        new Progress<double>(f => Sub(0.4, 1, f, "compressing frames")));
                    File.WriteAllBytes(j.Path, bytes);
                    return $"wrote {System.IO.Path.GetFileName(j.Path)}, {bytes.Length / 1024.0:0.0} KB, {frames.Count} frames, full alpha";
                }

                case ExportKind.WebP:
                {
                    var r = WebpWriter.Encode(frames, j.Path, j.Quality, j.Loop, ct,
                        new Progress<(double, string)>(t => Sub(0.4, 1, t.Item1, t.Item2)));
                    if (!r.Ok) throw new InvalidOperationException(r.Message);
                    return r.Message;
                }

                case ExportKind.WebM:
                case ExportKind.Mp4:
                {
                    var kind = j.Kind == ExportKind.Mp4 ? WebmExport.Kind.Mp4 : WebmExport.Kind.WebM;
                    var r = WebmExport.Encode(frames, j.Path, kind, j.Quality, j.Loop, Mix(j), j.Matte, ct,
                        new Progress<(double, string)>(t => Sub(0.4, 1, t.Item1, t.Item2)));
                    if (!r.Ok) throw new InvalidOperationException(r.Message);
                    return r.Message + (j.Kind == ExportKind.Mp4
                        ? " — MP4 has no transparency; it was flattened onto the background colour" : "");
                }

                case ExportKind.Jpeg:
                case ExportKind.Bmp:
                case ExportKind.Tiff:
                case ExportKind.Png8:
                case ExportKind.WebPStill:
                {
                    var f = frames[Math.Clamp(j.Current - j.From, 0, frames.Count - 1)];
                    return StillExport.Save(j, ImageOps.GetPixels(f.Bmp), f.Bmp.Width, f.Bmp.Height, ct);
                }

                case ExportKind.Png:
                    frames[Math.Clamp(j.Current - j.From, 0, frames.Count - 1)].Bmp.Save(j.Path, ImageFormat.Png);
                    return "wrote " + System.IO.Path.GetFileName(j.Path);

                case ExportKind.Sheet:
                    return SaveSheet(j, frames);

                case ExportKind.Sequence:
                    for (int i = 0; i < frames.Count; i++)
                    {
                        ct.ThrowIfCancellationRequested();
                        frames[i].Bmp.Save(System.IO.Path.Combine(j.Path, $"frame{i + 1:D4}.png"), ImageFormat.Png);
                        Sub(0.4, 1, (i + 1) / (double)frames.Count, $"writing {i + 1} of {frames.Count}");
                    }
                    return $"wrote {frames.Count} PNGs to {j.Path}";
            }
            throw new NotSupportedException(j.Kind.ToString());
        }
        finally { foreach (var f in frames) f.Bmp.Dispose(); }
    }

    public static List<Frame> Render(ExportJob j, IProgress<(double, string)>? p, CancellationToken ct)
    {
        var list = new List<Frame>();
        int n = j.To - j.From + 1;
        try
        {
            for (int i = j.From; i <= j.To; i++)
            {
                ct.ThrowIfCancellationRequested();
                list.Add(new Frame(j.Doc.RenderFrame(i), j.Doc.Frames[i].Delay));
                p?.Report((0.4 * (i - j.From + 1) / n, $"rendering frame {i - j.From + 1} of {n}"));
            }
            return list;
        }
        catch
        {
            foreach (var f in list) f.Bmp.Dispose();
            throw;
        }
    }

    /// <summary>The audio mix exactly covering the exported frames, or null.</summary>
    public static float[]? Mix(ExportJob j)
    {
        if (!j.IncludeAudio || j.Doc.Audio.IsEmpty) return null;
        var d = j.Doc.Frames.Select(f => Math.Max(1, f.Delay)).ToArray();
        double start = Playback.FrameStart(d, j.From);
        double end = Playback.FrameStart(d, j.To + 1);
        return AudioMixer.Mix(j.Doc.Audio, start, end - start);
    }

    /// <summary>
    /// Packed atlas PNG plus Aseprite-style JSON beside it. With audio, also a
    /// WAV of the same range and a cue file saying when each clip starts —
    /// a sprite sheet cannot carry sound itself.
    /// </summary>
    private static string SaveAtlas(ExportJob j, IProgress<(double, string)>? p)
    {
        p?.Report((0.1, "packing atlas"));
        string png = j.Path;
        string stem = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(png) ?? "", System.IO.Path.GetFileNameWithoutExtension(png));
        var a = Sprites.Atlas(j.Doc, j.From, j.To, System.IO.Path.GetFileName(png));
        using (var bmp = ImageOps.FromPixels(a.Width, a.Height, a.Pixels)) bmp.Save(png, ImageFormat.Png);
        File.WriteAllText(stem + ".json", a.Json);
        string extra = "";
        if (j.IncludeAudio && !j.Doc.Audio.IsEmpty)
        {
            p?.Report((0.7, "writing audio"));
            var mix = Mix(j)!;
            AudioIO.WritePcm16Wav(stem + ".wav", mix);
            File.WriteAllText(stem + ".audio.json", Sprites.AudioCues(j.Doc, j.From, j.To, System.IO.Path.GetFileName(stem + ".wav")));
            extra = ", plus .wav and .audio.json for the sound";
        }
        return $"wrote {System.IO.Path.GetFileName(png)} ({a.Width}×{a.Height}, {a.UniqueFrames} unique of {j.To - j.From + 1} frames) and .json{extra}";
    }

    private static string SaveWav(ExportJob j)
    {
        if (j.Doc.Audio.IsEmpty) throw new InvalidOperationException("there is no audio to export");
        var d = j.Doc.Frames.Select(f => Math.Max(1, f.Delay)).ToArray();
        bool whole = j.From == 0 && j.To == j.Doc.FrameCount - 1;
        double start = whole ? 0 : Playback.FrameStart(d, j.From);
        double end = whole ? Math.Max(Playback.TotalMs(d), j.Doc.Audio.EndMs) : Playback.FrameStart(d, j.To + 1);
        AudioIO.WritePcm16Wav(j.Path, AudioMixer.Mix(j.Doc.Audio, start, end - start));
        return $"wrote {System.IO.Path.GetFileName(j.Path)}, {(end - start) / 1000:0.00}s, 48 kHz stereo";
    }

    private static string SaveGif(ExportJob j, List<Frame> frames, Action<double> sub, CancellationToken ct)
    {
        int w = frames[0].Bmp.Width, h = frames[0].Bmp.Height;
        var all = frames.Select(f => ImageOps.GetPixels(f.Bmp)).ToList();

        bool anyTransparent = all.Any(px =>
        {
            for (int i = 3; i < px.Length; i += 4) if (px[i] < 128) return true;
            return false;
        });
        int offset = anyTransparent ? 1 : 0;

        // One palette across every frame: what stops colours shimmering.
        var hist = Quantizer.BuildHistogram(all, out double total);
        var colors = Quantizer.Quantize(hist, total, j.GifColours - offset);
        if (colors.Count == 0) throw new InvalidOperationException("no opaque pixels to build a palette from");
        var ix = new Quantizer.Indexer(colors);
        sub(0.2);

        var palette = colors;
        if (anyTransparent)
            palette = new Palette
            {
                R = new byte[] { 0 }.Concat(colors.R).ToArray(),
                G = new byte[] { 0 }.Concat(colors.G).ToArray(),
                B = new byte[] { 0 }.Concat(colors.B).ToArray(),
            };

        var gifFrames = new List<GifFrame>();
        for (int i = 0; i < all.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            byte[] indices;
            if (j.Dither) indices = ImageOps.Dither(all[i], w, h, colors, ix, offset);
            else
            {
                indices = new byte[w * h];
                var px = all[i];
                for (int q = 0; q < indices.Length; q++)
                {
                    int o = q * 4;
                    indices[q] = px[o + 3] < 128 ? (byte)0 : (byte)(ix.Nearest(px[o + 2], px[o + 1], px[o]) + offset);
                }
            }
            gifFrames.Add(new GifFrame { Indices = indices, DelayMs = frames[i].Delay });
            sub(0.2 + 0.7 * (i + 1) / all.Count);
        }

        byte[] gif = GifEncoder.Encode(w, h, gifFrames, palette, anyTransparent ? 0 : -1, j.Loop);
        File.WriteAllBytes(j.Path, gif);
        return $"wrote {System.IO.Path.GetFileName(j.Path)}, {gif.Length / 1024.0:0.0} KB, {palette.Count} colours";
    }

    private static string SaveSheet(ExportJob j, List<Frame> frames)
    {
        int w = frames[0].Bmp.Width, h = frames[0].Bmp.Height;
        int cols = j.SheetCols <= 0 ? frames.Count : j.SheetCols;
        int rows = (int)Math.Ceiling(frames.Count / (double)cols);
        var sheet = new byte[w * cols * h * rows * 4];
        for (int i = 0; i < frames.Count; i++)
            ImageOps.Over(sheet, w * cols, h * rows, ImageOps.GetPixels(frames[i].Bmp), w, h,
                          i % cols * w, i / cols * h, 255);
        using var bmp = ImageOps.FromPixels(w * cols, h * rows, sheet);
        bmp.Save(j.Path, ImageFormat.Png);
        return $"wrote {System.IO.Path.GetFileName(j.Path)}, {cols}×{rows}";
    }
}
