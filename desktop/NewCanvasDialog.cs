namespace Flint;

public sealed class NewCanvasDialog : ThemedForm
{
    private readonly NumericUpDown _w, _h;
    private readonly CheckBox _transparent;

    public int CanvasWidth => (int)_w.Value;
    public int CanvasHeight => (int)_h.Value;
    public bool Transparent => _transparent.Checked;

    public NewCanvasDialog(int defW = 64, int defH = 64, bool clipboardSize = false)
    {
        Text = "New canvas";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = MaximizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(268, 142);
        var tips = Theme.MakeTip();
        Disposed += (_, _) => tips.Dispose();

        Controls.Add(new Label { Text = "Width", Left = 16, Top = 20, Width = 52 });
        _w = new NumericUpDown
        {
            Left = 72, Top = 17, Width = 72, Minimum = 1, Maximum = 20000, Value = Math.Clamp(defW, 1, 20000), AccessibleName = "Canvas width",
            TextAlign = HorizontalAlignment.Right,
        };
        Controls.Add(_w);

        Controls.Add(new Label { Text = "Height", Left = 16, Top = 52, Width = 52 });
        _h = new NumericUpDown
        {
            Left = 72, Top = 49, Width = 72, Minimum = 1, Maximum = 20000, Value = Math.Clamp(defH, 1, 20000), AccessibleName = "Canvas height",
            TextAlign = HorizontalAlignment.Right,
        };
        Controls.Add(_h);

        _transparent = new MintCheckBox
        {
            Text = "Transparent", Left = 156, Top = 18, Width = 100, Checked = true,
        };
        Controls.Add(_transparent);
        if (clipboardSize)
            Controls.Add(new Label { Text = "Starting size from the clipboard image.", Left = 16,
                Top = 78, Width = 240, Height = 18, Tag = "muted", AccessibleName = "Clipboard image size" });

        var ok = new Button { Text = "Create", Left = 96, Top = 100, Width = 72, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancel", Left = 176, Top = 100, Width = 72, DialogResult = DialogResult.Cancel };
        Controls.Add(ok);
        Controls.Add(cancel);
        string[] hints = { "Canvas width in pixels", "Canvas height in pixels", "Start with a transparent background",
            "Create the canvas", "Keep the current canvas" };
        Control[] inputs = { _w, _h, _transparent, ok, cancel };
        for (int i = 0; i < inputs.Length; i++)
        {
            inputs[i].AccessibleName ??= hints[i];
            tips.SetToolTip(inputs[i], hints[i]);
        }
        foreach (var label in Controls.OfType<Label>())
        {
            label.AccessibleName ??= label.Text;
            tips.SetToolTip(label, label.Text);
        }
        AcceptButton = ok;
        CancelButton = cancel;
    }
}
