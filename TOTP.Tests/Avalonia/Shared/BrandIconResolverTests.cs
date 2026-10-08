using Avalonia.Media;
using Moq;
using TOTP.Avalonia.Shared.Branding;
using TOTP.Core.Services.Interfaces;
using TOTP.Core.Services.Models;

namespace TOTP.Tests.Avalonia.Shared;

public sealed class BrandIconResolverTests
{
    [Fact]
    public void Resolve_UnknownIssuerReturnsStableAttractiveFallback()
    {
        using var sut = new BrandIconResolver(Mock.Of<IBrandIconPackService>());

        var first = sut.Resolve("Acme Cloud");
        var second = sut.Resolve("Acme Cloud");

        Assert.Null(first.Id);
        Assert.Equal("AC", first.Initials);
        Assert.StartsWith("#", first.BackgroundColor, StringComparison.Ordinal);
        Assert.Same(first, second);
        Assert.False(first.HasIcon);
    }

    [Theory]
    [InlineData(null, "?")]
    [InlineData("", "?")]
    [InlineData("   ", "?")]
    [InlineData("Acme", "AC")]
    public void Resolve_MalformedOrMissingIssuerNeverBreaksTheRow(string? issuer, string initials)
    {
        using var sut = new BrandIconResolver(Mock.Of<IBrandIconPackService>());

        var resolved = sut.Resolve(issuer);

        Assert.Equal(initials, resolved.Initials);
        Assert.NotNull(resolved.BackgroundBrush);
    }

    [Fact]
    public void Resolve_KnownBrandCachesParsedGeometry()
    {
        var pack = new Mock<IBrandIconPackService>();
        pack.Setup(value => value.Resolve("GitHub", null))
            .Returns(new BrandDefinition("github", "GitHub", "#181717", "github.svg"));
        pack.Setup(value => value.TryGetIconPathData("github", out It.Ref<string>.IsAny))
            .Returns((string _, out string path) => { path = "M0 0h24v24H0z"; return true; });
        pack.Setup(value => value.TryGetIconTransform("github", out It.Ref<BrandIconTransform?>.IsAny))
            .Returns((string _, out BrandIconTransform? transform) =>
            {
                transform = new BrandIconTransform(0.6, 0, 0, 0.6, 4.8, 4.8);
                return true;
            });
        using var sut = new BrandIconResolver(pack.Object);

        var first = sut.Resolve("GitHub");
        var second = sut.Resolve("GitHub");

        Assert.Equal("github", first.Id);
        Assert.True(first.HasIcon);
        Assert.IsType<MatrixTransform>(first.IconTransform);
        Assert.Equal(new BrandIconTransform(0.6, 0, 0, 0.6, 4.8, 4.8), first.SourceTransform);
        Assert.Same(first, second);
        pack.Verify(value => value.TryGetIconPathData("github", out It.Ref<string>.IsAny), Times.Once);
    }

    [Fact]
    public void Resolve_CustomSvgMarksIconForThemeAwareSurfaceAndForeground()
    {
        var pack = new Mock<IBrandIconPackService>();
        pack.Setup(value => value.Resolve("Zara", null))
            .Returns(new BrandDefinition("custom_1234", "zara", "#334155", "zara.svg"));
        pack.Setup(value => value.TryGetIconPathData("custom_1234", out It.Ref<string>.IsAny))
            .Returns((string _, out string path) => { path = "M0 0h24v24H0z"; return true; });
        using var sut = new BrandIconResolver(pack.Object);

        var resolved = sut.Resolve("Zara");

        Assert.True(resolved.IsCustomIcon);
        Assert.Equal("M0 0h24v24H0z", resolved.IconData);
    }

    [Fact]
    public void ResolveAccount_UsesPersistedAccountOverrideBeforeIssuerMatch()
    {
        var accountId = Guid.NewGuid();
        var pack = new Mock<IBrandIconPackService>();
        pack.Setup(value => value.GetAccountBrandId(accountId)).Returns("amazon");
        pack.Setup(value => value.ResolveAccount("GitHub", "alice", "amazon"))
            .Returns(new BrandDefinition("amazon", "Amazon", "#FF9900", "amazon.svg"));
        using var sut = new BrandIconResolver(pack.Object);

        var resolved = sut.ResolveAccount(accountId, "GitHub", "alice");

        Assert.Equal("amazon", resolved.Id);
        pack.Verify(value => value.ResolveAccount("GitHub", "alice", "amazon"), Times.Once);
    }
}
