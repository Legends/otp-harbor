using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using FluentResults;
using TOTP.Avalonia.Shared.Branding;
using TOTP.Core.Services.Interfaces;
using TOTP.Core.Services.Models;

namespace TOTP.Tests.Avalonia.Headless;

public sealed class BrandIconRenderingTests
{
    [AvaloniaFact]
    public void Resolve_MulticolorSvgBuildsOneVectorImageFromAllLayers()
    {
        using var sut = new BrandIconResolver(new MulticolorPackService());

        var resolved = sut.Resolve("Microsoft");

        var image = Assert.IsType<DrawingImage>(resolved.IconImage);
        var drawing = Assert.IsType<DrawingGroup>(image.Drawing);
        Assert.Equal(4, drawing.Children.Count);
        Assert.Null(resolved.IconData);
        Assert.Equal(4, resolved.IconLayers?.Count);
        Assert.True(resolved.HasIcon);
    }

    [AvaloniaFact]
    public void Resolve_ColoredSvgUsesItsDeclaredViewportInsteadOfClippingLargeCoordinates()
    {
        using var sut = new BrandIconResolver(new LargeViewportPackService());

        var resolved = sut.Resolve("Firebase");

        var image = Assert.IsType<DrawingImage>(resolved.IconImage);
        Assert.Equal(new Rect(75.37, 20.86, 442.36, 555.61), image.Viewbox);
        Assert.Null(resolved.IconData);
        Assert.All(resolved.IconLayers!, layer => Assert.NotNull(layer.Viewport));
        Assert.True(resolved.HasIcon);
    }

    private class MulticolorPackService : IBrandIconPackService
    {
        public event EventHandler? CatalogChanged
        {
            add { }
            remove { }
        }
        public BrandIconPackStatus Status { get; } = new(false, null, 0);
        public bool ShowIssuerLogo => true;
        public IReadOnlyList<BrandDefinition> AvailableBrands { get; } = [];
        public string? GetAccountBrandId(Guid accountId) => null;
        public virtual BrandDefinition? Resolve(string? issuer, string? explicitBrandId = null) =>
            new("microsoft", "Microsoft", "#334155", "microsoft.svg");
        public BrandDefinition? ResolveAccount(string? issuer, string? accountName, string? explicitBrandId = null) =>
            Resolve(issuer, explicitBrandId);
        public bool TryGetIconPathData(string brandId, out string pathData)
        {
            pathData = string.Empty;
            return false;
        }
        public virtual bool TryGetIconLayers(string brandId, out IReadOnlyList<BrandIconLayer> layers)
        {
            layers =
            [
                new("M1 1h10v10H1z", "#F35325"),
                new("M12 1h10v10H12z", "#81BC06"),
                new("M1 12h10v10H1z", "#05A6F0"),
                new("M12 12h10v10H12z", "#FFBA08")
            ];
            return true;
        }
        public Task<Result<BrandIconPackImportResult>> ImportAsync(Stream zipStream, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<Result<BrandIconPackImportResult>> ImportAsync(Stream zipStream, string? fileName, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<Result<BrandDefinition>> ImportCustomIconAsync(Guid accountId, Stream svgStream, string? fileName = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<Result> ResetAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result> SetShowIssuerLogoAsync(bool showIssuerLogo, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result> SetAccountBrandIdAsync(Guid accountId, string? brandId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class LargeViewportPackService : MulticolorPackService
    {
        public override BrandDefinition? Resolve(string? issuer, string? explicitBrandId = null) =>
            new("firebase", "Firebase", "#334155", "firebase.svg");

        public override bool TryGetIconLayers(string brandId, out IReadOnlyList<BrandIconLayer> layers)
        {
            var viewport = new BrandIconViewport(75.37, 20.86, 442.36, 555.61);
            layers =
            [
                new("M214 560C237 570 262 576 289 576Z", "#FF9100", Viewport: viewport),
                new("M308 21C266 55 233 99 212 151Z", "#DD2C00", Viewport: viewport)
            ];
            return true;
        }
    }
}
