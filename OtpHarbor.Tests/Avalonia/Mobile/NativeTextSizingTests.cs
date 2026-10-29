using TOTP.Avalonia.Android;

namespace TOTP.Tests.Avalonia.Mobile;

public sealed class NativeTextSizingTests
{
    [Fact]
    public void ResolvePixels_DefaultFontAndReferenceWidth_PreservesRequestedSize()
    {
        var result = NativeTextSizing.ResolvePixels(17, 3, 1, 344, 344);

        Assert.Equal(51, result);
    }

    [Fact]
    public void ResolvePixels_LargeSystemFont_IsBoundedForDenseRows()
    {
        var result = NativeTextSizing.ResolvePixels(17, 3, 1.5f, 344, 344);

        Assert.Equal(17 * 3 * NativeTextSizing.MaximumDenseFontScale, result, 3);
    }

    [Fact]
    public void ResolvePixels_CompactViewport_ReducesSizeWithinReadableLimit()
    {
        var result = NativeTextSizing.ResolvePixels(17, 3, 1, 280, 344);

        Assert.Equal(17 * 3 * NativeTextSizing.MinimumViewportScale, result, 3);
    }

    [Fact]
    public void FitPixels_TextWiderThanItsRegion_ScalesItToAvailableWidth()
    {
        var result = NativeTextSizing.FitPixels(72, 144, 108, 36);

        Assert.Equal(54, result);
    }

    [Fact]
    public void FitPixels_ExtremeWidthPressure_PreservesMinimumSize()
    {
        var result = NativeTextSizing.FitPixels(72, 288, 24, 36);

        Assert.Equal(36, result);
    }
}
