/* Flint icon drawings (24x24 design grid, line-art style). Generated in batches. */
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Flint;

internal static class ToolIconsArt
{
    /// <summary>Draw icon <paramref name="key"/>; false if this key has no drawing yet.</summary>
    public static bool TryDraw(Graphics g, string key, Pen p, Brush b)
    {
        switch (key)
        {
            // ICONS-BELOW (each case: draw, then return true)

            case "move":
            {
                g.DrawLine(p, 12f, 3f, 12f, 21f);
                g.DrawLine(p, 3f, 12f, 21f, 12f);
                g.DrawLines(p, new[] { new PointF(9f, 6f), new PointF(12f, 3f), new PointF(15f, 6f) });
                g.DrawLines(p, new[] { new PointF(9f, 18f), new PointF(12f, 21f), new PointF(15f, 18f) });
                g.DrawLines(p, new[] { new PointF(6f, 9f), new PointF(3f, 12f), new PointF(6f, 15f) });
                g.DrawLines(p, new[] { new PointF(18f, 9f), new PointF(21f, 12f), new PointF(18f, 15f) });
                return true;
            }
case "transform":
{
    g.DrawRectangle(p, 4f, 4f, 16f, 16f);
    g.FillEllipse(b, 2f, 2f, 4f, 4f);
    g.FillEllipse(b, 18f, 2f, 4f, 4f);
    g.FillEllipse(b, 2f, 18f, 4f, 4f);
    g.FillEllipse(b, 18f, 18f, 4f, 4f);
    return true;
}
            case "crop":
            {
                g.DrawLines(p, new[] { new PointF(7f, 3f), new PointF(7f, 17f), new PointF(21f, 17f) });
                g.DrawLines(p, new[] { new PointF(3f, 7f), new PointF(17f, 7f), new PointF(17f, 21f) });
                return true;
            }
case "select-rect":
{
    using var dp = (Pen)p.Clone();
    dp.DashPattern = new float[] { 2f, 2f };
    g.DrawRectangle(dp, 4f, 4f, 16f, 16f);
    return true;
}

case "select-ellipse":
            {
                using var dp = (Pen)p.Clone();
                dp.DashPattern = new float[] { 2f, 2f };
                g.DrawEllipse(dp, 4f, 4f, 16f, 16f);
                return true;
            }
            case "lasso":
            {
                g.DrawBezier(p, 6f, 17f, 0f, 12f, 4f, 4f, 12f, 4f);
                g.DrawBezier(p, 12f, 4f, 22f, 4f, 23f, 16f, 14f, 18f);
                g.DrawBezier(p, 14f, 18f, 10f, 19f, 6f, 17f, 6f, 20f);
                g.DrawBezier(p, 6f, 20f, 6f, 22f, 11f, 21f, 12f, 20f);
                return true;
            }
            case "wand":
            {
                g.DrawPolygon(p, new[] { new PointF(4f, 18f), new PointF(16f, 6f), new PointF(19f, 9f), new PointF(7f, 21f) });
                g.DrawLine(p, 13f, 9f, 16f, 12f);
                g.DrawLine(p, 6f, 3f, 6f, 7f);
                g.DrawLine(p, 4f, 5f, 8f, 5f);
                g.DrawLine(p, 19f, 3f, 19f, 5f);
                g.DrawLine(p, 18f, 4f, 20f, 4f);
                g.DrawLine(p, 20f, 15f, 20f, 19f);
                g.DrawLine(p, 18f, 17f, 22f, 17f);
                return true;
            }
            case "eyedropper":
            {
                g.DrawPolygon(p, new[] { new PointF(5f, 16f), new PointF(14f, 7f), new PointF(17f, 10f), new PointF(8f, 19f), new PointF(3f, 21f) });
                g.DrawLine(p, 12f, 5f, 19f, 12f);
                g.DrawBezier(p, 14f, 7f, 18f, 0f, 24f, 6f, 17f, 10f);
                return true;
            }
            case "pan":
            {
                g.DrawLines(p, new[] { new PointF(8f, 12f), new PointF(8f, 6f) });
                g.DrawBezier(p, 8f, 6f, 8f, 3f, 11f, 3f, 11f, 6f);
                g.DrawLine(p, 11f, 6f, 11f, 11f);
                g.DrawBezier(p, 11f, 6f, 11f, 2f, 14f, 2f, 14f, 6f);
                g.DrawLine(p, 14f, 6f, 14f, 11f);
                g.DrawBezier(p, 14f, 7f, 14f, 4f, 17f, 4f, 17f, 7f);
                g.DrawLine(p, 17f, 7f, 17f, 12f);
                g.DrawBezier(p, 17f, 10f, 17f, 7f, 20f, 7f, 20f, 10f);
                g.DrawLine(p, 20f, 10f, 20f, 15f);
                g.DrawBezier(p, 20f, 15f, 20f, 23f, 10f, 23f, 7f, 18f);
                g.DrawLine(p, 7f, 18f, 3f, 12f);
                g.DrawBezier(p, 3f, 12f, 2f, 9f, 5f, 9f, 8f, 12f);
                return true;
            }
            case "pencil":
            {
                g.DrawPolygon(p, new[] { new PointF(3f, 21f), new PointF(5f, 14f), new PointF(16f, 3f), new PointF(21f, 8f), new PointF(10f, 19f) });
                g.DrawLine(p, 14f, 5f, 19f, 10f);
                g.DrawLine(p, 5f, 14f, 10f, 19f);
                return true;
            }
            case "eraser":
            {
                g.DrawPolygon(p, new[] { new PointF(3f, 14f), new PointF(13f, 4f), new PointF(21f, 12f), new PointF(12f, 21f), new PointF(10f, 21f) });
                g.DrawLine(p, 8f, 9f, 16f, 17f);
                g.DrawLine(p, 12f, 21f, 21f, 21f);
                return true;
            }
            case "fill":
            {
                g.DrawPolygon(p, new[] { new PointF(3f, 12f), new PointF(11f, 4f), new PointF(19f, 12f), new PointF(11f, 20f) });
                g.DrawLine(p, 4f, 12f, 18f, 12f);
                g.DrawLine(p, 7f, 3f, 12f, 8f);
                g.DrawBezier(p, 20f, 15f, 17f, 19f, 18f, 21f, 20f, 21f);
                g.DrawBezier(p, 20f, 21f, 22f, 21f, 23f, 19f, 20f, 15f);
                return true;
            }
            case "gradient":
            {
                g.DrawRectangle(p, 3f, 5f, 18f, 14f);
                g.FillRectangle(b, 4f, 6f, 4f, 12f);
                g.DrawLine(p, 11f, 7f, 11f, 17f);
                g.DrawLine(p, 14f, 7f, 14f, 17f);
                g.FillEllipse(b, 17f, 8f, 1f, 1f);
                g.FillEllipse(b, 17f, 12f, 1f, 1f);
                g.FillEllipse(b, 17f, 16f, 1f, 1f);
                return true;
            }
            case "clone":
            {
                g.DrawBezier(p, 9f, 13f, 10f, 9f, 8f, 8f, 8f, 6f);
                g.DrawBezier(p, 8f, 6f, 8f, 2f, 16f, 2f, 16f, 6f);
                g.DrawBezier(p, 16f, 6f, 16f, 8f, 14f, 9f, 15f, 13f);
                g.DrawLines(p, new[] { new PointF(9f, 13f), new PointF(5f, 15f), new PointF(4f, 18f), new PointF(20f, 18f), new PointF(19f, 15f), new PointF(15f, 13f) });
                g.DrawLine(p, 6f, 21f, 18f, 21f);
                return true;
            }
            case "text":
            {
                g.DrawLines(p, new[] { new PointF(4f, 7f), new PointF(4f, 4f), new PointF(20f, 4f), new PointF(20f, 7f) });
                g.DrawLine(p, 12f, 4f, 12f, 20f);
                g.DrawLine(p, 8f, 20f, 16f, 20f);
                return true;
            }
            case "line":
            {
                g.DrawLine(p, 4f, 20f, 20f, 4f);
                g.FillEllipse(b, 3f, 19f, 2f, 2f);
                g.FillEllipse(b, 19f, 3f, 2f, 2f);
                return true;
            }
case "rect":
            {
                g.DrawRectangle(p, 4f, 6f, 16f, 12f);
                return true;
            }
            case "ellipse":
            {
                g.DrawEllipse(p, 4f, 4f, 16f, 16f);
                return true;
            }
            case "pivot":
            {
                g.DrawEllipse(p, 7f, 7f, 10f, 10f);
                g.DrawLine(p, 12f, 3f, 12f, 7f);
                g.DrawLine(p, 12f, 17f, 12f, 21f);
                g.DrawLine(p, 3f, 12f, 7f, 12f);
                g.DrawLine(p, 17f, 12f, 21f, 12f);
                g.FillEllipse(b, 10.5f, 10.5f, 3f, 3f);
                return true;
            }
case "hitbox":
            {
                using var dp = (Pen)p.Clone();
                dp.DashPattern = new float[] { 2f, 2f };
                g.DrawRectangle(dp, 4f, 4f, 16f, 16f);
                g.FillRectangle(b, 18f, 18f, 4f, 4f);
                return true;
            }
            case "new":
            {
                g.DrawPolygon(p, new[] { new PointF(5f, 3f), new PointF(14f, 3f), new PointF(20f, 9f), new PointF(20f, 21f), new PointF(5f, 21f) });
                g.DrawLines(p, new[] { new PointF(14f, 3f), new PointF(14f, 9f), new PointF(20f, 9f) });
                return true;
            }
            case "open":
            {
                g.DrawLines(p, new[] { new PointF(3f, 18f), new PointF(3f, 5f), new PointF(9f, 5f), new PointF(12f, 8f), new PointF(20f, 8f), new PointF(20f, 11f) });
                g.DrawPolygon(p, new[] { new PointF(3f, 20f), new PointF(6f, 11f), new PointF(22f, 11f), new PointF(19f, 20f) });
                return true;
            }
            case "save":
            {
                g.DrawPolygon(p, new[] { new PointF(4f, 3f), new PointF(17f, 3f), new PointF(21f, 7f), new PointF(21f, 21f), new PointF(4f, 21f) });
                g.DrawRectangle(p, 8f, 3f, 8f, 6f);
                g.DrawRectangle(p, 8f, 14f, 9f, 7f);
                return true;
            }
            case "import":
            {
                g.DrawLines(p, new[] { new PointF(4f, 14f), new PointF(4f, 21f), new PointF(20f, 21f), new PointF(20f, 14f) });
                g.DrawLine(p, 12f, 3f, 12f, 16f);
                g.DrawLines(p, new[] { new PointF(8f, 12f), new PointF(12f, 16f), new PointF(16f, 12f) });
                return true;
            }
            case "export":
            {
                g.DrawLines(p, new[] { new PointF(4f, 14f), new PointF(4f, 21f), new PointF(20f, 21f), new PointF(20f, 14f) });
                g.DrawLine(p, 12f, 3f, 12f, 16f);
                g.DrawLines(p, new[] { new PointF(8f, 7f), new PointF(12f, 3f), new PointF(16f, 7f) });
                return true;
            }
            case "undo":
            {
                g.DrawBezier(p, 4f, 10f, 15f, 2f, 23f, 10f, 19f, 19f);
                g.DrawLines(p, new[] { new PointF(4f, 4f), new PointF(4f, 10f), new PointF(10f, 10f) });
                return true;
            }
            case "redo":
            {
                g.DrawBezier(p, 20f, 10f, 9f, 2f, 1f, 10f, 5f, 19f);
                g.DrawLines(p, new[] { new PointF(20f, 4f), new PointF(20f, 10f), new PointF(14f, 10f) });
                return true;
            }
            case "play":
            {
                g.FillPolygon(b, new[] { new PointF(7f, 4f), new PointF(21f, 12f), new PointF(7f, 20f) });
                return true;
            }
            case "stop":
            {
                g.FillRectangle(b, 5f, 5f, 14f, 14f);
                return true;
            }
            case "loop":
            {
                g.DrawArc(p, 4f, 4f, 16f, 16f, 210f, 150f);
                g.DrawLines(p, new[] { new PointF(17f, 9f), new PointF(20f, 12f), new PointF(22f, 8f) });
                g.DrawArc(p, 4f, 4f, 16f, 16f, 30f, 150f);
                g.DrawLines(p, new[] { new PointF(7f, 15f), new PointF(4f, 12f), new PointF(2f, 16f) });
                return true;
            }
            case "prev":
            {
                g.DrawLine(p, 4f, 5f, 4f, 19f);
                g.FillPolygon(b, new[] { new PointF(19f, 5f), new PointF(8f, 12f), new PointF(19f, 19f) });
                return true;
            }
            case "next":
            {
                g.DrawLine(p, 20f, 5f, 20f, 19f);
                g.FillPolygon(b, new[] { new PointF(5f, 5f), new PointF(16f, 12f), new PointF(5f, 19f) });
                return true;
            }
            case "add-frame":
            {
                using var frame = new GraphicsPath();
                frame.AddArc(3f, 4f, 4f, 4f, 180f, 90f);
                frame.AddArc(17f, 4f, 4f, 4f, 270f, 90f);
                frame.AddArc(17f, 16f, 4f, 4f, 0f, 90f);
                frame.AddArc(3f, 16f, 4f, 4f, 90f, 90f);
                frame.CloseFigure();
                g.DrawPath(p, frame);
                g.DrawLine(p, 8f, 12f, 16f, 12f);
                g.DrawLine(p, 12f, 8f, 12f, 16f);
                return true;
            }
            case "dup-frame":
            {
                using var frame = new GraphicsPath();
                frame.AddArc(7f, 8f, 4f, 4f, 180f, 90f);
                frame.AddArc(17f, 8f, 4f, 4f, 270f, 90f);
                frame.AddArc(17f, 17f, 4f, 4f, 0f, 90f);
                frame.AddArc(7f, 17f, 4f, 4f, 90f, 90f);
                frame.CloseFigure();
                g.DrawPath(p, frame);
                g.DrawLines(p, new[] { new PointF(17f, 5f), new PointF(17f, 3f), new PointF(3f, 3f), new PointF(3f, 16f), new PointF(4f, 16f) });
                return true;
            }
            case "delete-frame":
            {
                using var frame = new GraphicsPath();
                frame.AddArc(3f, 4f, 4f, 4f, 180f, 90f);
                frame.AddArc(17f, 4f, 4f, 4f, 270f, 90f);
                frame.AddArc(17f, 16f, 4f, 4f, 0f, 90f);
                frame.AddArc(3f, 16f, 4f, 4f, 90f, 90f);
                frame.CloseFigure();
                g.DrawPath(p, frame);
                g.DrawLine(p, 9f, 9f, 15f, 15f);
                g.DrawLine(p, 15f, 9f, 9f, 15f);
                return true;
            }
            case "tag":
            {
                g.DrawPolygon(p, new[] { new PointF(3f, 3f), new PointF(12f, 3f), new PointF(21f, 12f), new PointF(12f, 21f), new PointF(3f, 12f) });
                g.DrawEllipse(p, 6f, 6f, 3f, 3f);
                return true;
            }
            case "zoom-in":
            {
                g.DrawEllipse(p, 3f, 3f, 13f, 13f);
                g.DrawLine(p, 14.5f, 14.5f, 21f, 21f);
                g.DrawLine(p, 6.5f, 9.5f, 12.5f, 9.5f);
                g.DrawLine(p, 9.5f, 6.5f, 9.5f, 12.5f);
                return true;
            }
            case "zoom-out":
            {
                g.DrawEllipse(p, 3f, 3f, 13f, 13f);
                g.DrawLine(p, 14.5f, 14.5f, 21f, 21f);
                g.DrawLine(p, 6.5f, 9.5f, 12.5f, 9.5f);
                return true;
            }
            case "onion":
            {
                g.DrawEllipse(p, 3f, 6f, 12f, 12f);
                g.DrawEllipse(p, 6f, 6f, 12f, 12f);
                g.DrawEllipse(p, 9f, 6f, 12f, 12f);
                return true;
            }
            case "record":
            {
                g.FillEllipse(b, 5f, 5f, 14f, 14f);
                return true;
            }
            case "audio":
            {
                g.DrawPolygon(p, new[] { new PointF(3f, 9f), new PointF(7f, 9f), new PointF(12f, 5f), new PointF(12f, 19f), new PointF(7f, 15f), new PointF(3f, 15f) });
                g.DrawBezier(p, 16f, 8f, 19f, 10f, 19f, 14f, 16f, 16f);
                g.DrawBezier(p, 19f, 4f, 23f, 8f, 23f, 16f, 19f, 20f);
                return true;
            }
            case "eye":
            {
                g.DrawBezier(p, 3f, 12f, 7f, 3f, 17f, 3f, 21f, 12f);
                g.DrawBezier(p, 21f, 12f, 17f, 21f, 7f, 21f, 3f, 12f);
                g.DrawEllipse(p, 9f, 9f, 6f, 6f);
                return true;
            }
            case "eye-off":
            {
                g.DrawBezier(p, 3f, 12f, 4f, 9f, 6f, 7f, 7f, 7f);
                g.DrawBezier(p, 3f, 12f, 8f, 20f, 16f, 19f, 18f, 16f);
                g.DrawBezier(p, 11f, 6f, 16f, 5f, 19f, 9f, 21f, 12f);
                g.DrawLine(p, 21f, 12f, 20f, 14f);
                g.DrawArc(p, 9f, 9f, 6f, 6f, 35f, 180f);
                g.DrawLine(p, 3f, 3f, 21f, 21f);
                return true;
            }
            case "lock":
            {
                using var frame = new GraphicsPath();
                frame.AddArc(5f, 11f, 4f, 4f, 180f, 90f);
                frame.AddArc(15f, 11f, 4f, 4f, 270f, 90f);
                frame.AddArc(15f, 17f, 4f, 4f, 0f, 90f);
                frame.AddArc(5f, 17f, 4f, 4f, 90f, 90f);
                frame.CloseFigure();
                g.DrawPath(p, frame);
                g.DrawLine(p, 8f, 11f, 8f, 7f);
                g.DrawArc(p, 8f, 3f, 8f, 8f, 180f, 180f);
                g.DrawLine(p, 16f, 7f, 16f, 11f);
                g.DrawLine(p, 12f, 15f, 12f, 17f);
                return true;
            }
            case "unlock":
            {
                using var frame = new GraphicsPath();
                frame.AddArc(5f, 11f, 4f, 4f, 180f, 90f);
                frame.AddArc(15f, 11f, 4f, 4f, 270f, 90f);
                frame.AddArc(15f, 17f, 4f, 4f, 0f, 90f);
                frame.AddArc(5f, 17f, 4f, 4f, 90f, 90f);
                frame.CloseFigure();
                g.DrawPath(p, frame);
                g.DrawLine(p, 8f, 11f, 8f, 7f);
                g.DrawArc(p, 8f, 3f, 8f, 8f, 180f, 180f);
                g.DrawLine(p, 12f, 15f, 12f, 17f);
                return true;
            }
            case "mask":
            {
                g.DrawRectangle(p, 3f, 3f, 18f, 18f);
                g.DrawEllipse(p, 7f, 7f, 10f, 10f);
                return true;
            }
            case "add-layer":
            {
                g.DrawPolygon(p, new[] { new PointF(3f, 7f), new PointF(10f, 3f), new PointF(17f, 7f), new PointF(10f, 11f) });
                g.DrawLines(p, new[] { new PointF(3f, 12f), new PointF(10f, 16f), new PointF(13f, 14f) });
                g.DrawLines(p, new[] { new PointF(3f, 17f), new PointF(10f, 21f), new PointF(13f, 19f) });
                g.DrawLine(p, 16f, 17f, 22f, 17f);
                g.DrawLine(p, 19f, 14f, 19f, 20f);
                return true;
            }
            case "delete-layer":
            {
                g.DrawPolygon(p, new[] { new PointF(3f, 7f), new PointF(10f, 3f), new PointF(17f, 7f), new PointF(10f, 11f) });
                g.DrawLines(p, new[] { new PointF(3f, 12f), new PointF(10f, 16f), new PointF(13f, 14f) });
                g.DrawLines(p, new[] { new PointF(3f, 17f), new PointF(10f, 21f), new PointF(13f, 19f) });
                g.DrawLine(p, 16f, 17f, 22f, 17f);
                return true;
            }
            case "layer-up":
            {
                g.DrawLine(p, 4f, 21f, 20f, 21f);
                g.DrawLine(p, 12f, 4f, 12f, 16f);
                g.DrawLines(p, new[] { new PointF(7f, 9f), new PointF(12f, 4f), new PointF(17f, 9f) });
                return true;
            }
            case "layer-down":
            {
                g.DrawLine(p, 4f, 21f, 20f, 21f);
                g.DrawLine(p, 12f, 3f, 12f, 16f);
                g.DrawLines(p, new[] { new PointF(7f, 11f), new PointF(12f, 16f), new PointF(17f, 11f) });
                return true;
            }
            case "duplicate":
            {
                g.DrawRectangle(p, 8f, 8f, 13f, 13f);
                g.DrawLines(p, new[] { new PointF(16f, 4f), new PointF(16f, 3f), new PointF(3f, 3f), new PointF(3f, 16f), new PointF(4f, 16f) });
                return true;
            }
            case "effects":
            {
                g.DrawPolygon(p, new[] { new PointF(10f, 4f), new PointF(12.5f, 10.5f), new PointF(19f, 13f), new PointF(12.5f, 15.5f), new PointF(10f, 22f), new PointF(7.5f, 15.5f), new PointF(2f, 13f), new PointF(7.5f, 10.5f) });
                g.DrawPolygon(p, new[] { new PointF(19f, 2f), new PointF(20f, 5f), new PointF(22f, 6f), new PointF(20f, 7f), new PointF(19f, 10f), new PointF(18f, 7f), new PointF(16f, 6f), new PointF(18f, 5f) });
                return true;
            }
            case "library":
            {
                g.DrawRectangle(p, 4f, 4f, 4f, 15f);
                g.DrawRectangle(p, 8f, 4f, 4f, 15f);
                g.DrawPolygon(p, new[] { new PointF(14f, 5f), new PointF(18f, 4f), new PointF(21f, 18f), new PointF(17f, 19f) });
                g.DrawLine(p, 3f, 21f, 21f, 21f);
                return true;
            }
            case "history":
            {
                g.DrawArc(p, 4f, 4f, 16f, 16f, 210f, 300f);
                g.DrawLines(p, new[] { new PointF(3f, 3f), new PointF(3f, 8f), new PointF(8f, 8f) });
                g.DrawLines(p, new[] { new PointF(12f, 7f), new PointF(12f, 12f), new PointF(16f, 14f) });
                return true;
            }
            default:
                return false;
        }
    }
}
