using System;
using System.Collections.Generic;

namespace Flint
{
    public static class EditableShapeGeometry
    {
        private const int MaxDimension = 16384;

        public static VectorArt Rectangle(int width, int height, uint colour)
        {
            ValidateDimensions(width, height);

            var art = new VectorArt
            {
                W = width,
                H = height
            };

            var shape = new VShape
            {
                Colour = colour
            };

            // Pixel corners: (0,0), (width,0), (width,height), (0,height)
            var contour = new List<VPt>
            {
                new VPt(0.0, 0.0),
                new VPt(width, 0.0),
                new VPt(width, height),
                new VPt(0.0, height)
            };

            shape.Contours.Add(contour);
            art.Shapes.Add(shape);

            return art;
        }

        public static VectorArt Ellipse(int width, int height, uint colour)
        {
            ValidateDimensions(width, height);

            var art = new VectorArt
            {
                W = width,
                H = height
            };

            var shape = new VShape
            {
                Colour = colour
            };

            // Center at width/2.0, height/2.0
            double centerX = width / 2.0;
            double centerY = height / 2.0;
            double radiusX = width / 2.0;
            double radiusY = height / 2.0;

            var contour = new List<VPt>(128);
            for (int i = 0; i < 128; i++)
            {
                double angle = 2.0 * Math.PI * i / 128.0;
                double x = centerX + radiusX * Math.Cos(angle);
                double y = centerY + radiusY * Math.Sin(angle);
                contour.Add(new VPt(x, y));
            }

            // No duplicated closing vertex (128 vertices for a closed polygon)
            shape.Contours.Add(contour);
            art.Shapes.Add(shape);

            return art;
        }

        private static void ValidateDimensions(int width, int height)
        {
            if (width <= 0 || width > MaxDimension)
            {
                throw new ArgumentOutOfRangeException(nameof(width), $"Width must be positive and <= {MaxDimension}.");
            }

            if (height <= 0 || height > MaxDimension)
            {
                throw new ArgumentOutOfRangeException(nameof(height), $"Height must be positive and <= {MaxDimension}.");
            }
        }
    }
}
