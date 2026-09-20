using Avalonia.Media;
using TOTP.Avalonia.Shared.Controls;

namespace TOTP.Tests.Avalonia.Shared;

public sealed class BrandTileTests
{
    [Fact]
    public void IconDataAndFallbackInitialsAreBindableWithoutLoadingAnImageFile()
    {
        const string geometry = "M0 0h24v24H0z";
        var sut = new BrandTile
        {
            Initials = "GH",
            IconData = geometry,
            TileBackground = Brushes.Black
        };

        Assert.Equal("GH", sut.Initials);
        Assert.Same(geometry, sut.IconData);
        Assert.Same(Brushes.Black, sut.TileBackground);
    }
}
