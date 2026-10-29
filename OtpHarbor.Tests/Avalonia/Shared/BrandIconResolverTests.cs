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

    [Fact]
    public void ResolveAccountMetadata_DefersAndCachesIconMaterializationByBrandId()
    {
        var accountId = Guid.NewGuid();
        var definition = new BrandDefinition("github", "GitHub", "#181717", "github.svg");
        var pack = new Mock<IBrandIconPackService>();
        pack.Setup(value => value.ResolveAccount("GitHub", "alice", null))
            .Returns(definition);
        pack.Setup(value => value.TryGetIconLayers(
                "github",
                out It.Ref<IReadOnlyList<BrandIconLayer>>.IsAny))
            .Returns((string _, out IReadOnlyList<BrandIconLayer> layers) =>
            {
                layers = [new BrandIconLayer("M0 0h24v24H0z")];
                return true;
            });
        using var sut = new BrandIconResolver(pack.Object);

        var metadata = sut.ResolveAccountMetadata(accountId, "GitHub", "alice");

        Assert.False(metadata.HasIcon);
        Assert.False(sut.TryGetMaterializedIconData(metadata, out _));

        var first = sut.MaterializeIconData(metadata);
        var second = sut.MaterializeIconData(metadata);

        Assert.True(first.HasIcon);
        Assert.Same(first, second);
        Assert.True(sut.TryGetMaterializedIconData(metadata, out var cached));
        Assert.Same(first, cached);
        pack.Verify(
            value => value.TryGetIconLayers(
                "github",
                out It.Ref<IReadOnlyList<BrandIconLayer>>.IsAny),
            Times.Once);
    }

    [Fact]
    public void ResolveAccountMetadata_PreservesRuntimeDescriptorWithoutParsingSvg()
    {
        var runtime = new BrandRuntimeIcon(
            "github:0123456789abcdef",
            "runtime/github.png",
            new string('0', 64),
            128,
            128);
        var definition = new BrandDefinition(
            "github",
            "GitHub",
            "#181717",
            "github.svg",
            RuntimeIcon: runtime);
        var pack = new Mock<IBrandIconPackService>();
        pack.Setup(value => value.ResolveAccount("GitHub", "alice", null)).Returns(definition);
        using var sut = new BrandIconResolver(pack.Object);

        var metadata = sut.ResolveAccountMetadata(Guid.NewGuid(), "GitHub", "alice");
        var materialized = sut.MaterializeIconData(metadata);

        Assert.Same(runtime, metadata.RuntimeIcon);
        Assert.True(metadata.HasIcon);
        Assert.False(metadata.HasVectorIcon);
        Assert.Same(metadata, materialized);
        pack.Verify(
            value => value.TryGetIconLayers(
                It.IsAny<string>(),
                out It.Ref<IReadOnlyList<BrandIconLayer>>.IsAny),
            Times.Never);
    }

    [Fact]
    public void ResolveAccountMetadata_PreservesThemeAppearancesWithoutParsingSvg()
    {
        var lightAsset = new BrandRuntimeIcon(
            $"github:light:{new string('1', 64)}",
            null,
            new string('1', 64),
            128,
            128,
            "pack.otphicons",
            $"runtime/appearance-128/{new string('1', 64)}.png",
            "github",
            BrandIconTheme.Light);
        var darkAsset = new BrandRuntimeIcon(
            $"github:dark:{new string('2', 64)}",
            null,
            new string('2', 64),
            128,
            128,
            "pack.otphicons",
            $"runtime/appearance-128/{new string('2', 64)}.png",
            "github",
            BrandIconTheme.Dark);
        var source = new BrandIconAppearanceSource("simple-icons", "github", "icons/github.svg");
        var appearances = new BrandRuntimeAppearances(
            new BrandRuntimeAppearance(
                lightAsset,
                BrandIconAssetRole.Foreground,
                BrandIconBackgroundMode.Solid,
                "#FFFFFF",
                BrandIconAppearanceSelection.SourceDefault,
                source,
                "Synthetic light appearance."),
            new BrandRuntimeAppearance(
                darkAsset,
                BrandIconAssetRole.CompleteIcon,
                BrandIconBackgroundMode.Embedded,
                null,
                BrandIconAppearanceSelection.CompleteSourceArtwork,
                source,
                "Synthetic dark appearance."));
        var definition = new BrandDefinition(
            "github",
            "GitHub",
            "#FFFFFF",
            null,
            RuntimeAppearances: appearances);
        var pack = new Mock<IBrandIconPackService>();
        pack.Setup(value => value.ResolveAccount("GitHub", "alice", null)).Returns(definition);
        using var sut = new BrandIconResolver(pack.Object);

        var metadata = sut.ResolveAccountMetadata(Guid.NewGuid(), "GitHub", "alice");
        var materialized = sut.MaterializeIconData(metadata);

        Assert.Same(appearances, metadata.RuntimeAppearances);
        Assert.Same(lightAsset, metadata.RuntimeAppearanceFor(global::Avalonia.Styling.ThemeVariant.Light)?.Asset);
        Assert.Same(darkAsset, metadata.RuntimeAppearanceFor(global::Avalonia.Styling.ThemeVariant.Dark)?.Asset);
        Assert.True(metadata.HasIcon);
        Assert.False(metadata.HasVectorIcon);
        Assert.Same(metadata, materialized);
        pack.Verify(
            value => value.TryGetIconLayers(
                It.IsAny<string>(),
                out It.Ref<IReadOnlyList<BrandIconLayer>>.IsAny),
            Times.Never);
    }

    [Fact]
    public void CatalogChange_InvalidatesDeferredMetadata()
    {
        var definition = new BrandDefinition("github", "GitHub", "#181717", "github.svg");
        var pack = new Mock<IBrandIconPackService>();
        pack.Setup(value => value.ResolveAccount("GitHub", null, null)).Returns(definition);
        using var sut = new BrandIconResolver(pack.Object);
        var first = sut.ResolveAccountMetadata(Guid.NewGuid(), "GitHub", null);

        pack.Raise(value => value.CatalogChanged += null, EventArgs.Empty);
        var second = sut.ResolveAccountMetadata(Guid.NewGuid(), "GitHub", null);

        Assert.NotSame(first, second);
    }
}
