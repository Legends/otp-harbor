using Avalonia.Media;
using TOTP.Avalonia.Shared.Controls;

namespace TOTP.Tests.Avalonia.Shared;

public sealed class BrandTileTests
{
    [Fact]
    public void IconDataAndFallbackInitialsAreBindableWithoutLoadingAnImageFile()
    {
        const string geometry = "M0 0h24v24H0z";
        var transform = new ScaleTransform(0.6, 0.6);
        var sut = new BrandTile
        {
            Initials = "GH",
            IconData = geometry,
            IconTransform = transform,
            TileBackground = Brushes.Black
        };

        Assert.Equal("GH", sut.Initials);
        Assert.Same(geometry, sut.IconData);
        Assert.Same(transform, sut.IconTransform);
        Assert.Same(Brushes.Black, sut.TileBackground);
    }
}
