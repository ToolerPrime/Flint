namespace Flint
{
    public static class MaskPixels
    {
        public static byte[] Apply(byte[] bgra, byte[] mask)
        {
            if (bgra == null)
                throw new ArgumentNullException(nameof(bgra));
            if (mask == null)
                throw new ArgumentNullException(nameof(mask));

            int pixelCount = bgra.Length / 4;
            if (bgra.Length % 4 != 0)
                throw new ArgumentException("BGRA array length must be divisible by 4.", nameof(bgra));
            if (mask.Length != pixelCount)
                throw new ArgumentException("Mask length must equal BGRA length divided by 4.", nameof(mask));

            // Handle empty inputs
            if (bgra.Length == 0 && mask.Length == 0)
                return new byte[0];

            // Clone the input array to avoid mutation
            byte[] result = new byte[bgra.Length];
            System.Array.Copy(bgra, result, bgra.Length);

            for (int i = 0; i < pixelCount; i++)
            {
                // Alpha is at index i*3 + 3 in BGRA format?
                // Wait, BGRA means Blue, Green, Red, Alpha.
                // So for pixel i:
                // B = result[i*4]
                // G = result[i*4 + 1]
                // R = result[i*4 + 2]
                // A = result[i*4 + 3]

                int alphaIndex = i * 4 + 3;
                byte sourceAlpha = result[alphaIndex];
                byte maskValue = mask[i];

                // Calculate new alpha: (sourceAlpha * mask[i] + 127) / 255
                // Using integer arithmetic.
                int newAlpha = (sourceAlpha * maskValue + 127) / 255;

                result[alphaIndex] = (byte)newAlpha;
            }

            return result;
        }
    }
}
