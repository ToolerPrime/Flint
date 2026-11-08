using System;
using System.Drawing;

namespace Flint
{
    public enum CanvasAlign
    {
        Left,
        HorizontalCentre,
        Right,
        Top,
        VerticalCentre,
        Bottom
    }

    public static class CanvasAlignment
    {
        public static Point Offset(Rectangle bounds, int canvasWidth, int canvasHeight, CanvasAlign edge)
        {
            if (canvasWidth < 1 || canvasHeight < 1)
            {
                throw new ArgumentOutOfRangeException("Canvas dimensions must be >= 1.");
            }

            if (bounds.Width < 1 || bounds.Height < 1)
            {
                throw new ArgumentOutOfRangeException("Bounds width and height must be >= 1.");
            }

            int dx = 0;
            int dy = 0;

            switch (edge)
            {
                case CanvasAlign.Left:
                    dx = -bounds.Left;
                    break;
                case CanvasAlign.Right:
                    dx = canvasWidth - bounds.Right;
                    break;
                case CanvasAlign.HorizontalCentre:
                    dx = (int)Math.Round((canvasWidth - bounds.Width) / 2.0 - bounds.Left, MidpointRounding.AwayFromZero);
                    break;
                case CanvasAlign.Top:
                    dy = -bounds.Top;
                    break;
                case CanvasAlign.VerticalCentre:
                    dy = (int)Math.Round((canvasHeight - bounds.Height) / 2.0 - bounds.Top, MidpointRounding.AwayFromZero);
                    break;
                case CanvasAlign.Bottom:
                    dy = canvasHeight - bounds.Bottom;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(edge), edge, "Invalid enum value.");
            }

            return new Point(dx, dy);
        }
    }
}
