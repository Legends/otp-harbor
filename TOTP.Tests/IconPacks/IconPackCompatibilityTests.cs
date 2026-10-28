using TOTP.Core.Icons;
using TOTP.Core.Services.Models;
using TOTP.Infrastructure.Icons;

namespace TOTP.Tests.IconPacks;

public sealed class IconPackCompatibilityTests
{
    private const string AegisArchiveVariable = "OTP_HARBOR_AEGIS_ICON_PACK";
    private const string OtpHarborArchiveVariable = "OTP_HARBOR_UNIFIED_ICON_PACK";
    private const string SimpleIconsArchiveVariable = "OTP_HARBOR_SIMPLE_ICONS_PACK";

    public static bool AegisArchiveIsAvailable => ArchiveIsAvailable(AegisArchiveVariable);

    public static bool OtpHarborArchiveIsAvailable => ArchiveIsAvailable(OtpHarborArchiveVariable);

    public static bool SimpleIconsArchiveIsAvailable => ArchiveIsAvailable(SimpleIconsArchiveVariable);

    [Fact(
        Skip = "The latest OTP Harbor icon pack is supplied only by the compatibility workflow.",
        SkipUnless = nameof(OtpHarborArchiveIsAvailable))]
    [Trait("Category", "IconPackCompatibility")]
    public async Task LatestOtpHarborIconPack_IsAcceptedByProductionImporter()
    {
        var result = await ImportAsync(
            new OtpHarborIconPackImporter(),
            OtpHarborArchiveVariable);

        Assert.True(result.IsSuccess, FailureMessage(result.Errors));
        Assert.Equal(BrandIconPackFormat.OtpHarbor, result.Value.Format);
        Assert.Equal("otp-harbor-icons", result.Value.ProviderId);
        Assert.True(result.Value.Icons.Count >= 5_000, $"Only {result.Value.Icons.Count} unified icons were imported.");
        Assert.NotNull(result.Value.Metadata);
        Assert.True(result.Value.Metadata.IssuerAliases.Count >= 5_000);
        AssertUsableIcons(result.Value.Icons);
        if (result.Value.Metadata.FormatVersion == 2)
        {
            Assert.All(result.Value.Icons, icon =>
            {
                Assert.NotNull(icon.RuntimeIcon);
                Assert.Null(icon.RuntimeAppearances);
                Assert.Equal($"runtime/foreground-128/{icon.Id}.png", icon.RuntimeIcon.ArchivePath);
                AssertRuntimeAsset(icon.RuntimeIcon, "otp-harbor-foreground-v1");
            });
        }
        else
        {
            Assert.Equal(3, result.Value.Metadata.FormatVersion);
            Assert.All(result.Value.Icons, icon =>
            {
                Assert.Null(icon.RuntimeIcon);
                var appearances = Assert.IsType<ImportedRuntimeAppearances>(icon.RuntimeAppearances);
                AssertRuntimeAppearance(appearances.LightTheme);
                AssertRuntimeAppearance(appearances.DarkTheme);
            });
        }
    }

    [Fact(
        Skip = "The latest Aegis archive is supplied only by the compatibility workflow.",
        SkipUnless = nameof(AegisArchiveIsAvailable))]
    [Trait("Category", "IconPackCompatibility")]
    public async Task LatestAegisIconPack_IsAcceptedByProductionImporter()
    {
        var result = await ImportAsync(
            new AegisIconPackImporter(),
            AegisArchiveVariable);

        Assert.True(result.IsSuccess, FailureMessage(result.Errors));
        Assert.Equal(BrandIconPackFormat.Aegis, result.Value.Format);
        Assert.Equal("aegis", result.Value.ProviderId);
        Assert.True(result.Value.Icons.Count >= 500, $"Only {result.Value.Icons.Count} Aegis icons were imported.");
        AssertUsableIcons(result.Value.Icons);
    }

