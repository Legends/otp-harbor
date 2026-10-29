using TOTP.Avalonia.Mobile.Platform;

namespace TOTP.Tests.Avalonia.Mobile;

public sealed class MobileQrCapturePolicyTests
{
    [Theory]
    [InlineData(0, 1920, 1080)]
    [InlineData(-1, 1920, 1080)]
    [InlineData(1, 0, 1080)]
    [InlineData(1, 1920, 0)]
    [InlineData(1, -1, 1080)]
    [InlineData(1, 1920, -1)]
    public void CreatePlan_WhenMetadataIsInvalid_RejectsCapture(
        long encodedBytes,
        int width,
        int height)
    {
        var result = MobileQrCapturePolicy.CreatePlan(encodedBytes, width, height);

        Assert.False(result.IsAccepted);
    }

    [Fact]
    public void CreatePlan_WhenEncodedImageExceedsLimit_RejectsCapture()
    {
        var result = MobileQrCapturePolicy.CreatePlan(
            MobileQrCapturePolicy.MaximumEncodedBytes + 1,
            1920,
            1080);

        Assert.False(result.IsAccepted);
    }

    [Fact]
    public void CreatePlan_WhenSourceDimensionExceedsLimit_RejectsCapture()
    {
        var result = MobileQrCapturePolicy.CreatePlan(
            1,
            MobileQrCapturePolicy.MaximumSourceDimension + 1,
            1080);

        Assert.False(result.IsAccepted);
    }

    [Theory]
    [InlineData(1920, 1080, 1)]
    [InlineData(4032, 3024, 2)]
    [InlineData(8000, 6000, 4)]
    public void CreatePlan_WhenCaptureIsSupported_ReturnsBoundedPowerOfTwoSample(
        int width,
        int height,
        int expectedSampleSize)
    {
        var result = MobileQrCapturePolicy.CreatePlan(1024, width, height);

        Assert.True(result.IsAccepted);
        Assert.Equal(expectedSampleSize, result.SampleSize);
        Assert.InRange(
            (long)CeilingDivide(width, result.SampleSize) *
            CeilingDivide(height, result.SampleSize),
            1,
            MobileQrCapturePolicy.MaximumDecodedPixels);
    }

    [Theory]
    [InlineData(0, 1080)]
    [InlineData(1920, 0)]
    [InlineData(4097, 500)]
    [InlineData(3000, 2000)]
    public void CreateDecodeSizes_WhenDecodedBitmapIsInvalid_ReturnsNoPasses(
        int width,
        int height)
    {
        var result = MobileQrCapturePolicy.CreateDecodeSizes(width, height);

        Assert.Empty(result);
    }

    [Theory]
    [InlineData(2000, 1500, 2000, 1500, 1000, 750, 500, 375)]
    [InlineData(1001, 751, 1001, 751, 500, 375, 250, 187)]
    public void CreateDecodeSizes_WhenDecodedBitmapIsSupported_ReturnsProgressivePasses(
        int width,
        int height,
        int originalWidth,
        int originalHeight,
        int halfWidth,
        int halfHeight,
        int quarterWidth,
        int quarterHeight)
    {
        var result = MobileQrCapturePolicy.CreateDecodeSizes(width, height);

        Assert.Equal(
            [
                new MobileQrDecodeSize(originalWidth, originalHeight),
                new MobileQrDecodeSize(halfWidth, halfHeight),
                new MobileQrDecodeSize(quarterWidth, quarterHeight)
            ],
            result);
    }

    private static int CeilingDivide(int value, int divisor) =>
        (value + divisor - 1) / divisor;
}
