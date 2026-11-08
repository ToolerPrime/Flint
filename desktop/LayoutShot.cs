/* Render the real window to a PNG and report any control that does not fit
 * its parent. Hand-placed WinForms controls clip silently, so this is the
 * cheap way to catch it without a human looking.
 *     Flint.exe --shot <out.png> [sheet.png]
 */
using System.Drawing.Imaging;

namespace Flint;

public static class LayoutShot
{
    public static int Run(string outPng, string? sheet)
    {
        var form = new MainForm
        {
            StartPosition = FormStartPosition.Manual,
            Location = new Point(-6000, -6000),   // off-screen, so nothing flashes
        };
        form.SuppressPrompts = true;   // headless: a save prompt would hang the run
        form.Show();
        Application.DoEvents();

        if (sheet != null && File.Exists(sheet))
        {
            form.LoadForTest(new[] { sheet }, cols: 6, rows: 1, key: Color.Magenta, tolerance: 20);
            Application.DoEvents();

            // Give the shot everything the timeline can show: a tag and two
            // audio lanes with waveforms, so their layout is checked too.
            form.TestSelectFrames(0, 2);
            form.TestTag("Idle");
            form.TestSelectFrames(3, 5);
            form.TestTag("Hop");
            var tone = new float[AudioFormat.MsToFrames(450) * 2];
            for (int i = 0; i < tone.Length / 2; i++)
                tone[i * 2] = tone[i * 2 + 1] = 0.6f * (float)Math.Sin(i * 0.02) * (float)Math.Sin(i * 0.0007);
            form.TestFinishRecording(tone, 100);
            form.TestDocument.Audio.Tracks.Add(new AudioTrack { Name = "SFX", Kind = TrackKind.Effects });
            var sfx = form.TestDocument.Audio.AddSource(tone.Take(tone.Length / 3).ToArray(), "boing");
            form.TestDocument.Audio.AddClip(form.TestDocument.Audio.Tracks[1], sfx, 300);
            form.TestSelectFrames(1, 1);
            Application.DoEvents();
        }

        if (Environment.GetEnvironmentVariable("FLINT_FRAMES") == "1") { form.TestSetFramesView(true); Application.DoEvents(); }
        int problems = 0;
        foreach (var (path, c) in Walk(form, ""))
        {
            if (c.Parent == null || !c.Visible) continue;
            var client = c.Parent.ClientRectangle;
            // A docked or anchored control is laid out by the framework; only
            // absolutely positioned ones can be placed outside the parent.
            if (c.Dock != DockStyle.None) continue;
            if (c.Right > client.Right + 1 || c.Bottom > client.Bottom + 1 || c.Left < -1 || c.Top < -1)
            {
                Console.WriteLine($"  CLIPPED {path} at {c.Bounds} in client {client}");
                problems++;
            }
        }

        // A visible control squeezed to nothing has vanished for the user even
        // though nothing is "clipped". This is how the Layers panel once went
        // missing when the timeline grew.
        foreach (var (path, c) in Walk(form, ""))
        {
            if (!c.Visible || c is ScrollBar || c is Splitter) continue;
            if (c.Parent != null && !c.Parent.Visible) continue;
            // Only containers the layout engine sizes; fixed-size swatches are small on purpose.
            if (c is FlatGroup { Collapsed: true } || c.Tag as string == "slim") continue;   // slim on purpose
            if (c.Dock != DockStyle.None && c is GroupBox or Panel or LayersPanel or TimelinePanel or CanvasPanel)
            {
                if (c.Width < 40 || c.Height < 40)
                {
                    Console.WriteLine($"  COLLAPSED {path} is {c.Width}x{c.Height}");
                    problems++;
                }
            }
        }

        // Overlapping siblings in the same panel are almost always a mistake.
        foreach (var parent in AllContainers(form))
        {
            var kids = parent.Controls.Cast<Control>()
                .Where(k => k.Visible && k.Dock == DockStyle.None).ToList();
            for (int i = 0; i < kids.Count; i++)
            {
                for (int j = i + 1; j < kids.Count; j++)
                {
                    var a = kids[i].Bounds;
                    var b = kids[j].Bounds;
                    a.Intersect(b);
                    if (a.Width > 2 && a.Height > 2)
                    {
                        Console.WriteLine($"  OVERLAP {Name(kids[i])} and {Name(kids[j])} " +
                                          $"in {Name(parent)} by {a.Width}x{a.Height}");
                        problems++;
                    }
                }
            }
        }

        var bmp = new Bitmap(form.Width, form.Height, PixelFormat.Format32bppArgb);
        form.DrawToBitmap(bmp, new Rectangle(0, 0, form.Width, form.Height));
        bmp.Save(outPng, ImageFormat.Png);
        Console.WriteLine($"wrote {outPng} ({form.Width}x{form.Height})");
        Console.WriteLine(problems == 0 ? "layout clean" : $"{problems} layout problem(s)");

        form.Close();
        return problems == 0 ? 0 : 1;
    }

    private static string Name(Control c) =>
        string.IsNullOrEmpty(c.Name)
            ? $"{c.GetType().Name}(\"{Short(c.Text)}\")"
            : c.Name;

    private static string Short(string? s) =>
        string.IsNullOrEmpty(s) ? "" : (s.Length > 18 ? s[..18] : s);

    private static IEnumerable<(string, Control)> Walk(Control root, string path)
    {
        foreach (Control c in root.Controls)
        {
            string p = path + "/" + Name(c);
            yield return (p, c);
            foreach (var sub in Walk(c, p)) yield return sub;
        }
    }

    private static IEnumerable<Control> AllContainers(Control root)
    {
        foreach (Control c in root.Controls)
        {
            if (c.Controls.Count > 1) yield return c;
            foreach (var sub in AllContainers(c)) yield return sub;
        }
    }
}
