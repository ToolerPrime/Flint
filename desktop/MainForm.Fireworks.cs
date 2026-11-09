/* Opening a Fireworks 8 PNG as a document (File > Open, or dropping it).
 * The parsing is in FireworksPng.cs. The new document is untitled: Save asks
 * for a .flint name, and the original .png is never written to. */
namespace Flint;

public sealed partial class MainForm
{
    private void SaveFireworksPng()
    {
        FinishFloat();
        StopPlay();
        if (FireworksPngWriter.HasCrossLayerBlending(_doc) &&
            MessageBox.Show(ProjectOwner,
                "Some blend modes may look different when Fireworks recombines the saved layers. Save a Fireworks PNG anyway?",
                "Save Fireworks PNG", MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK)
            return;

        string name = Path.GetFileNameWithoutExtension(_path ?? _sourcePath ?? "untitled");
        using var dialog = new SaveFileDialog
        {
            Title = "Save Fireworks PNG",
            Filter = "Fireworks PNG (editable bitmap layers)|*.png",
            FileName = $"{name}-fireworks.png",
            DefaultExt = "png",
        };
        if (dialog.ShowDialog(ProjectOwner) != DialogResult.OK) return;

        string path = dialog.FileName;
        var doc = _doc;
        var (ok, message) = ProgressDialog.Run(ProjectOwner, "Saving Fireworks PNG", (progress, ct) =>
        {
            FireworksPngWriter.Save(doc, path, ct, progress);
            return $"Saved {Path.GetFileName(path)} with editable bitmap layers.";
        });
        Say(message);
        if (!ok && message != "cancelled")
            MessageBox.Show(ProjectOwner, message, "Could not save Fireworks PNG", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    /// <summary>File > Open for either kind of file. A plain PNG is not opened: Import places it.</summary>
    internal bool OpenAny(string path)
    {
        if (!path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) return LoadProject(path);
        if (Importer.HasFireworksChunks(path)) return OpenFireworks(path);
        const string msg = "This PNG has no Fireworks layers in it, so it is not a document. Use Import (File > Import) to place it as a picture.";
        if (!Program.Headless) MessageBox.Show(this, msg, "Open", MessageBoxButtons.OK, MessageBoxIcon.Information);
        Say(Path.GetFileName(path) + ": not a Fireworks document. Use Import instead.");
        return false;
    }

    internal bool OpenFireworks(string path)
    {
        FwImport imp;
        try { imp = FireworksPng.Open(path); }
        catch (Exception ex) when (ex is InvalidDataException or IOException or ArgumentException or OutOfMemoryException)
        {
            string why = $"{Path.GetFileName(path)} could not be read as a Fireworks document: {ex.Message}";
            if (!Program.Headless) MessageBox.Show(this, why, "Could not open", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            Say(why);
            return false;
        }
        StopPlay();
        _undo.Clear();
        ClearProjectRecovery();
        _doc.Dispose();
        _doc = imp.Doc;
        _cur = _selStart = _selEnd = 0;
        _activeLayer = _doc.Layers.Count - 1;
        _selection = null;
        Theme.RememberProject(path);
        _sourcePath = Path.GetFullPath(path);
        _path = null;          // Save asks for a .flint name
        _dirty = true;
        FitZoom();
        InvalidateComposite();
        RefreshAll();
        UpdateTitle();
        string note = imp.Warnings.Count > 0 ? $" ({imp.Warnings.Count} note{(imp.Warnings.Count == 1 ? "" : "s")}: {string.Join("; ", imp.Warnings)})" : "";
        Say($"opened {Path.GetFileName(path)} as a Fireworks document: {imp.Summary}{note}");
        if (imp.Warnings.Count > 0 && !Program.Headless)
            MessageBox.Show(this,
                "Opened as a new untitled document. Some things from Fireworks could not be brought across:\n\n- " + string.Join("\n- ", imp.Warnings) +
                "\n\nThe flattened picture is kept on a hidden layer so you can compare.",
                "Fireworks document", MessageBoxButtons.OK, MessageBoxIcon.Information);
        return true;
    }
}
