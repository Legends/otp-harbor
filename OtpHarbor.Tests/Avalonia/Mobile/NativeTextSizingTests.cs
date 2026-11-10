using TOTP.Avalonia.Android;

namespace TOTP.Tests.Avalonia.Mobile;

public sealed class NativeTextSizingTests
{
    [Theory]
    [InlineData(320)]
    [InlineData(360)]
    [InlineData(390)]
    [InlineData(430)]
    public void EightDigitColumn_FitsGroupedCodeAtReadableMinimumAndLargeFontScale(float viewport)
    {
        var width = NativeTextSizing.CodeColumnWidthDp(viewport, 8);
        var minimumPixels = NativeTextSizing.ResolvePixels(18, 1, 1.5f, viewport, 344);
        // Nine monospace glyphs including the grouping space, allowing 0.65 em per glyph.
        Assert.True(minimumPixels * 9 * 0.65f <= width);
        Assert.True(width > NativeTextSizing.CodeColumnWidthDp(viewport, 6));
    }

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
