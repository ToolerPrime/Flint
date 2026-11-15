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

    /// <summary>
    /// What a tween does to the selected object across new frames after the
    /// current one: move, scale and rotate about its centre, fade. Step k of N
    /// uses t = k/N; the current frame is t = 0. Moves are rounded to whole
    /// pixels so pixel art stays sharp.
    /// </summary>
    internal sealed class TweenSpec
    {
        public int NewFrames;
        public double Dx, Dy;
        public double ScaleFrom = 100, ScaleTo = 100;      // percent
        public double? OpacityFrom, OpacityTo;             // percent; null = keep the object's own
        public double Rotate;                              // degrees, + clockwise
        public bool Moves => Dx != 0 || Dy != 0;
        public bool Changes => Moves || ScaleFrom != 100 || ScaleTo != 100 || Rotate != 0 || OpacityFrom.HasValue || OpacityTo.HasValue;
    }

    private bool CreateMotionTween(Point offset, int frames)
    {
        if (offset.IsEmpty) { Say("Enter a horizontal or vertical distance to move."); return false; }
        return CreateTween(new TweenSpec { NewFrames = frames, Dx = offset.X, Dy = offset.Y }, "create motion tween");
    }

    /// <summary>The selected object at tween position <paramref name="t"/> (0..1), made from the original each time so nothing degrades.</summary>
    internal static RasterObject TweenStep(RasterObject o, TweenSpec s, double t)
    {
        int dx = (int)Math.Round(s.Dx * t, MidpointRounding.AwayFromZero);
        int dy = (int)Math.Round(s.Dy * t, MidpointRounding.AwayFromZero);
        double scale = (s.ScaleFrom + (s.ScaleTo - s.ScaleFrom) * t) / 100, rot = s.Rotate * t;
        RasterObject r;
        if (Math.Abs(scale - 1) < 1e-9 && Math.Abs(rot) < 1e-9) r = dx == 0 && dy == 0 ? (RasterObject)o.CloneObject() : o.Moved(dx, dy);
        else
        {
            double cx = o.X + o.Width / 2.0, cy = o.Y + o.Height / 2.0;
            var m = Affine.Translate(-cx, -cy).Then(Affine.Scale(scale, scale)).Then(Affine.Rotate(rot)).Then(Affine.Translate(cx + dx, cy + dy));
            r = o.Retransform(m, o.Mode);
        }
        if (s.OpacityFrom.HasValue || s.OpacityTo.HasValue)
        {
            double a = s.OpacityFrom ?? o.Opacity * 100 / 255.0, b = s.OpacityTo ?? o.Opacity * 100 / 255.0;
            r.Opacity = (byte)Math.Clamp(Math.Round((a + (b - a) * t) * 255 / 100), 0, 255);
        }
        return r;
    }

    private bool CreateTween(TweenSpec spec, string label)
    {
        if (!CanTweenObject()) return false;
        int frames = spec.NewFrames;
        if (!spec.Changes) { Say("Choose a move, scale, rotation or opacity change."); return false; }
        if (frames is < 1 or > MaxTweenFrames || Math.Abs(spec.Dx) > MaxTweenOffset || Math.Abs(spec.Dy) > MaxTweenOffset ||
            spec.ScaleFrom is <= 0 or > 1000 || spec.ScaleTo is <= 0 or > 1000)
        {
            Say("The distance, scale or frame count is outside the tween limits.");
            return false;
        }

        int start = _cur;
        int objectIndex = _objectIndex;
        var tweenLayer = ActiveLayer;
        var sources = _doc.Layers.Where(layer => !layer.Shared && layer.Cels.ContainsKey(start))
            .Select(layer => (Layer: layer, Cel: layer.Cels[start])).ToArray();
        long bytesPerFrame = sources.Sum(source =>
            source.Cel.Objects.Sum(TweenObjectBytes) + TweenObjectBytes(source.Cel.Mask));
        double grow = Math.Max(1, Math.Max(spec.ScaleFrom, spec.ScaleTo) / 100);
        bytesPerFrame += (long)(TweenObjectBytes(tweenLayer.Cels[start].Objects[objectIndex]) * (grow * grow * 2));
        if (bytesPerFrame > TweenPixelBudget / frames)
        {
            Say("These frames would copy too many pixels. Try fewer new frames.");
            return false;
        }

        // Make independent copies before changing the timeline. In particular, moving a
        // plain raster directly would reuse pixels that the start frame can still paint.
        var original = (RasterObject)tweenLayer.Cels[start].Objects[objectIndex];
        var newCels = new Cel[frames, sources.Length];
        for (int step = 1; step <= frames; step++)
        {
            for (int i = 0; i < sources.Length; i++)
            {
                var copy = sources[i].Cel.Clone();
                // Step from the clone, which owns its pixels: a step made from the start
                // frame's own object would share the bitmap that frame can still paint.
                if (sources[i].Layer == tweenLayer)
                    copy.Objects[objectIndex] = TweenStep((RasterObject)copy.Objects[objectIndex], spec, step / (double)frames);
                newCels[step - 1, i] = copy;
            }
        }
        var startState = spec.OpacityFrom.HasValue || spec.ScaleFrom != 100 ? TweenStep((RasterObject)original.CloneObject(), spec, 0) : null;

        var before = Before();
        if (startState != null) tweenLayer.Cels[start].Objects[objectIndex] = startState;
        for (int step = 1; step <= frames; step++)
        {
            int frame = start + step;
            _doc.InsertFrame(frame, _doc.Frames[start].Clone());
            for (int i = 0; i < sources.Length; i++)
                sources[i].Layer.Cels.Add(frame, newCels[step - 1, i]);
        }

        _cur = _selStart = _selEnd = start + frames;
        SelectObject(objectIndex);
        PushStructure(label, before);
        Say($"{(label == "create motion tween" ? "Motion tween" : "Animation")} created with {frames} new {(frames == 1 ? "frame" : "frames")}.");
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
