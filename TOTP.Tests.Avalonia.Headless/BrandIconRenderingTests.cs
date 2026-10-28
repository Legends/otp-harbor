using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentResults;
using System.IO.Compression;
using TOTP.Avalonia.Shared.Branding;
using TOTP.Avalonia.Shared.Controls;
using TOTP.Core.Services.Interfaces;
using TOTP.Core.Services.Models;
using TOTP.Tests.Services;

namespace TOTP.Tests.Avalonia.Headless;

public sealed class BrandIconRenderingTests
{
    [AvaloniaFact]
    public async Task BrandTile_LoadsValidatedArchiveRuntimePngWithoutSvgFallback()
    {
        var png = OtpHarborRuntimePngTestData.Create();
        var path = Path.Combine(Path.GetTempPath(), $"otp-harbor-runtime-{Guid.NewGuid():N}.otphicons");
        using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry(
                "runtime/foreground-128/test.png",
                CompressionLevel.NoCompression);
            await using var output = entry.Open();
            await output.WriteAsync(png, TestContext.Current.CancellationToken);
        }
        try
        {
            var sha256 = OtpHarborRuntimePngTestData.Sha256(png);
            var runtime = new BrandRuntimeIcon(
                $"test:{sha256}",
                null,
                sha256,
                128,
                128,
                path,
                "runtime/foreground-128/test.png",
                "test");
            var tile = new BrandTile { RuntimeIcon = runtime };
            var window = new Window { Content = tile };
            window.Show();

            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (tile.RuntimeImage is null && DateTime.UtcNow < deadline)
            {
                Dispatcher.UIThread.RunJobs();
                await Task.Delay(10, TestContext.Current.CancellationToken);
            }

            Assert.NotNull(tile.RuntimeImage);
            Assert.Null(tile.IconData);
            Assert.Null(tile.IconImage);
            window.Close();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [AvaloniaFact]
    public async Task BrandTile_LoadsValidatedRuntimePngWithoutSvgFallback()
    {
        var png = OtpHarborRuntimePngTestData.Create();
        var path = Path.Combine(Path.GetTempPath(), $"otp-harbor-runtime-{Guid.NewGuid():N}.png");
        await File.WriteAllBytesAsync(path, png, TestContext.Current.CancellationToken);
        try
        {
            var runtime = new BrandRuntimeIcon(
                $"test:{OtpHarborRuntimePngTestData.Sha256(png)}",
                path,
                OtpHarborRuntimePngTestData.Sha256(png),
                128,
                128);
            var tile = new BrandTile { RuntimeIcon = runtime };
            var window = new Window { Content = tile };
            window.Show();

            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (tile.RuntimeImage is null && DateTime.UtcNow < deadline)
            {
                Dispatcher.UIThread.RunJobs();
                await Task.Delay(10, TestContext.Current.CancellationToken);
            }

            Assert.NotNull(tile.RuntimeImage);
            Assert.Null(tile.IconData);
            Assert.Null(tile.IconImage);

            tile.RuntimeIcon = null;
            Assert.Null(tile.RuntimeImage);
            window.Close();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [AvaloniaFact]
    public async Task BrandTile_SelectsThemeAppearanceAndAppliesItsBackgroundSemantics()
    {
        var lightPng = OtpHarborRuntimePngTestData.Create(red: 0, green: 89, blue: 156);
        var darkPng = OtpHarborRuntimePngTestData.Create(red: 24, green: 24, blue: 27);
        var lightHash = OtpHarborRuntimePngTestData.Sha256(lightPng);
        var darkHash = OtpHarborRuntimePngTestData.Sha256(darkPng);
        var path = Path.Combine(Path.GetTempPath(), $"otp-harbor-runtime-{Guid.NewGuid():N}.otphicons");
        using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            await WriteRuntimeEntryAsync(archive, lightHash, lightPng);
            await WriteRuntimeEntryAsync(archive, darkHash, darkPng);
        }

        try
        {
            var appearances = new BrandRuntimeAppearances(
                CreateAppearance(
                    BrandIconTheme.Light,
                    lightHash,
                    path,
                    BrandIconAssetRole.Foreground,
                    BrandIconBackgroundMode.Solid,
                    "#FFFFFF"),
                CreateAppearance(
                    BrandIconTheme.Dark,
                    darkHash,
                    path,
                    BrandIconAssetRole.CompleteIcon,
                    BrandIconBackgroundMode.Embedded,
                    null));
            var tile = new BrandTile { RuntimeAppearances = appearances };
            var window = new Window
            {
                Content = tile,
                RequestedThemeVariant = global::Avalonia.Styling.ThemeVariant.Light
            };
            window.Show();

            await WaitForRuntimeImageAsync(tile);
            var lightImage = Assert.IsAssignableFrom<IImage>(tile.RuntimeImage);
            var lightBackground = Assert.IsType<SolidColorBrush>(tile.EffectiveTileBackground);
            Assert.Equal(Colors.White, lightBackground.Color);

            window.RequestedThemeVariant = global::Avalonia.Styling.ThemeVariant.Dark;
            Dispatcher.UIThread.RunJobs();
            await WaitForRuntimeImageAsync(tile, lightImage);

            Assert.NotNull(tile.RuntimeImage);
            Assert.NotSame(lightImage, tile.RuntimeImage);
            Assert.Same(Brushes.Transparent, tile.EffectiveTileBackground);
            Assert.Null(tile.IconData);
            Assert.Null(tile.IconImage);
            window.Close();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [AvaloniaFact]
    public async Task BrandTile_RendersCompleteEmbeddedV3CanvasWithoutBackgroundOrSecondInset()
    {
        var png = OtpHarborRuntimePngTestData.Create(
            red: 183,
            green: 28,
            blue: 28,
            transparentInset: 8);
        var sha256 = OtpHarborRuntimePngTestData.Sha256(png);
        var path = Path.Combine(Path.GetTempPath(), $"otp-harbor-runtime-{Guid.NewGuid():N}.otphicons");
        using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
            await WriteRuntimeEntryAsync(archive, sha256, png);

        try
        {
            var appearances = new BrandRuntimeAppearances(
                CreateAppearance(
                    BrandIconTheme.Light,
                    sha256,
                    path,
                    BrandIconAssetRole.CompleteIcon,
                    BrandIconBackgroundMode.Embedded,
                    null,
                    BrandIconAppearanceSelection.ReviewedBadgeComposition),
                CreateAppearance(
                    BrandIconTheme.Dark,
                    sha256,
                    path,
                    BrandIconAssetRole.CompleteIcon,
                    BrandIconBackgroundMode.Embedded,
                    null,
                    BrandIconAppearanceSelection.ReviewedBadgeComposition));
            var tile = new BrandTile { RuntimeAppearances = appearances };
            Assert.Equal(128, appearances.LightTheme.Asset.Width);
            Assert.Equal(128, appearances.LightTheme.Asset.Height);
            Assert.Equal(
                png,
                await RuntimeBrandIconAssetReader.ReadValidatedAsync(
                    appearances.LightTheme.Asset,
                    TestContext.Current.CancellationToken));
            var window = new Window
            {
                Content = tile,
                RequestedThemeVariant = global::Avalonia.Styling.ThemeVariant.Light
            };
            window.Show();

            await WaitForRuntimeImageAsync(tile);
            Assert.NotNull(tile.RuntimeImage);
            Assert.Same(Brushes.Transparent, tile.EffectiveTileBackground);
            var image = tile.GetVisualDescendants()
                .OfType<Image>()
                .Single(control => control.Name == "PART_RuntimeImage");
            Assert.Equal(40, image.Bounds.Width);
            Assert.Equal(40, image.Bounds.Height);
            Assert.Equal(new Thickness(0), image.Margin);
            Assert.Equal(Stretch.Uniform, image.Stretch);

            window.RequestedThemeVariant = global::Avalonia.Styling.ThemeVariant.Dark;
            Dispatcher.UIThread.RunJobs();
            await WaitForRuntimeImageAsync(tile);
            Assert.NotNull(tile.RuntimeImage);
            Assert.Same(Brushes.Transparent, tile.EffectiveTileBackground);
            window.Close();
        }
        finally
        {
            File.Delete(path);
        }
    }

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

    private static async Task WriteRuntimeEntryAsync(
        ZipArchive archive,
        string sha256,
        byte[] png)
    {
        var entry = archive.CreateEntry(
            $"runtime/appearance-128/{sha256}.png",
            CompressionLevel.NoCompression);
        await using var output = entry.Open();
        await output.WriteAsync(png, TestContext.Current.CancellationToken);
    }

    private static BrandRuntimeAppearance CreateAppearance(
        BrandIconTheme theme,
        string sha256,
        string archivePath,
        BrandIconAssetRole role,
        BrandIconBackgroundMode backgroundMode,
        string? backgroundColor,
        BrandIconAppearanceSelection? selection = null) =>
        new(
            new BrandRuntimeIcon(
                $"test:{theme.ToString().ToLowerInvariant()}:{sha256}",
                null,
                sha256,
                128,
                128,
                archivePath,
                $"runtime/appearance-128/{sha256}.png",
                "test",
                theme),
            role,
            backgroundMode,
            backgroundColor,
            selection ?? (role == BrandIconAssetRole.CompleteIcon
                ? BrandIconAppearanceSelection.CompleteSourceArtwork
                : BrandIconAppearanceSelection.SourceDefault),
            new BrandIconAppearanceSource("simple-icons", "test", "icons/test.svg"),
            "Synthetic theme appearance.");

    private static async Task WaitForRuntimeImageAsync(
        BrandTile tile,
        IImage? previous = null)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while ((tile.RuntimeImage is null || ReferenceEquals(tile.RuntimeImage, previous))
               && DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }
}