    [Fact(
        Skip = "The latest Simple Icons archive is supplied only by the compatibility workflow.",
        SkipUnless = nameof(SimpleIconsArchiveIsAvailable))]
    [Trait("Category", "IconPackCompatibility")]
    public async Task LatestSimpleIconsRelease_IsAcceptedByProductionImporter()
    {
        var result = await ImportAsync(
            new SimpleIconsImporter(),
            SimpleIconsArchiveVariable);

        Assert.True(result.IsSuccess, FailureMessage(result.Errors));
        Assert.Equal(BrandIconPackFormat.SimpleIcons, result.Value.Format);
        Assert.Equal("simple-icons", result.Value.ProviderId);
        Assert.True(result.Value.Icons.Count >= 2_000, $"Only {result.Value.Icons.Count} Simple Icons entries were imported.");
        AssertUsableIcons(result.Value.Icons);
    }

    private static async Task<FluentResults.Result<IconPackImportResult>> ImportAsync(
        IIconPackImporter importer,
        string environmentVariable)
    {
        var path = Environment.GetEnvironmentVariable(environmentVariable)!;
        await using var archive = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        Assert.True(
            await importer.CanImportAsync(
                new IconPackSource { Stream = archive, FileName = Path.GetFileName(path) },
                TestContext.Current.CancellationToken),
            $"{importer.DisplayName} did not recognize the downloaded archive.");
        archive.Position = 0;
        return await importer.ImportAsync(
            new IconPackSource { Stream = archive, FileName = Path.GetFileName(path) },
            TestContext.Current.CancellationToken);
    }

    private static void AssertUsableIcons(IReadOnlyList<ImportedIcon> icons)
    {
        Assert.All(icons, icon =>
        {
            Assert.False(string.IsNullOrWhiteSpace(icon.Id));
            Assert.False(string.IsNullOrWhiteSpace(icon.Name));
            var hasRuntimeAsset = icon.RuntimeIcon is not null || icon.RuntimeAppearances is not null;
            Assert.True(
                (icon.SvgData is { Length: > 0 }) ^ hasRuntimeAsset,
                $"{icon.Id} must expose exactly one validated display asset.");
            Assert.False(string.IsNullOrWhiteSpace(icon.ProviderId));
        });
    }

    private static void AssertRuntimeAppearance(ImportedRuntimeAppearance appearance)
    {
        AssertRuntimeAsset(appearance.Asset, "otp-harbor-themed-appearance-v1");
        Assert.Equal(
            $"runtime/appearance-128/{appearance.Asset.Sha256}.png",
            appearance.Asset.ArchivePath);
        Assert.False(string.IsNullOrWhiteSpace(appearance.AppearanceSelectionReason));
        if (appearance.AssetRole == BrandIconAssetRole.Foreground)
        {
            Assert.Equal(BrandIconBackgroundMode.Solid, appearance.BackgroundMode);
            Assert.Matches("^#[0-9A-F]{6}$", appearance.BackgroundColor);
        }
        else
        {
            Assert.Equal(BrandIconAssetRole.CompleteIcon, appearance.AssetRole);
            Assert.Equal(BrandIconBackgroundMode.Embedded, appearance.BackgroundMode);
            Assert.Null(appearance.BackgroundColor);
        }
    }

    private static void AssertRuntimeAsset(ImportedRuntimeIcon runtime, string renderProfile)
    {
        Assert.Equal("image/png", runtime.MediaType);
        Assert.Equal(128, runtime.Width);
        Assert.Equal(128, runtime.Height);
        Assert.Matches("^[0-9a-f]{64}$", runtime.Sha256);
        Assert.Equal(renderProfile, runtime.RenderProfile);
    }

    private static bool ArchiveIsAvailable(string environmentVariable) =>
        Environment.GetEnvironmentVariable(environmentVariable) is { Length: > 0 } path
        && File.Exists(path);

    private static string FailureMessage(IReadOnlyList<FluentResults.IError> errors) =>
        string.Join("; ", errors.SelectMany(Flatten));

    private static IEnumerable<string> Flatten(FluentResults.IError error)
    {
        yield return error.Message;
        foreach (var reason in error.Reasons)
        {
            yield return reason.Message;
        }
    }
}
