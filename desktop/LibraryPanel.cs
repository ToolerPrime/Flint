/* The library: images kept with the document for reuse.
 *
 * Dropping an image on the canvas adds it here as well as placing it. Drag a
 * thumbnail onto the canvas to place another copy where you let go, or
 * double-click to place it in the middle of the view. */
namespace Flint;

public sealed class LibraryPanel : Panel
{
    public const string DragFormat = "Flint.LibraryItem";
    private const int Thumb = 56;

    private readonly ListView _list;
    private readonly ImageList _images = new() { ImageSize = new Size(Thumb, Thumb), ColorDepth = ColorDepth.Depth32Bit };
    private List<LibraryItem> _items = new();

    /// <summary>Place item i; the point is in screen coordinates, or null for the middle of the view.</summary>
    public event Action<int, Point?>? PlaceRequested;
    public event Action<string[]>? AddFilesRequested;
    public event Action? AddSelectionRequested;
    public event Action? Changed;

    public LibraryPanel()
    {
        _list = new ListView
        {
            Dock = DockStyle.Fill, View = View.LargeIcon, LargeImageList = _images, LabelEdit = true,
            MultiSelect = false, HideSelection = false, AllowDrop = true, BorderStyle = BorderStyle.None,
        };
        _list.ItemDrag += (_, e) =>
        {
            if (e.Item is ListViewItem it) _list.DoDragDrop(new DataObject(DragFormat, it.Index.ToString()), DragDropEffects.Copy);
        };
        _list.DoubleClick += (_, _) => { if (Selected >= 0) PlaceRequested?.Invoke(Selected, null); };
        _list.AfterLabelEdit += (_, e) =>
        {
            if (e.Label is not { Length: > 0 } name || e.Item < 0 || e.Item >= _items.Count) { e.CancelEdit = e.Label != null; return; }
            _items[e.Item].Name = name.Trim();
            Changed?.Invoke();
        };
        _list.KeyDown += (_, e) => { if (e.KeyCode == Keys.Delete) DeleteSelected(); if (e.KeyCode == Keys.F2 && _list.SelectedItems.Count > 0) _list.SelectedItems[0].BeginEdit(); };
        _list.DragEnter += (_, e) => e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None;
        _list.DragDrop += (_, e) =>
        {
            if (e.Data?.GetData(DataFormats.FileDrop) is string[] files) AddFilesRequested?.Invoke(files.Where(Importer.IsImage).ToArray());
        };

        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 64, Padding = new Padding(0, 2, 0, 0) };
        Button B(string text, int w, Action a)
        {
            var b = new Button { Text = text, Width = w, Height = 26, Margin = new Padding(2) };
            b.Click += (_, _) => a();
            bar.Controls.Add(b);
            return b;
        }
        B("Place", 56, () => { if (Selected >= 0) PlaceRequested?.Invoke(Selected, null); });
        B("Import…", 64, () =>
        {
            using var dlg = new OpenFileDialog { Multiselect = true, Filter = "Images|*.png;*.gif;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff|All files|*.*", Title = "Add images to the library" };
            if (dlg.ShowDialog(this) == DialogResult.OK) AddFilesRequested?.Invoke(dlg.FileNames);
        });
        B("Add selection", 96, () => AddSelectionRequested?.Invoke());
        B("Rename", 60, () => { if (_list.SelectedItems.Count > 0) _list.SelectedItems[0].BeginEdit(); });
        B("Delete", 56, DeleteSelected);

        var hint = new Label
        {
            Dock = DockStyle.Top, Height = 30, ForeColor = SystemColors.GrayText,
            Text = "Drag onto the canvas to place. Images dropped on the canvas are kept here.",
        };
        Controls.Add(_list);
        Controls.Add(hint);
        Controls.Add(bar);
    }

    private int Selected => _list.SelectedIndices.Count > 0 ? _list.SelectedIndices[0] : -1;

    public void Bind(List<LibraryItem> items)
    {
        _items = items;
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var img in _images.Images.Cast<Image>().ToList()) img.Dispose();
        _images.Images.Clear();
        for (int i = 0; i < items.Count; i++)
        {
            _images.Images.Add(MakeThumb(items[i].Pixels));
            _list.Items.Add(new ListViewItem(items[i].Name, i)
            {
                ToolTipText = $"{items[i].Name}  {items[i].Pixels.Width}×{items[i].Pixels.Height}" + (items[i].Source is { } s ? $"\n{s}" : ""),
            });
        }
        _list.ShowItemToolTips = true;
        _list.EndUpdate();
    }

    private void DeleteSelected()
    {
        int i = Selected;
        if (i < 0) return;
        if (!Program.Headless && MessageBox.Show(this, $"Remove '{_items[i].Name}' from the library?\nLayers already using it keep their copy. This cannot be undone.",
                "Library", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
        _items.RemoveAt(i);
        Bind(_items);
        Changed?.Invoke();
    }

    /// <summary>Letterboxed with nearest-neighbour, so pixel art stays sharp, on a checker.</summary>
    private static Bitmap MakeThumb(Bitmap src)
    {
        var b = new Bitmap(Thumb, Thumb, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(b);
        for (int y = 0; y < Thumb; y += 8)
            for (int x = 0; x < Thumb; x += 8)
                using (var br = new SolidBrush(((x + y) / 8 % 2 == 0) ? Color.FromArgb(235, 235, 238) : Color.FromArgb(210, 210, 215)))
                    g.FillRectangle(br, x, y, 8, 8);
        double k = Math.Min((double)Thumb / src.Width, (double)Thumb / src.Height);
        if (k > 1) k = Math.Floor(k);
        int w = Math.Max(1, (int)(src.Width * k)), h = Math.Max(1, (int)(src.Height * k));
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
        g.DrawImage(src, (Thumb - w) / 2, (Thumb - h) / 2, w, h);
        return b;
    }
}
