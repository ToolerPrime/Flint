namespace Flint;

public sealed partial class MainForm
{
    private const int MaxTweenFrames = 120;
    private const int MaxTweenOffset = 20_000;
    private const long TweenPixelBudget = 256L * 1024 * 1024;

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
        if (offset.IsEmpty) { Say("Enter a horizontal or vertical distance to move."); return false; }
        return CreateTween(new TweenSpec { NewFrames = frames, Dx = offset.X, Dy = offset.Y }, "create motion tween");
    }

    /// <summary>The selected object at tween position <paramref name="t"/> (0..1), made from the original each time so nothing degrades.</summary>
    internal static RasterObject TweenStep(RasterObject o, TweenSpec s, double t)
    {
        double e = s.Ease.Apply(t);
        var (px, py) = s.CurveX is double ctlX && s.CurveY is double ctlY ? new MotionPath(s.Dx, s.Dy, ctlX, ctlY).Point(e) : (s.Dx * e, s.Dy * e);
        int dx = (int)Math.Round(px, MidpointRounding.AwayFromZero);
        int dy = (int)Math.Round(py, MidpointRounding.AwayFromZero);
        double scale = Math.Max(0.01, (s.ScaleFrom + (s.ScaleTo - s.ScaleFrom) * e) / 100), rot = s.Rotate * e;
        t = e;                                             // fades follow the ease too
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
        if (!TweenFits(start, tweenLayer.Cels[start].Objects[objectIndex], spec, frames)) return false;

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

        tweenLayer.Tweens.Add(new TweenSpan(start, frames, objectIndex, spec.Clone(), original.Opacity, tweenLayer.Cels[start].Objects.Count));
        _cur = _selStart = _selEnd = start + frames;
        SelectObject(objectIndex);
        PushStructure(label, before);
        Say($"{(label == "create motion tween" ? "Motion tween" : "Animation")} created with {frames} new {(frames == 1 ? "frame" : "frames")}.");
        return true;
    }

    /// <summary>Limits and the pixel budget, shared by Create and Edit: <paramref name="frames"/> copies of every layer's cel at <paramref name="copyFrom"/>.</summary>
    private bool TweenFits(int copyFrom, DocObject tweened, TweenSpec spec, int frames)
    {
        double[] values = { spec.Dx, spec.Dy, spec.ScaleFrom, spec.ScaleTo, spec.Rotate, spec.CurveX ?? 0, spec.CurveY ?? 0, spec.OpacityFrom ?? 0, spec.OpacityTo ?? 0 };
        if (frames is < 1 or > MaxTweenFrames || values.Any(v => !double.IsFinite(v)) || Math.Abs(spec.Dx) > MaxTweenOffset || Math.Abs(spec.Dy) > MaxTweenOffset ||
            spec.ScaleFrom is <= 0 or > 1000 || spec.ScaleTo is <= 0 or > 1000)
        {
            Say("The distance, scale or frame count is outside the tween limits.");
            return false;
        }
        long bytesPerFrame = _doc.Layers.Where(l => !l.Shared && l.Cels.ContainsKey(copyFrom))
            .Sum(l => l.Cels[copyFrom].Objects.Sum(TweenObjectBytes) + TweenObjectBytes(l.Cels[copyFrom].Mask));
        double grow = Math.Max(1, Math.Max(spec.ScaleFrom, spec.ScaleTo) / 100);
        bytesPerFrame += (long)(TweenObjectBytes(tweened) * (grow * grow * 2));
        if (bytesPerFrame > TweenPixelBudget / frames)
        {
            Say("These frames would copy too many pixels. Try fewer frames or a smaller scale.");
            return false;
        }
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
}
