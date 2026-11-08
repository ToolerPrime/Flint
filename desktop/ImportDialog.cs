/* The import dialog: how to place the images, and whether to change them.
 * Defaults change nothing about the pixels. */
using System.Drawing.Drawing2D;

namespace Flint;

public sealed class ImportDialog : ThemedForm
{
    private readonly List<ImportedFile> _files;
    private readonly Document _doc;
    public ImportOptions Options { get; } = new();

    private readonly RadioButton _frames, _layers, _one, _sheetAuto, _sheetGrid;
    private readonly CheckBox _preserve, _fit, _smooth, _palette, _dither;
    private readonly NumericUpDown _colours, _gap, _cellW, _cellH, _margin, _spacing;
    private readonly ComboBox _anchor;
    private readonly Label _notes, _found, _previewInfo;
    private readonly PictureBox _before, _after;

    public ImportDialog(List<ImportedFile> files, Document doc)
    {
        _files = files;
        _doc = doc;
        Text = files.Count == 1 ? $"Import {files[0].Name}" : $"Import {files.Count} images";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = MaximizeBox = false;
        ClientSize = new Size(620, 520);

        int frameTotal = files.Sum(f => f.Frames.Count);
        var list = new Label
        {
            Left = 14, Top = 10, Width = 590, Height = 38,
            Text = string.Join("   ", files.Take(4).Select(f =>
                $"{f.Name} — {f.Width}×{f.Height} {f.Format}{(f.Frames.Count > 1 ? $", {f.Frames.Count} frames" : "")}"))
                + (files.Count > 4 ? $"   … and {files.Count - 4} more" : ""),
        };
        Controls.Add(list);

        var place = new FlatGroup { Text = "Place as", Left = 12, Top = 50, Width = 290, Height = 250 };
        _frames = Radio(place, "Animation frames, one per image", 22, true);
        _layers = Radio(place, "Layers, one per image", 46);
        _one = Radio(place, "One image on a new layer", 70);
        _sheetAuto = Radio(place, "Sprite sheet — find sprites automatically", 100);
        place.Controls.Add(new Label { Text = "gap", Left = 34, Top = 128, Width = 30 });
        _gap = Num(place, 64, 124, 44, 2, 0, 20);
        place.Controls.Add(new Label { Text = "align", Left = 116, Top = 128, Width = 36 });
        _anchor = new ComboBox { Left = 152, Top = 124, Width = 124, DropDownStyle = ComboBoxStyle.DropDownList };
        _anchor.Items.AddRange(new object[] { "Bottom centre (feet)", "Centre", "Top left" });
        _anchor.SelectedIndex = 0;
        place.Controls.Add(_anchor);
        _sheetGrid = Radio(place, "Sprite sheet — fixed grid", 156);
        place.Controls.Add(new Label { Text = "cell", Left = 34, Top = 184, Width = 30 });
        _cellW = Num(place, 64, 180, 48, 32, 1, 4096);
        place.Controls.Add(new Label { Text = "×", Left = 114, Top = 184, Width = 12 });
        _cellH = Num(place, 128, 180, 48, 32, 1, 4096);
        place.Controls.Add(new Label { Text = "margin", Left = 34, Top = 212, Width = 46 });
        _margin = Num(place, 82, 208, 40, 0, 0, 256);
        place.Controls.Add(new Label { Text = "spacing", Left = 128, Top = 212, Width = 50 });
        _spacing = Num(place, 180, 208, 40, 0, 0, 256);
        _found = new Label { Left = 230, Top = 184, Width = 56, Height = 40, ForeColor = SystemColors.GrayText };
        place.Controls.Add(_found);
        Controls.Add(place);
        bool single = files.Count == 1 && files[0].Frames.Count == 1;
        _sheetAuto.Enabled = _sheetGrid.Enabled = files.Count == 1;
        if (single && (files[0].Width > doc.Width * 2 || files[0].Height > doc.Height * 2) && doc.ApproximateBytes() > 0)
            _one.Checked = true;

        var pixels = new FlatGroup { Text = "Pixels", Left = 312, Top = 50, Width = 296, Height = 250 };
        _preserve = new MintCheckBox { Text = "Preserve original pixels (recommended)", Left = 12, Top = 22, Width = 278, Checked = true };
        pixels.Controls.Add(_preserve);
        pixels.Controls.Add(new Label
        {
            Left = 30, Top = 44, Width = 258, Height = 44, ForeColor = SystemColors.GrayText,
            Text = "Keeps size, transparency and every colour exactly. A larger image grows the canvas instead of being shrunk.",
        });
        _fit = new MintCheckBox { Text = "Shrink to fit the canvas", Left = 30, Top = 90, Width = 180, Enabled = false };
        _smooth = new MintCheckBox { Text = "smooth", Left = 214, Top = 90, Width = 70, Enabled = false };
        pixels.Controls.Add(_fit);
        pixels.Controls.Add(_smooth);
        _palette = new MintCheckBox { Text = "Convert to project palette", Left = 12, Top = 122, Width = 200 };
        pixels.Controls.Add(_palette);
        pixels.Controls.Add(new Label { Text = "colours", Left = 30, Top = 150, Width = 50 });
        _colours = Num(pixels, 82, 146, 52, 32, 2, 256);
        _dither = new MintCheckBox { Text = "dither", Left = 144, Top = 148, Width = 70 };
        pixels.Controls.Add(_dither);
        _before = new PictureBox { Left = 30, Top = 176, Width = 64, Height = 64, BorderStyle = BorderStyle.FixedSingle, SizeMode = PictureBoxSizeMode.Zoom };
        _after = new PictureBox { Left = 100, Top = 176, Width = 64, Height = 64, BorderStyle = BorderStyle.FixedSingle, SizeMode = PictureBoxSizeMode.Zoom };
        _previewInfo = new Label { Left = 172, Top = 176, Width = 118, Height = 66, ForeColor = SystemColors.GrayText };
        pixels.Controls.Add(_before);
        pixels.Controls.Add(_after);
        pixels.Controls.Add(_previewInfo);
        Controls.Add(pixels);

        _notes = new Label { Left = 14, Top = 308, Width = 594, Height = 160, ForeColor = Theme.Warn };
        Controls.Add(_notes);

        var ok = new Button { Text = "Import", Left = 442, Top = 480, Width = 80, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancel", Left = 528, Top = 480, Width = 80, DialogResult = DialogResult.Cancel };
        Controls.Add(ok);
        Controls.Add(cancel);
        AcceptButton = ok;
        CancelButton = cancel;

        foreach (var c in new Control[] { _frames, _layers, _one, _sheetAuto, _sheetGrid, _preserve, _fit, _smooth, _palette, _dither })
            if (c is RadioButton r) r.CheckedChanged += (_, _) => Sync(); else if (c is CheckBox cb) cb.CheckedChanged += (_, _) => Sync();
        foreach (var n in new[] { _colours, _gap, _cellW, _cellH, _margin, _spacing }) n.ValueChanged += (_, _) => Sync();
        _anchor.SelectedIndexChanged += (_, _) => Sync();
        if (_files.Count == 1 && files[0].Frames.Count == 1 && files[0].Width >= 64)
        {
            // A big single image dropped into a small sprite document is probably a sheet.
            var found = Sprites.Auto(ImageOps.GetPixels(files[0].Frames[0].bmp), files[0].Width, files[0].Height);
            if (found.Count >= 3) _sheetAuto.Checked = true;
        }
        Sync();
        FormClosing += (_, e) => { if (DialogResult == DialogResult.OK) Collect(); };
    }

    private static RadioButton Radio(Control parent, string text, int top, bool on = false)
    {
        var r = new RadioButton { Text = text, Left = 12, Top = top, Width = 270, Checked = on };
        parent.Controls.Add(r);
        return r;
    }

    private static NumericUpDown Num(Control parent, int x, int y, int w, decimal v, decimal min, decimal max)
    {
        var n = new NumericUpDown { Left = x, Top = y, Width = w, Minimum = min, Maximum = max, Value = v, TextAlign = HorizontalAlignment.Right };
        parent.Controls.Add(n);
        return n;
    }

    private void Collect()
    {
        Options.Mode = _frames.Checked ? ImportMode.Frames : _layers.Checked ? ImportMode.Layers
                     : _one.Checked ? ImportMode.OneImage : _sheetAuto.Checked ? ImportMode.SheetAuto : ImportMode.SheetGrid;
        Options.PreservePixels = _preserve.Checked;
        Options.FitToCanvas = !_preserve.Checked && _fit.Checked;
        Options.SmoothScaling = _smooth.Checked;
        Options.ConvertToPalette = _palette.Checked;
        Options.PaletteColours = (int)_colours.Value;
        Options.Dither = _dither.Checked;
        Options.GapPixels = (int)_gap.Value;
        Options.Anchor = (SliceAnchor)_anchor.SelectedIndex;
        Options.CellW = (int)_cellW.Value; Options.CellH = (int)_cellH.Value;
        Options.Margin = (int)_margin.Value; Options.Spacing = (int)_spacing.Value;
    }

    private void Sync()
    {
        Collect();
        _fit.Enabled = !_preserve.Checked;
        _smooth.Enabled = !_preserve.Checked && _fit.Checked;
        _colours.Enabled = _dither.Enabled = _palette.Checked;
        _gap.Enabled = _anchor.Enabled = _sheetAuto.Checked;
        _cellW.Enabled = _cellH.Enabled = _margin.Enabled = _spacing.Enabled = _sheetGrid.Checked;

        if (_files.Count == 1 && (_sheetAuto.Checked || _sheetGrid.Checked))
        {
            var b = _files[0].Frames[0].bmp;
            int n = _sheetAuto.Checked
                ? Sprites.Auto(ImageOps.GetPixels(b), b.Width, b.Height, joinGap: (int)_gap.Value).Count
                : Sprites.Grid(b.Width, b.Height, (int)_cellW.Value, (int)_cellH.Value, (int)_margin.Value, (int)_spacing.Value).Count;
            _found.Text = n == 1 ? "1 frame" : $"{n} frames";
        }
        else _found.Text = "";

        _notes.Text = string.Join(Environment.NewLine + Environment.NewLine, Importer.Notes(_files, Options));
        UpdatePreview();
    }

    private void UpdatePreview()
    {
        _before.Image?.Dispose();
        _after.Image?.Dispose();
        _before.Image = _after.Image = null;
        _previewInfo.Text = "";
        if (!_palette.Checked || _files.Count == 0 || _files[0].Frames.Count == 0) return;

        var src = _files[0].Frames[0].bmp;
        // Preview at most 256 px across so a big sheet stays quick.
        double s = Math.Min(1.0, 256.0 / Math.Max(src.Width, src.Height));
        var small = s < 1 ? ImageOps.Scale(src, Math.Max(1, (int)(src.Width * s)), Math.Max(1, (int)(src.Height * s)), false) : ImageOps.Clone(src);
        var px = ImageOps.GetPixels(small);
        var pal = Importer.ProjectPalette(_doc, _files, (int)_colours.Value);
        int total = px.Length / 4, opaque = 0;
        for (int i = 3; i < px.Length; i += 4) if (px[i] > 0) opaque++;
        int changed = Importer.ApplyPalette(px, small.Width, small.Height, pal, _dither.Checked);
        _before.Image = Pixelated(small);
        _after.Image = Pixelated(ImageOps.FromPixels(small.Width, small.Height, px));
        _previewInfo.Text = $"before | after{Environment.NewLine}{changed} of {opaque} visible pixels change{Environment.NewLine}{pal.Count} colour{(pal.Count == 1 ? "" : "s")}";
    }

    /// <summary>Enlarge for the preview box without blurring pixel art.</summary>
    private static Bitmap Pixelated(Bitmap b)
    {
        int k = Math.Max(1, 64 / Math.Max(b.Width, b.Height));
        return k <= 1 ? b : ImageOps.Scale(b, b.Width * k, b.Height * k, false);
    }
}
