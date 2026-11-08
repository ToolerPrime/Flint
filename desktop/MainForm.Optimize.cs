/* Glue for the Export preview dialog and the still-image export formats. */
namespace Flint;

public sealed partial class MainForm
{
    private OptSource OptimizeSource()
    {
        var frames = Enumerable.Range(0, _doc.FrameCount).Select(i => _doc.RenderPixels(i)).ToArray();
        return new OptSource
        {
            Frames = frames, Delays = _doc.Frames.Select(f => Math.Max(1, f.Delay)).ToArray(),
            W = DocW, H = DocH, Current = _cur, Matte = _bg, Loop = _loop.Checked,
        };
    }

    private void ExportPreview()
    {
        FinishFloat();
        StopPlay();
        using var dlg = new OptimizeDialog(OptimizeSource());
        if (dlg.ShowDialog(this) != DialogResult.OK || dlg.Chosen == null) return;
        var s = dlg.Chosen;
        var (kind, filter) = KindFor(s);
        using var save = new SaveFileDialog
        {
            Title = "Export with these settings", Filter = filter,
            FileName = Path.GetFileNameWithoutExtension(_path ?? "") is { Length: > 0 } n ? n : "animation",
        };
        if (save.ShowDialog(this) != DialogResult.OK) return;
        RunJob(JobFor(s, kind, save.FileName, dlg.Matte));
    }

    private (ExportKind kind, string filter) KindFor(OptSettings s)
    {
        bool multi = _doc.FrameCount > 1;
        return s.Format switch
        {
            OptFormat.Gif => (ExportKind.Gif, "GIF|*.gif"),
            OptFormat.Png8 => (ExportKind.Png8, "PNG-8|*.png"),
            OptFormat.Png32 => (ExportKind.Png, "PNG|*.png"),
            OptFormat.Apng => (ExportKind.Apng, "Animated PNG|*.png"),
            OptFormat.WebpLossless or OptFormat.WebpLossy => (multi ? ExportKind.WebP : ExportKind.WebPStill, "WebP|*.webp"),
            OptFormat.Jpeg => (ExportKind.Jpeg, "JPEG|*.jpg"),
            OptFormat.Bmp => (ExportKind.Bmp, "BMP|*.bmp"),
            _ => (ExportKind.Tiff, "TIFF|*.tif"),
        };
    }

    /// <summary>The normal export job for one pane's settings (animations take every frame, as the preview did).</summary>
    internal ExportJob JobFor(OptSettings s, ExportKind kind, string path, Color matte)
    {
        bool anim = kind is ExportKind.Gif or ExportKind.Apng or ExportKind.WebP;
        return new ExportJob
        {
            Doc = _doc, Kind = kind, Path = path, Current = _cur,
            From = anim ? 0 : _cur, To = anim ? _doc.FrameCount - 1 : _cur,
            GifColours = s.Colours, Dither = s.Dither, Loop = _loop.Checked, Matte = matte,
            Quality = s.Format == OptFormat.WebpLossless ? 0 : s.Quality,
        };
    }

    /// <summary>JPEG, WebP still, BMP, TIFF and PNG-8 picked in the ordinary Export dialog.</summary>
    private void RunStillExport(int formatIndex, string path)
    {
        var (filter, kind) = ExportFormats[formatIndex];
        int quality = 0;
        if (kind == ExportKind.Jpeg) quality = AskQuality("JPEG quality", 90);
        else if (kind == ExportKind.WebPStill && filter.Contains("lossy")) quality = AskQuality("WebP quality", 80);
        if (quality < 0) return;
        RunJob(new ExportJob
        {
            Doc = _doc, Kind = kind, Path = path, Current = _cur, From = _cur, To = _cur,
            GifColours = (int)_colors.Value, Dither = _dither.Checked, Matte = _bg, Quality = quality,
        });
    }

    /// <summary>1-100, or -1 if cancelled. Headless runs and tests take the default.</summary>
    private int AskQuality(string title, int def)
    {
        if (SuppressPrompts || Program.Headless) return def;
        using var f = new Form
        {
            Text = title, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false,
            StartPosition = FormStartPosition.CenterParent, ClientSize = new Size(260, 90),
        };
        var n = new NumericUpDown { Minimum = 1, Maximum = 100, Value = def, Left = 120, Top = 14, Width = 60 };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Left = 60, Top = 52 };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Left = 140, Top = 52 };
        f.Controls.AddRange(new Control[] { new Label { Text = "Quality (1-100)", Left = 14, Top = 18, AutoSize = true }, n, ok, cancel });
        f.AcceptButton = ok; f.CancelButton = cancel;
        return f.ShowDialog(this) == DialogResult.OK ? (int)n.Value : -1;
    }

    private void RunJob(ExportJob job)
    {
        StopPlay();
        if (Exporter.NeedsFfmpeg(job.Kind) && WebmExport.Find() == null)
        {
            if (!SuppressPrompts && !Program.Headless)
                MessageBox.Show(this,
                    "This format uses ffmpeg, which was not found.\n\nInstall it with:\n    winget install Gyan.FFmpeg\n\n" +
                    "or put ffmpeg.exe next to Flint.exe, then try again.", "ffmpeg required", MessageBoxButtons.OK, MessageBoxIcon.Information);
            Say("ffmpeg not found");
            return;
        }
        var (ok, message) = ProgressDialog.Run(this, "Exporting", (p, ct) => Exporter.Run(job, p, ct));
        Say(ok ? message : "export " + (message == "cancelled" ? "cancelled" : "failed: " + message));
        if (!ok && message != "cancelled" && !SuppressPrompts && !Program.Headless)
            MessageBox.Show(this, message, "Export failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    // ---------- test hooks ----------
    internal OptimizeDialog TestOptimizeDialog() => new(OptimizeSource());
    internal string TestRunOptimized(OptSettings s, string path, Color matte)
    {
        var (kind, _) = KindFor(s);
        return Exporter.Run(JobFor(s, kind, path, matte), null, default);
    }
    internal (ExportKind kind, string filter) TestKindFor(OptSettings s) => KindFor(s);
    internal OptSource TestOptSource() => OptimizeSource();
    internal void TestUseDocument(Document d)
    {
        _undo.Clear(); _doc = d; _cur = 0; _activeLayer = d.Layers.Count - 1;
        InvalidateComposite(); RefreshAll();
    }
}
