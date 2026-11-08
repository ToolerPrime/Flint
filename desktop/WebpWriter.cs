/* Animated WebP.
 *
 * ffmpeg compresses each frame to a still WebP (lossless by default); this
 * file then assembles the animated container itself: VP8X header, ANIM, and
 * one ANMF chunk per frame. Done this way because ffmpeg's animated WebP
 * writer, given per-frame delays, got the last frame wrong (192 ms for 60),
 * and the constant-frame-rate workaround used for video would duplicate
 * frames. Here every frame keeps its exact delay and appears exactly once.
 */
using System.Buffers.Binary;
using System.Drawing.Imaging;

namespace Flint;

public static class WebpWriter
{
    /// <param name="quality">0 = lossless (exact pixels), otherwise lossy 1-100.</param>
    public static WebmExport.Result Encode(IList<Frame> frames, string outPath, int quality, bool loop,
                                           CancellationToken ct, IProgress<(double, string)>? progress = null)
    {
        var ffmpeg = WebmExport.Find();
        if (ffmpeg == null)
            return new WebmExport.Result { Ok = false, Message = "ffmpeg not found (winget install Gyan.FFmpeg)" };
        if (frames.Count == 0)
            return new WebmExport.Result { Ok = false, Message = "no frames" };

        int w = frames[0].Bmp.Width, h = frames[0].Bmp.Height;
        string work = Path.Combine(Path.GetTempPath(), "flint-webp-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(work);
        try
        {
            for (int i = 0; i < frames.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                frames[i].Bmp.Save(Path.Combine(work, $"f{i:D5}.png"), ImageFormat.Png);
                progress?.Report((0.3 * (i + 1) / frames.Count, $"preparing frame {i + 1} of {frames.Count}"));
            }

            // One ffmpeg run turns the whole sequence into still WebPs.
            string codec = quality <= 0
                ? "-c:v libwebp -lossless 1 -pix_fmt bgra"
                : $"-c:v libwebp -lossless 0 -quality {Math.Clamp(quality, 1, 100)} -pix_fmt yuva420p";
            string args = $"-y -nostats -loglevel error -i \"{Path.Combine(work, "f%05d.png")}\" {codec} " +
                          $"-f image2 \"{Path.Combine(work, "w%05d.webp")}\"";
            progress?.Report((0.35, "compressing frames with ffmpeg…"));
            var (ok, msg) = WebmExport.RunFfmpeg(ffmpeg, args, 60_000 + frames.Count * 2000, ct);
            if (!ok) return new WebmExport.Result { Ok = false, Message = msg };

            using var o = new MemoryStream();
            // RIFF header; the size is filled in at the end.
            o.Write("RIFF"u8);
            o.Write(new byte[4]);
            o.Write("WEBP"u8);

            var vp8x = new byte[10];
            vp8x[0] = 0x02 | 0x10;                 // animation, alpha
            Write24(vp8x, 4, w - 1);
            Write24(vp8x, 7, h - 1);
            Chunk(o, "VP8X", vp8x);

            var anim = new byte[6];                // background 0 (transparent), loop count
            BinaryPrimitives.WriteUInt16LittleEndian(anim.AsSpan(4), (ushort)(loop ? 0 : 1));
            Chunk(o, "ANIM", anim);

            for (int i = 0; i < frames.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                // image2 numbers its output from 1.
                var still = File.ReadAllBytes(Path.Combine(work, $"w{i + 1:D5}.webp"));
                var payload = FrameChunks(still);

                var head = new byte[16];
                // x/2 and y/2 offsets are 0: each frame is the full canvas.
                Write24(head, 6, w - 1);
                Write24(head, 9, h - 1);
                Write24(head, 12, Math.Clamp(frames[i].Delay, 1, 0xFFFFFF));
                // No blending (replace) and dispose to background, so partial
                // alpha is never composited over the previous frame.
                head[15] = 0x03;
                Chunk(o, "ANMF", head.Concat(payload).ToArray());
                progress?.Report((0.6 + 0.4 * (i + 1) / frames.Count, $"assembling frame {i + 1}"));
            }

            var bytes = o.ToArray();
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)(bytes.Length - 8));
            File.WriteAllBytes(outPath, bytes);
            return new WebmExport.Result
            {
                Ok = true, Bytes = bytes.Length,
                Message = $"wrote {Path.GetFileName(outPath)}, {bytes.Length / 1024.0:0.0} KB, {frames.Count} frames" +
                          (quality <= 0 ? ", lossless" : $", lossy q{quality}"),
            };
        }
        catch (OperationCanceledException)
        {
            try { File.Delete(outPath); } catch { }
            throw;
        }
        finally
        {
            try { Directory.Delete(work, true); } catch { }
        }
    }

    /// <summary>The image chunks of a still WebP (VP8L, or VP8 with ALPH), ready for an ANMF.</summary>
    private static byte[] FrameChunks(byte[] still)
    {
        if (still.Length < 12 || still[0] != 'R' || still[8] != 'W')
            throw new InvalidDataException("ffmpeg did not produce a WebP frame");
        using var o = new MemoryStream();
        int p = 12;
        while (p + 8 <= still.Length)
        {
            string tag = System.Text.Encoding.ASCII.GetString(still, p, 4);
            int size = (int)BinaryPrimitives.ReadUInt32LittleEndian(still.AsSpan(p + 4));
            int total = 8 + size + (size & 1);
            if (tag is "VP8L" or "VP8 " or "ALPH")
                o.Write(still, p, Math.Min(total, still.Length - p));
            p += total;
        }
        if (o.Length == 0) throw new InvalidDataException("WebP frame had no image data");
        return o.ToArray();
    }

    private static void Chunk(Stream s, string tag, byte[] data)
    {
        s.Write(System.Text.Encoding.ASCII.GetBytes(tag));
        Span<byte> len = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(len, (uint)data.Length);
        s.Write(len);
        s.Write(data);
        if ((data.Length & 1) == 1) s.WriteByte(0);   // RIFF chunks are padded to even length
    }

    private static void Write24(byte[] b, int at, int v)
    {
        b[at] = (byte)v; b[at + 1] = (byte)(v >> 8); b[at + 2] = (byte)(v >> 16);
    }
}
