using System.Drawing.Imaging;

namespace HDRScreenMirror.DirectX;

internal sealed record HdrScreenshotFrame(int Width, int Height, byte[] Pixels)
{
    internal const int BytesPerPixel = 8;
}

internal static class HdrScreenshotCapture
{
    private const float ScRgbReferenceWhiteNits = 80.0f;
    private const float ToneMapWhiteNits = 4000.0f;

    public static unsafe Bitmap ConvertToSdrBitmap(
        HdrScreenshotFrame frame,
        float paperWhiteNits,
        bool falseColorEnabled)
    {
        int expectedLength = checked(
            frame.Width * frame.Height * HdrScreenshotFrame.BytesPerPixel);
        if (frame.Pixels.Length != expectedLength)
            throw new InvalidOperationException("The screenshot frame has an invalid data length.");

        paperWhiteNits = Math.Max(paperWhiteNits, 1.0f);
        Bitmap bitmap = new(frame.Width, frame.Height, PixelFormat.Format32bppArgb);
        BitmapData? destination = null;
        try
        {
            destination = bitmap.LockBits(
                new Rectangle(0, 0, frame.Width, frame.Height),
                ImageLockMode.WriteOnly,
                PixelFormat.Format32bppArgb);

            fixed (byte* sourceBase = frame.Pixels)
            {
                byte* destinationBase = (byte*)destination.Scan0;
                int sourceStride = checked(frame.Width * HdrScreenshotFrame.BytesPerPixel);
                for (int y = 0; y < frame.Height; y++)
                {
                    ushort* sourceRow = (ushort*)(sourceBase + y * sourceStride);
                    byte* destinationRow = destinationBase + y * destination.Stride;
                    for (int x = 0; x < frame.Width; x++)
                    {
                        int sourceIndex = x * 4;
                        float red = (float)BitConverter.UInt16BitsToHalf(sourceRow[sourceIndex]) *
                            ScRgbReferenceWhiteNits;
                        float green = (float)BitConverter.UInt16BitsToHalf(sourceRow[sourceIndex + 1]) *
                            ScRgbReferenceWhiteNits;
                        float blue = (float)BitConverter.UInt16BitsToHalf(sourceRow[sourceIndex + 2]) *
                            ScRgbReferenceWhiteNits;

                        MapHdrToSdr(
                            ref red,
                            ref green,
                            ref blue,
                            paperWhiteNits,
                            falseColorEnabled);
                        WriteBgra(destinationRow + x * 4, red, green, blue);
                    }
                }
            }

            bitmap.UnlockBits(destination);
            destination = null;
            return bitmap;
        }
        catch
        {
            if (destination is not null)
                bitmap.UnlockBits(destination);
            bitmap.Dispose();
            throw;
        }
    }

    private static void MapHdrToSdr(
        ref float redNits,
        ref float greenNits,
        ref float blueNits,
        float paperWhiteNits,
        bool falseColorEnabled)
    {
        float red = redNits / paperWhiteNits;
        float green = greenNits / paperWhiteNits;
        float blue = blueNits / paperWhiteNits;
        float gray;

        if (falseColorEnabled)
        {
            gray = Math.Clamp(
                0.2126f * red + 0.7152f * green + 0.0722f * blue,
                0.0f,
                1.0f);
        }
        else
        {
            float luminance = Math.Max(
                0.2126f * red + 0.7152f * green + 0.0722f * blue,
                0.0f);
            if (luminance <= 1e-7f)
            {
                redNits = 0.0f;
                greenNits = 0.0f;
                blueNits = 0.0f;
                return;
            }

            float white = ToneMapWhiteNits / paperWhiteNits;
            float mappedLuminance = luminance *
                (1.0f + luminance / (white * white)) /
                (1.0f + luminance);
            mappedLuminance = Math.Clamp(mappedLuminance, 0.0f, 1.0f);
            float scale = mappedLuminance / luminance;
            red *= scale;
            green *= scale;
            blue *= scale;
            gray = mappedLuminance;
        }

        CompressGamut(ref red, ref green, ref blue, gray);
        redNits = Math.Clamp(red, 0.0f, 1.0f);
        greenNits = Math.Clamp(green, 0.0f, 1.0f);
        blueNits = Math.Clamp(blue, 0.0f, 1.0f);
    }

    private static void CompressGamut(
        ref float red,
        ref float green,
        ref float blue,
        float gray)
    {
        if (red is >= 0.0f and <= 1.0f &&
            green is >= 0.0f and <= 1.0f &&
            blue is >= 0.0f and <= 1.0f)
        {
            return;
        }

        gray = Math.Clamp(gray, 0.0f, 1.0f);
        float amount = 1.0f;
        amount = Math.Min(amount, FindGamutScale(red, gray));
        amount = Math.Min(amount, FindGamutScale(green, gray));
        amount = Math.Min(amount, FindGamutScale(blue, gray));
        red = gray + amount * (red - gray);
        green = gray + amount * (green - gray);
        blue = gray + amount * (blue - gray);
    }

    private static float FindGamutScale(float channel, float gray)
    {
        float difference = channel - gray;
        if (difference > 0.0f)
            return (1.0f - gray) / difference;
        if (difference < 0.0f)
            return -gray / difference;
        return 1.0f;
    }

    private static unsafe void WriteBgra(
        byte* destination,
        float red,
        float green,
        float blue)
    {
        destination[0] = ToByte(LinearToSrgb(blue));
        destination[1] = ToByte(LinearToSrgb(green));
        destination[2] = ToByte(LinearToSrgb(red));
        destination[3] = 255;
    }

    private static float LinearToSrgb(float value) => value <= 0.0031308f
        ? 12.92f * value
        : 1.055f * MathF.Pow(value, 1.0f / 2.4f) - 0.055f;

    private static byte ToByte(float value) =>
        (byte)Math.Clamp((int)MathF.Round(value * 255.0f), 0, 255);
}
