namespace Flint;

public sealed class NewCanvasDialog : ThemedForm
{
    private readonly NumericUpDown _w, _h;
    private readonly CheckBox _transparent;

    public int CanvasWidth => (int)_w.Value;
    public int CanvasHeight => (int)_h.Value;
    public bool Transparent => _transparent.Checked;

    public NewCanvasDialog(int defW = 64, int defH = 64)
    {
        Text = "New canvas";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = MaximizeBox = false;
        ClientSize = new Size(268, 142);

        Controls.Add(new Label { Text = "Width", Left = 16, Top = 20, Width = 52 });
        _w = new NumericUpDown
        {
            Left = 72, Top = 17, Width = 72, Minimum = 1, Maximum = 20000, Value = defW,
            TextAlign = HorizontalAlignment.Right,
        };
        Controls.Add(_w);

        Controls.Add(new Label { Text = "Height", Left = 16, Top = 52, Width = 52 });
        _h = new NumericUpDown
        {
            Left = 72, Top = 49, Width = 72, Minimum = 1, Maximum = 20000, Value = defH,
            TextAlign = HorizontalAlignment.Right,
        };
        Controls.Add(_h);

        _transparent = new MintCheckBox
        {
            Text = "transparent", Left = 156, Top = 18, Width = 100, Checked = true,
        };
        Controls.Add(_transparent);

        var ok = new Button { Text = "Create", Left = 96, Top = 100, Width = 72, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancel", Left = 176, Top = 100, Width = 72, DialogResult = DialogResult.Cancel };
        Controls.Add(ok);
        Controls.Add(cancel);
        AcceptButton = ok;
        CancelButton = cancel;
    }
}
