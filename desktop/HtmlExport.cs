/* One self-contained .html page that plays the animation: every frame in a
 * sprite-sheet PNG embedded as a data URL, exact per-frame delays, loop or
 * play once, scaled by whole numbers with sharp pixels. Click to pause.
 * Written for Flint. */
using System.Drawing.Imaging;
using System.Globalization;
using System.Net;

namespace Flint;

public static class HtmlExport
{
    /// <summary>Columns of the embedded sheet: near-square, so browsers' canvas size limits are not hit.</summary>
    public static int Columns(int n) => Math.Max(1, (int)Math.Ceiling(Math.Sqrt(n)));

    /// <summary>Largest sheet side browsers draw reliably.</summary>
    public const int MaxSide = 16384;

    public static string Save(ExportJob j, List<Frame> frames)
    {
        int w = frames[0].Bmp.Width, h = frames[0].Bmp.Height, n = frames.Count, cols = Columns(n);
        int rows = (n + cols - 1) / cols;
        if ((long)w * cols > MaxSide || (long)h * rows > MaxSide)
            throw new InvalidOperationException($"{n} frames of {w}×{h} make a picture bigger than browsers show ({MaxSide} pixels a side). Export fewer frames or a smaller canvas.");
        var sheet = new byte[w * cols * h * rows * 4];
        for (int i = 0; i < n; i++)
        {
            var px = ImageOps.GetPixels(frames[i].Bmp);
            for (int y = 0; y < h; y++)
                Array.Copy(px, y * w * 4, sheet, ((i / cols * h + y) * w * cols + i % cols * w) * 4, w * 4);
        }
        string b64;
        using (var bmp = ImageOps.FromPixels(w * cols, h * rows, sheet))
        using (var ms = new MemoryStream())
        {
            bmp.Save(ms, ImageFormat.Png);
            b64 = Convert.ToBase64String(ms.ToArray());
        }
        string delays = string.Join(",", frames.Select(f => Math.Max(1, f.Delay).ToString(CultureInfo.InvariantCulture)));
        var bg = j.Matte;
        string title = WebUtility.HtmlEncode(Path.GetFileNameWithoutExtension(j.Path));
        string html = $$"""
<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>{{title}}</title>
<style>
html, body { margin: 0; height: 100%; background: #{{bg.R:X2}}{{bg.G:X2}}{{bg.B:X2}}; }
body { display: flex; align-items: center; justify-content: center; }
canvas { image-rendering: pixelated; image-rendering: crisp-edges; cursor: pointer; }
</style>
</head>
<body>
<canvas id="anim" width="{{w}}" height="{{h}}" role="img" aria-label="{{title}} animation, click to pause"></canvas>
<script>
// Made with Flint. Frames: {{n}}, {{w}}x{{h}}, delays in milliseconds.
const W = {{w}}, H = {{h}}, COLS = {{cols}}, LOOP = {{(j.Loop ? "true" : "false")}};
const DELAYS = [{{delays}}];
const canvas = document.getElementById("anim"), g = canvas.getContext("2d");
const sheet = new Image();
let frame = 0, due = 0, paused = false;
function fit() {
  const k = Math.max(1, Math.floor(Math.min(innerWidth / W, innerHeight / H)));
  canvas.style.width = W * k + "px"; canvas.style.height = H * k + "px";
}
function show(i) {
  g.clearRect(0, 0, W, H);
  g.drawImage(sheet, (i % COLS) * W, Math.floor(i / COLS) * H, W, H, 0, 0, W, H);
}
function tick(now) {
  if (!paused) {
    if (now - due > 1000) due = now;          // back from a hidden tab: carry on, do not replay
    while (now >= due) {
      if (frame === DELAYS.length - 1 && !LOOP) { due = Infinity; break; }
      frame = (frame + 1) % DELAYS.length;
      due += DELAYS[frame];
      show(frame);
    }
  }
  requestAnimationFrame(tick);
}
canvas.addEventListener("click", () => {
  paused = !paused;
  if (!paused) due = performance.now() + DELAYS[frame];
});
addEventListener("resize", fit);
sheet.onload = () => { fit(); show(0); due = performance.now() + DELAYS[0]; requestAnimationFrame(tick); };
sheet.src = "data:image/png;base64,{{b64}}";
</script>
</body>
</html>
""";
        File.WriteAllText(j.Path, html);
        return $"wrote {Path.GetFileName(j.Path)}, {html.Length / 1024.0:0.0} KB, {n} frames in one page";
    }
}
