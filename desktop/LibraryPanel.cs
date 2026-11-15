/* The library: images kept with the document for reuse.
 *
 * Dropping an image on the canvas adds it here as well as placing it. Drag a
 * thumbnail onto the canvas to place another copy where you let go, or
 * double-click to place it in the middle of the view. */
namespace Flint;

public sealed class LibraryPanel : Panel
{
    public const string DragFormat = "Flint.LibraryItem";
    public const string SymbolDragFormat = "Flint.Symbol";
    private const int Thumb = 56;

    private readonly ListView _list;
    private readonly ImageList _images = new() { ImageSize = new Size(Thumb, Thumb), ColorDepth = ColorDepth.Depth32Bit };
    private List<LibraryItem> _items = new();
    private IReadOnlyList<SymbolMaster> _symbols = Array.Empty<SymbolMaster>();
    private readonly ListViewGroup _gSymbols = new("Symbols"), _gImages = new("Images");

    public event Action<string, Point?>? SymbolPlaceRequested;
    public event Action<string>? SymbolEditRequested;
    public event Action<string, string>? SymbolRenameRequested;
    public event Action<string>? SymbolDeleteRequested;

    private string? SymbolAt(ListViewItem? it) => it?.Tag as string;

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
        _list.Groups.Add(_gSymbols); _list.Groups.Add(_gImages);
        _list.ItemDrag += (_, e) =>
        {
            if (e.Item is not ListViewItem it) return;
            if (SymbolAt(it) is { } id) _list.DoDragDrop(new DataObject(SymbolDragFormat, id), DragDropEffects.Copy);
            else _list.DoDragDrop(new DataObject(DragFormat, ImageIndex(it).ToString()), DragDropEffects.Copy);
        };
        _list.DoubleClick += (_, _) => PlaceSelected();
        _list.AfterLabelEdit += (_, e) =>
        {
            var it = e.Item >= 0 && e.Item < _list.Items.Count ? _list.Items[e.Item] : null;
            if (e.Label is not { Length: > 0 } name || it == null) { e.CancelEdit = e.Label != null; return; }
            if (SymbolAt(it) is { } id) { e.CancelEdit = true; SymbolRenameRequested?.Invoke(id, name.Trim()); return; }
            int i = ImageIndex(it);
            if (i < 0 || i >= _items.Count) { e.CancelEdit = true; return; }
            _items[i].Name = name.Trim();
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
        B("Place", 56, PlaceSelected);
        B("Edit symbol", 84, () => { if (SymbolAt(SelectedItem) is { } id) SymbolEditRequested?.Invoke(id); });
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

    private ListViewItem? SelectedItem => _list.SelectedItems.Count > 0 ? _list.SelectedItems[0] : null;
    private int ImageIndex(ListViewItem? it) => it == null || it.Tag is string ? -1 : (int)it.Tag!;
    private int Selected => ImageIndex(SelectedItem);

    private void PlaceSelected()
    {
        if (SymbolAt(SelectedItem) is { } id) SymbolPlaceRequested?.Invoke(id, null);
        else if (Selected >= 0) PlaceRequested?.Invoke(Selected, null);
    }

    public void Bind(List<LibraryItem> items) => Bind(items, _symbols);

    public void Bind(List<LibraryItem> items, IReadOnlyList<SymbolMaster> symbols)
    {
        _items = items;
        _symbols = symbols;
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var img in _images.Images.Cast<Image>().ToList()) img.Dispose();
        _images.Images.Clear();
        foreach (var m in symbols)
        {
            _images.Images.Add(MakeThumb(SymbolOps.FrameBitmap(m, 0)));
            _list.Items.Add(new ListViewItem(m.Name, _images.Images.Count - 1)
            {
                Tag = m.Id, Group = _gSymbols,
                ToolTipText = $"Symbol '{m.Name}'  {m.Content.Width}×{m.Content.Height}, {m.Content.FrameCount} frame{(m.Content.FrameCount == 1 ? "" : "s")}\nDouble-click or drag to place; Edit symbol to change every copy",
            });
        }
        for (int i = 0; i < items.Count; i++)
        {
            _images.Images.Add(MakeThumb(items[i].Pixels));
            _list.Items.Add(new ListViewItem(items[i].Name, _images.Images.Count - 1)
            {
                Tag = i, Group = _gImages,
                ToolTipText = $"{items[i].Name}  {items[i].Pixels.Width}×{items[i].Pixels.Height}" + (items[i].Source is { } s ? $"\n{s}" : ""),
            });
        }
        _list.ShowItemToolTips = true;
        _list.EndUpdate();
    }

    private void DeleteSelected()
    {
        if (SymbolAt(SelectedItem) is { } id) { SymbolDeleteRequested?.Invoke(id); return; }
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
