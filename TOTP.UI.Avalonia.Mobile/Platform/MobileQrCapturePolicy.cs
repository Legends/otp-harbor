namespace TOTP.Avalonia.Mobile.Platform;

public readonly record struct MobileQrCapturePlan(bool IsAccepted, int SampleSize);
public readonly record struct MobileQrDecodeSize(int Width, int Height);

public static class MobileQrCapturePolicy
{
    private static readonly int[] DecodeDivisors = [1, 2, 4];

    public const long MaximumEncodedBytes = 32L * 1024 * 1024;
    public const int MaximumSourceDimension = 32_768;
    public const int MaximumDecodedDimension = 4_096;
    public const int MaximumDecodedPixels = 4_000_000;

    public static MobileQrCapturePlan CreatePlan(
        long encodedBytes,
        int width,
        int height)
    {
        if (encodedBytes <= 0 || encodedBytes > MaximumEncodedBytes ||
            width <= 0 || height <= 0 ||
            width > MaximumSourceDimension || height > MaximumSourceDimension)
        {
            return default;
        }

        var sampleSize = 1;
        while (DecodedWidth(width, sampleSize) > MaximumDecodedDimension ||
               DecodedWidth(height, sampleSize) > MaximumDecodedDimension ||
               (long)DecodedWidth(width, sampleSize) * DecodedWidth(height, sampleSize) >
               MaximumDecodedPixels)
        {
            if (sampleSize > MaximumSourceDimension / 2) return default;
            sampleSize *= 2;
        }

        return new MobileQrCapturePlan(true, sampleSize);
    }

    public static IReadOnlyList<MobileQrDecodeSize> CreateDecodeSizes(
        int width,
        int height)
    {
        if (width <= 0 || height <= 0 ||
            width > MaximumDecodedDimension || height > MaximumDecodedDimension ||
            (long)width * height > MaximumDecodedPixels)
        {
            return [];
        }

        return DecodeDivisors
            .Select(divisor => new MobileQrDecodeSize(
                Math.Max(1, width / divisor),
                Math.Max(1, height / divisor)))
            .Distinct()
            .ToArray();
    }

    private static int DecodedWidth(int value, int sampleSize) =>
        (value + sampleSize - 1) / sampleSize;
}
