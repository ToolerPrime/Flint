namespace Flint;

public sealed partial class MainForm
{
    private const int MaxTweenFrames = 120;
    private const int MaxTweenOffset = 20_000;
    private const long TweenPixelBudget = 256L * 1024 * 1024;

    private void RunMotionTween()
    {
        FinishFloat();
        StopPlay();
        if (!CanTweenObject()) return;

        using var dialog = new MotionTweenDialog();
        if (dialog.ShowDialog(this) == DialogResult.OK)
            CreateMotionTween(dialog.Offset, dialog.Frames);
    }

    private bool CanTweenObject()
    {
        if (ActiveLayer.Shared) { Say("Motion tween needs a layer with separate frames."); return false; }
        if (ActiveLayer.Locked || SelectedObject == null || SelectedObjectIndices.Length != 1)
        {
            Say("Select one object on an unlocked layer to create a motion tween.");
            return false;
        }
        return true;
    }

    private bool CreateMotionTween(Point offset, int frames)
    {
        if (!CanTweenObject()) return false;
        if (offset.IsEmpty) { Say("Enter a horizontal or vertical distance to move."); return false; }
        if (frames is < 1 or > MaxTweenFrames ||
            offset.X is < -MaxTweenOffset or > MaxTweenOffset || offset.Y is < -MaxTweenOffset or > MaxTweenOffset)
        {
            Say("The distance or frame count is outside the motion tween limits.");
            return false;
        }

        int start = _cur;
        int objectIndex = _objectIndex;
        var tweenLayer = ActiveLayer;
        var sources = _doc.Layers.Where(layer => !layer.Shared && layer.Cels.ContainsKey(start))
            .Select(layer => (Layer: layer, Cel: layer.Cels[start])).ToArray();
        long bytesPerFrame = sources.Sum(source =>
            source.Cel.Objects.Sum(TweenObjectBytes) + TweenObjectBytes(source.Cel.Mask));
        if (bytesPerFrame > TweenPixelBudget / frames)
        {
            Say("These frames would copy too many pixels. Try fewer new frames.");
            return false;
        }

        // Make independent copies before changing the timeline. In particular, moving a
        // plain raster directly would reuse pixels that the start frame can still paint.
        var newCels = new Cel[frames, sources.Length];
        for (int step = 1; step <= frames; step++)
        {
            int dx = (int)Math.Round((double)(offset.X * step) / frames, MidpointRounding.AwayFromZero);
            int dy = (int)Math.Round((double)(offset.Y * step) / frames, MidpointRounding.AwayFromZero);
            for (int i = 0; i < sources.Length; i++)
            {
                var copy = sources[i].Cel.Clone();
                if (sources[i].Layer == tweenLayer)
                    copy.Objects[objectIndex] = ((RasterObject)copy.Objects[objectIndex]).Moved(dx, dy);
                newCels[step - 1, i] = copy;
            }
        }

        var before = Before();
        for (int step = 1; step <= frames; step++)
        {
            int frame = start + step;
            _doc.InsertFrame(frame, _doc.Frames[start].Clone());
            for (int i = 0; i < sources.Length; i++)
                sources[i].Layer.Cels.Add(frame, newCels[step - 1, i]);
        }

        _cur = _selStart = _selEnd = start + frames;
        SelectObject(objectIndex);
        PushStructure("create motion tween", before);
        Say($"Motion tween created with {frames} new {(frames == 1 ? "frame" : "frames")}.");
        return true;
    }

    private static long TweenObjectBytes(DocObject? obj)
    {
        if (obj == null) return 0;
        long pixels = (long)obj.Width * obj.Height;
        if (obj is RasterObject { Original: { } original })
            pixels += (long)original.Width * original.Height;
        return 4 * pixels;
    }

    internal bool TestCreateMotionTween(Point offset, int frames) => CreateMotionTween(offset, frames);

    private sealed class MotionTweenDialog : ThemedForm
    {
        private readonly NumericUpDown _x, _y, _frames;

        public Point Offset => new((int)_x.Value, (int)_y.Value);
        public int Frames => (int)_frames.Value;

        public MotionTweenDialog()
        {
            Text = "Create motion tween";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = MaximizeBox = ShowInTaskbar = false;
            ClientSize = new Size(312, 186);
            var tips = Theme.MakeTip();
            Disposed += (_, _) => tips.Dispose();

            _x = Number("&Horizontal (px)", 18, -MaxTweenOffset, MaxTweenOffset, 32, "Negative values move left.");
            _y = Number("&Vertical (px)", 52, -MaxTweenOffset, MaxTweenOffset, 0, "Negative values move up.");
            _frames = Number("&New frames", 86, 1, MaxTweenFrames, 6, "Number of frames to add after the current frame.");

            var create = new Button { Text = "Create", Left = 120, Top = 138, Width = 84, DialogResult = DialogResult.OK };
            var cancel = new Button { Text = "Cancel", Left = 212, Top = 138, Width = 84, DialogResult = DialogResult.Cancel };
            Controls.Add(create);
            Controls.Add(cancel);
            tips.SetToolTip(create, "Add the new frames to the timeline.");
            tips.SetToolTip(cancel, "Close without adding frames.");
            AcceptButton = create;
            CancelButton = cancel;

            NumericUpDown Number(string label, int top, int min, int max, int value, string hint)
            {
                var caption = new Label { Text = label, Left = 16, Top = top + 3, Width = 144 };
                var input = new NumericUpDown
                {
                    AccessibleName = label.Replace("&", ""),
                    Left = 164, Top = top, Width = 132, Minimum = min, Maximum = max, Value = value,
                    TextAlign = HorizontalAlignment.Right,
                };
                Controls.Add(caption);
                Controls.Add(input);
                tips.SetToolTip(caption, hint);
                tips.SetToolTip(input, hint);
                return input;
            }
        }
    }
}
