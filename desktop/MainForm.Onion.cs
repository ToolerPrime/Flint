namespace Flint;

public sealed partial class MainForm
{
    private void ShowOnionSettings()
    {
        using var dialog = new OnionSettingsDialog(_onionSet, _canvas.Invalidate);
        dialog.ShowDialog(this);
    }

    private void AddOnionMenu(ToolStripMenuItem frameMenu)
    {
        var toggle = new ToolStripMenuItem("Show onion skin", null, (_, _) => _onionBox.Checked = !_onionBox.Checked);
        frameMenu.DropDownOpening += (_, _) => toggle.Checked = _onionSet.Enabled;
        frameMenu.DropDownItems.Add(toggle);
        frameMenu.DropDownItems.Add("Onion skin settings…", null, (_, _) => ShowOnionSettings());
    }
}

internal sealed class OnionSettingsDialog : ThemedForm
{
    public OnionSettingsDialog(OnionSettings settings, Action refresh)
    {
        Text = "Onion skin settings";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        ShowInTaskbar = MinimizeBox = MaximizeBox = false;
        ClientSize = new Size(340, 228);
        var original = (settings.Before, settings.After, settings.Opacity, settings.Tint, settings.Wrap);
        var tips = Theme.MakeTip();
        Disposed += (_, _) => tips.Dispose();
        int y = 14;

        void Add(Control control, string name, string tip)
        {
            control.AccessibleName = name;
            tips.SetToolTip(control, tip);
            Controls.Add(control);
        }

        void Number(string label, int value, int min, int max, Action<int> change, string tip, bool slider = false)
        {
            Add(new Label { Text = label, Left = 16, Top = y + 4, Width = 136 }, label, tip);
            var number = new NumericUpDown { Left = 156, Top = y, Width = 58,
                Value = value, Minimum = min, Maximum = max };
            Add(number, label, tip);
            MiniSlider? track = null;
            if (slider)
            {
                track = new MiniSlider { Left = 228, Top = y, Width = 94,
                    Minimum = min, Maximum = max, Value = value };
                Add(track, label + " slider", tip);
                track.ValueChanged += (_, _) => number.Value = track.Value;
            }
            number.ValueChanged += (_, _) =>
            {
                if (track != null) track.Value = number.Value;
                change((int)number.Value);
                refresh();
            };
            y += 30;
        }

        void Toggle(string label, bool value, Action<bool> change, string tip)
        {
            var box = new MintCheckBox { Text = label, Left = 16, Top = y, Width = 308, Height = 24, Checked = value };
            box.CheckedChanged += (_, _) => { change(box.Checked); refresh(); };
            Add(box, label, tip);
            y += 28;
        }

        Number("Frames before", settings.Before, 0, 9, n => settings.Before = n, "Show this many earlier frames");
        Number("Frames after", settings.After, 0, 9, n => settings.After = n, "Show this many later frames");
        Number("Opacity (%)", (int)Math.Round(settings.Opacity * 100), 5, 100,
            n => settings.Opacity = n / 100f, "How strongly the closest ghosts show", slider: true);
        Toggle("Tint earlier red and later blue", settings.Tint, b => settings.Tint = b, "Tell earlier and later frames apart by colour");
        Toggle("Wrap from last to first frame", settings.Wrap, b => settings.Wrap = b, "Show ghosts across the ends of a loop");
        var ok = new Button { Text = "OK", Left = 156, Top = 183, Width = 80, Height = 28, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancel", Left = 244, Top = 183, Width = 80, Height = 28, DialogResult = DialogResult.Cancel };
        Add(ok, "Keep onion skin settings", "Keep these settings");
        Add(cancel, "Cancel onion skin settings", "Restore the previous settings");
        AcceptButton = ok;
        CancelButton = cancel;
        FormClosed += (_, _) =>
        {
            if (DialogResult != DialogResult.OK)
            {
                (settings.Before, settings.After, settings.Opacity, settings.Tint, settings.Wrap) = original;
                refresh();
            }
        };
    }
}
