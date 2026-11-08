namespace Flint
{
    using System;
    using System.Drawing;

    public static class ResizePlan
    {
        public const int MaxSide = 16384;
        public const long MaxPixels = 16777216L;

        public static void Validate(int width, int height)
        {
            if (width < 1 || height < 1)
                throw new ArgumentOutOfRangeException();
            if (width > MaxSide || height > MaxSide)
                throw new ArgumentOutOfRangeException();
            if ((long)width * height > MaxPixels)
                throw new ArgumentOutOfRangeException();
        }

        public static Size Scale(int width, int height, double factor)
        {
            if (width <= 0 || height <= 0)
                throw new ArgumentOutOfRangeException();
            if (!double.IsFinite(factor))
                throw new ArgumentOutOfRangeException();
            if (factor <= 0)
                throw new ArgumentOutOfRangeException();

            double w = Math.Max(1, Math.Round(width * factor, MidpointRounding.AwayFromZero));
            double h = Math.Max(1, Math.Round(height * factor, MidpointRounding.AwayFromZero));

            if (!double.IsFinite(w) || !double.IsFinite(h))
                throw new ArgumentOutOfRangeException();
            if (w > MaxSide || h > MaxSide)
                throw new ArgumentOutOfRangeException();

            int resultW = (int)w;
            int resultH = (int)h;

            Validate(resultW, resultH);

            return new Size(resultW, resultH);
        }
    }
}
