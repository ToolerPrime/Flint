using System;

namespace Flint
{
    /// <summary>
    /// Provides pixel snapping functionality for coordinates.
    /// </summary>
    public static class PixelSnap
    {
        /// <summary>
        /// Snaps a coordinate value to the nearest integer if enabled, or returns it unchanged if disabled.
        /// </summary>
        /// <param name="value">The coordinate value to snap.</param>
        /// <param name="enabled">Whether to enable snapping.</param>
        /// <returns>The snapped coordinate value.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="value"/> is NaN or infinite.</exception>
        public static double Coordinate(double value, bool enabled)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Value cannot be NaN or infinity.");
            }

            if (enabled)
            {
                return Math.Round(value, MidpointRounding.AwayFromZero);
            }

            return value;
        }
    }
}