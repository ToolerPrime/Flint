/* Autosave and crash recovery.
 *
 * While a document has unsaved changes it is written periodically to
 *   %LOCALAPPDATA%\Flint\Recovery\<session>.flint   (+ .json sidecar)
 * with the same atomic save as a normal project. A clean exit, a manual save,
 * or a new document removes it. A recovery file whose owning process is no
 * longer running was left by a crash, and is offered back on the next launch.
 *
 * Recovery files never replace the user's own file: restoring opens the copy
 * as an unsaved document and remembers where it came from.
 */
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Flint;

public static class Recovery
{
    /// <summary>Overridable so tests never touch the real folder.</summary>
    public static string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Flint", "Recovery");

    public static readonly string Session = Guid.NewGuid().ToString("N")[..12];

    public sealed class Entry
    {
        public string ProjectPath = "";
        public string? OriginalPath;
        public DateTime SavedAt;
        public int Pid;
        public long Bytes;
        public string Display => (OriginalPath != null ? Path.GetFileName(OriginalPath) : "Untitled") +
                                 $", autosaved {SavedAt:HH:mm} on {SavedAt:d MMM}";
    }

    private static string ProjectFor(string session) => Path.Combine(Dir, session + FlintFormat.Extension);
    private static string MetaFor(string session) => Path.Combine(Dir, session + ".json");

    /// <summary>Write the recovery copy for this session.</summary>
    public static void Write(Document doc, string? originalPath, string? session = null)
    {
        session ??= Session;
        Directory.CreateDirectory(Dir);
        FlintFormat.Save(doc, ProjectFor(session));
        var meta = new JsonObject
        {
            ["original"] = originalPath,
            ["savedAt"] = DateTime.Now.ToString("o"),
            ["pid"] = Environment.ProcessId,
        };
        string tmp = MetaFor(session) + ".tmp";
        File.WriteAllText(tmp, meta.ToJsonString());
        File.Move(tmp, MetaFor(session), overwrite: true);
    }

    /// <summary>Remove this session's copy, e.g. after a save or on clean exit.</summary>
    public static void Clear(string? session = null)
    {
        session ??= Session;
        TryDelete(ProjectFor(session));
        TryDelete(MetaFor(session));
    }

    /// <summary>Recovery copies left behind by processes that are no longer running.</summary>
    public static List<Entry> Orphans()
    {
        var found = new List<Entry>();
        if (!Directory.Exists(Dir)) return found;
        foreach (var meta in Directory.GetFiles(Dir, "*.json"))
        {
            string session = Path.GetFileNameWithoutExtension(meta);
            string project = ProjectFor(session);
            if (!File.Exists(project)) { TryDelete(meta); continue; }
            try
            {
                var node = JsonNode.Parse(File.ReadAllText(meta));
                int pid = node?["pid"]?.GetValue<int>() ?? 0;
                if (pid == Environment.ProcessId || IsRunningFlint(pid)) continue;   // still alive
                found.Add(new Entry
                {
                    ProjectPath = project,
                    OriginalPath = node?["original"]?.GetValue<string>(),
                    SavedAt = DateTime.TryParse(node?["savedAt"]?.GetValue<string>(), out var t) ? t : File.GetLastWriteTime(project),
                    Pid = pid,
                    Bytes = new FileInfo(project).Length,
                });
            }
            catch { /* unreadable sidecar: leave the files for the user to find */ }
        }
        return found.OrderByDescending(e => e.SavedAt).ToList();
    }

    public static void Discard(Entry e)
    {
        TryDelete(e.ProjectPath);
        TryDelete(Path.ChangeExtension(e.ProjectPath, ".json"));
    }

    private static bool IsRunningFlint(int pid)
    {
        if (pid <= 0) return false;
        try
        {
            using var p = Process.GetProcessById(pid);
            // A reused pid belonging to some other program does not count.
            return !p.HasExited && p.ProcessName.StartsWith("Flint", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
