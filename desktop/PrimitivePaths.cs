using System;
using System.Collections.Generic;

namespace Flint
{
    public static class PrimitivePaths
    {
        public static VectorArt Build(bool ellipse, int width, int height, uint colour, bool filled, int stroke)
        {
            if (width < 1 || width > 16384)
                throw new ArgumentOutOfRangeException(nameof(width), "Width must be between 1 and 16384.");
            if (height < 1 || height > 16384)
                throw new ArgumentOutOfRangeException(nameof(height), "Height must be between 1 and 16384.");
            if (stroke < 1 || stroke > 512)
                throw new ArgumentOutOfRangeException(nameof(stroke), "Stroke must be between 1 and 512.");

            var art = new VectorArt { W = width, H = height };
            var shape = new VShape { Colour = colour };

            List<VPt> outerContour;
            if (ellipse)
            {
                outerContour = GetEllipseContours(width, height);
            }
            else
            {
                outerContour = GetRectangleContours(width, height);
            }
            shape.Contours.Add(outerContour);

            if (!filled)
            {
                bool widthConsumed = 2 * stroke >= width;
                bool heightConsumed = 2 * stroke >= height;

                if (!widthConsumed && !heightConsumed)
                {
                    List<VPt> innerContour;
                    if (ellipse)
                    {
                        innerContour = GetEllipseInnerContours(width, height, stroke);
                    }
                    else
                    {
                        innerContour = GetRectangleInnerContours(width, height, stroke);
                    }
                    shape.Contours.Add(innerContour);
                }
            }

            art.Shapes.Add(shape);
            return art;
        }

        private static List<VPt> GetRectangleContours(int w, int h)
        {
            var points = new List<VPt>(4);
            points.Add(new VPt(0.0, 0.0));
            points.Add(new VPt(w, 0.0));
            points.Add(new VPt(w, h));
            points.Add(new VPt(0.0, h));
            return points;
        }

        private static List<VPt> GetEllipseContours(int w, int h)
        {
            var points = new List<VPt>(256);
            double cx = w / 2.0;
            double cy = h / 2.0;
            double rx = w / 2.0;
            double ry = h / 2.0;

            for (int i = 0; i < 256; i++)
            {
                double angle = (i * 2.0 * Math.PI) / 256.0;
                double x = cx + rx * Math.Cos(angle);
                double y = cy + ry * Math.Sin(angle);
                points.Add(new VPt(x, y));
            }
            return points;
        }

        private static List<VPt> GetRectangleInnerContours(int w, int h, int stroke)
        {
            var points = new List<VPt>(4);
            double x1 = stroke;
            double y1 = stroke;
            double x2 = w - stroke;
            double y2 = h - stroke;

            points.Add(new VPt(x1, y1));
            points.Add(new VPt(x2, y1));
            points.Add(new VPt(x2, y2));
            points.Add(new VPt(x1, y2));
            return points;
        }

        private static List<VPt> GetEllipseInnerContours(int w, int h, int stroke)
        {
            var points = new List<VPt>(256);
            double cx = w / 2.0;
            double cy = h / 2.0;
            double rx = (w / 2.0) - stroke;
            double ry = (h / 2.0) - stroke;

            for (int i = 0; i < 256; i++)
            {
                double angle = (i * 2.0 * Math.PI) / 256.0;
                double x = cx + rx * Math.Cos(angle);
                double y = cy + ry * Math.Sin(angle);
                points.Add(new VPt(x, y));
            }
            return points;
        }
    }
}
