/* Export, kept out of the window so it can run on a worker thread and be
 * tested without one. The caller blocks editing while it runs (a modal
 * progress dialog), which is what makes reading the document's bitmaps from
 * another thread safe: nothing else touches them meanwhile. */
using System.Drawing.Imaging;

namespace Flint;

public enum ExportKind { Gif, Apng, WebP, WebM, Mp4, Png, Sheet, Atlas, Sequence, Wav, Jpeg, Bmp, Tiff, Png8, WebPStill, Mov, MovProRes, Mkv, Avif, Aseprite, Html }

public sealed class ExportJob
{
    public Document Doc = null!;
    public ExportKind Kind;
    /// <summary>File, or folder for a PNG sequence.</summary>
    public string Path = "";
    public int From, To, Current;

    public int GifColours = 256;
    public bool Dither;
    /// <summary>Full GIF settings from the export preview; null = GifColours/Dither.</summary>
    public GifOptions? Gif;
    public bool Loop = true;
    public bool IncludeAudio = true;
    /// <summary>WebM/MP4 CRF, or for WebP 0 = lossless, else quality 1-100.</summary>
    public int Quality = 30;
    public Color Matte = Color.Black;
    public int SheetCols;
    /// <summary>Video: host size, letterbox, repeat, frame rate, size cap. Null = as before.</summary>
    public VideoOptions? Video;
}

public static class Exporter
{
    public static bool IsVideo(ExportKind k) => k is ExportKind.WebM or ExportKind.Mp4 or ExportKind.Mov or ExportKind.MovProRes or ExportKind.Mkv or ExportKind.Avif;
    public static bool HasAudio(ExportKind k) => k is ExportKind.Wav or ExportKind.Atlas || (IsVideo(k) && k != ExportKind.Avif);
    public static bool NeedsFfmpeg(ExportKind k) => IsVideo(k) || k is ExportKind.WebP or ExportKind.WebPStill;

    /// <summary>Run an export; returns a one-line result for the status bar.</summary>
    public static string Run(ExportJob j, IProgress<(double, string)>? p, CancellationToken ct)
    {
        if (j.Kind == ExportKind.Wav) return SaveWav(j);
        if (j.Kind == ExportKind.Atlas) return SaveAtlas(j, p);
        if (j.Kind == ExportKind.Aseprite) return AsepriteFile.Write(j.Doc, j.Path, j.From, j.To);

        // One-frame formats export exactly the current frame, wherever it is and whatever its export flag.
        if (j.Kind == ExportKind.Png || StillExport.IsStill(j.Kind)) { j.From = j.To = j.Current; }
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
                case ExportKind.Mov:
                case ExportKind.MovProRes:
                case ExportKind.Mkv:
                case ExportKind.Avif:
                {
                    var kind = j.Kind switch
                    {
                        ExportKind.Mp4 => WebmExport.Kind.Mp4, ExportKind.Mov => WebmExport.Kind.Mov,
                        ExportKind.MovProRes => WebmExport.Kind.MovProRes, ExportKind.Mkv => WebmExport.Kind.Mkv,
                        ExportKind.Avif => WebmExport.Kind.Avif, _ => WebmExport.Kind.WebM,
                    };
                    var r = WebmExport.Encode(frames, j.Path, kind, j.Quality, j.Loop, Mix(j), j.Matte, ct,
                        new Progress<(double, string)>(t => Sub(0.4, 1, t.Item1, t.Item2)), j.Video);
                    if (!r.Ok) throw new InvalidOperationException(r.Message);
                    return r.Message + (!WebmExport.KeepsAlpha(kind)
                        ? $" — {j.Kind.ToString().ToUpperInvariant()} has no transparency; it was flattened onto the background colour" : "");
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

                case ExportKind.Html:
                    return HtmlExport.Save(j, frames);

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
        var which = j.Doc.ExportedFrames(j.From, j.To).ToList();
        if (which.Count == 0) throw new InvalidOperationException("every frame in this range is set not to export (Frame > Include when exporting)");
        int n = which.Count;
        try
        {
            foreach (int i in which)
            {
                ct.ThrowIfCancellationRequested();
                list.Add(new Frame(j.Doc.RenderFrame(i), j.Doc.Frames[i].Delay));
                p?.Report((0.4 * list.Count / n, $"rendering frame {list.Count} of {n}"));
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
        var which = j.Doc.ExportedFrames(j.From, j.To).ToList();
        if (which.Count == j.To - j.From + 1)
        {
            double start = Playback.FrameStart(d, j.From);
            double end = Playback.FrameStart(d, j.To + 1);
            return AudioMixer.Mix(j.Doc.Audio, start, end - start);
        }
        // Frames left out take their sound with them, so picture and sound stay together.
        var parts = which.Select(i => AudioMixer.Mix(j.Doc.Audio, Playback.FrameStart(d, i), d[i])).ToList();
        var all = new float[parts.Sum(p => p.Length)];
        int at = 0;
        foreach (var p in parts) { Array.Copy(p, 0, all, at, p.Length); at += p.Length; }
        return all;
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

    /// <summary>The GIF settings a job uses: its own, or the simple colours/dither pair.</summary>
    public static GifOptions GifFor(ExportJob j) => j.Gif?.Clone() ?? new GifOptions
    {
        Colours = j.GifColours, Dither = j.Dither ? DitherKind.FloydSteinberg : DitherKind.None,
    };

    private static string SaveGif(ExportJob j, List<Frame> frames, Action<double> sub, CancellationToken ct)
    {
        int w = frames[0].Bmp.Width, h = frames[0].Bmp.Height;
        var all = frames.Select(f => ImageOps.GetPixels(f.Bmp)).ToList();
        var o = GifFor(j);
        // One palette across every frame: what stops colours shimmering.
        var ix = GifPalette.Index(all, w, h, o, ct);
        sub(0.5);
        var gifFrames = ix.Indices.Select((idx, i) => new GifFrame { Indices = idx, DelayMs = frames[i].Delay }).ToList();
        byte[] gif = GifEncoder.Encode(w, h, gifFrames, ix.Palette, ix.TransparentIndex, j.Loop, o.Optimize, o.Lossy, ct);
        File.WriteAllBytes(j.Path, gif);
        return $"wrote {System.IO.Path.GetFileName(j.Path)}, {gif.Length / 1024.0:0.0} KB, {ix.Palette.Count} colours";
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
