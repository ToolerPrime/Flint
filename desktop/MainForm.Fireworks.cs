/* Opening a Fireworks 8 PNG as a document (File > Open, or dropping it).
 * The parsing is in FireworksPng.cs. The new document is untitled: Save asks
 * for a .flint name, and the original .png is never written to. */
namespace Flint;

public sealed partial class MainForm
{
    /// <summary>File > Open for either kind of file. A plain PNG is not opened: Import places it.</summary>
    private void OpenAny(string path)
    {
        if (!path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) { LoadProject(path); return; }
        if (Importer.HasFireworksChunks(path)) { OpenFireworks(path); return; }
        const string msg = "This PNG has no Fireworks layers in it, so it is not a document. Use Import (File > Import) to place it as a picture.";
        if (!Program.Headless) MessageBox.Show(this, msg, "Open", MessageBoxButtons.OK, MessageBoxIcon.Information);
        Say(Path.GetFileName(path) + ": not a Fireworks document. Use Import instead.");
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
        Recovery.Clear();
        _doc.Dispose();
        _doc = imp.Doc;
        _cur = 0;
        _activeLayer = _doc.Layers.Count - 1;
        _selection = null;
        _path = null;          // untitled: Save asks for a .flint name
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
