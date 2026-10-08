using System.IO.Compression;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TOTP.Core.Services.Interfaces;
using TOTP.Core.Services.Models;
using TOTP.Infrastructure.Services;
using TOTP.Tests.Common;

namespace TOTP.Tests.Services;

public sealed class SimpleIconsBrandIconPackServiceTests
{
    private const string AegisCompatibilityArchiveVariable = "OTP_HARBOR_AEGIS_ICON_PACK";
    private const string SimpleIconsCompatibilityArchiveVariable = "OTP_HARBOR_SIMPLE_ICONS_PACK";
    private const string UnifiedCompatibilityArchiveVariable = "OTP_HARBOR_UNIFIED_ICON_PACK";

    public static bool LatestCompatibilityArchivesAreAvailable =>
        File.Exists(Environment.GetEnvironmentVariable(AegisCompatibilityArchiveVariable))
        && File.Exists(Environment.GetEnvironmentVariable(SimpleIconsCompatibilityArchiveVariable));

    public static bool LatestUnifiedCompatibilityArchiveIsAvailable =>
        File.Exists(Environment.GetEnvironmentVariable(UnifiedCompatibilityArchiveVariable));

    [Fact]
    public async Task ImportAsync_InstallsLocalIndexAndResolvesTitlesSlugsAndKnownAliases()
    {
        using var temp = new TempDir();
        var sut = CreateSut(temp.Path);
        await using var archive = CreateArchive(includeMicrosoftEntra: true);

        var imported = await sut.ImportAsync(archive, TestContext.Current.CancellationToken);

        Assert.True(imported.IsSuccess);
        Assert.Equal("16.31.0", imported.Value.Version);
        Assert.Equal(9, imported.Value.BrandCount);
        Assert.Equal(BrandIconPackFormat.SimpleIcons, imported.Value.Format);
        Assert.Equal("github", sut.Resolve("GitHub")?.Id);
        Assert.Equal("github", sut.Resolve("github")?.Id);
        Assert.Equal("github", sut.Resolve("github.com")?.Id);
        Assert.Equal("github", sut.Resolve("github test")?.Id);
        Assert.Equal("gitlab", sut.Resolve("gitlab test")?.Id);
        Assert.Equal("microsoft", sut.Resolve("Microsoft Account")?.Id);
        Assert.Equal("microsoft", sut.Resolve("  MICROSOFT   ACCOUNT ")?.Id);
        Assert.Equal("microsoft", sut.Resolve("Microsoft 365")?.Id);
        Assert.Equal("google", sut.Resolve("Google Workspace")?.Id);
        Assert.Equal("microsoft", sut.Resolve("Microsoft test")?.Id);
        Assert.Equal("slack", sut.Resolve("Slack test")?.Id);
        Assert.Equal("amazonwebservices", sut.Resolve("AWS")?.Id);
        Assert.Equal("amazonwebservices", sut.Resolve("AWS test")?.Id);
        Assert.Equal("amazonwebservices", sut.Resolve("Amazon Web Services")?.Id);
        Assert.Equal("amazon", sut.Resolve("Amazon")?.Id);
        Assert.Equal("amazon", sut.Resolve("Amazon test")?.Id);
        Assert.Equal("microsoftazure", sut.Resolve("Azure test")?.Id);
        Assert.Equal("microsoftentra", sut.Resolve("Azure AD")?.Id);
        Assert.Equal("microsoftentra", sut.Resolve("Azure AD tenant")?.Id);
        Assert.Null(sut.Resolve("My Private Server"));
        Assert.Null(sut.Resolve(null));
        Assert.Null(sut.Resolve("  "));
    }

    [Fact]
    public async Task ImportAsync_IndexesGenericSvgFilenamesAndPreservesNotices()
    {
        using var temp = new TempDir();
        var sut = CreateSut(temp.Path);
        await using var archive = CreateFilenameIndexedArchive();

        var imported = await sut.ImportAsync(
            archive,
            "my-local-icons.zip",
            TestContext.Current.CancellationToken);

        Assert.True(imported.IsSuccess);
        Assert.Equal("filename-indexed", imported.Value.Version);
        Assert.Equal(3, imported.Value.BrandCount);
        Assert.Equal(BrandIconPackFormat.FilenameIndexed, imported.Value.Format);
        Assert.Equal("my-local-icons", imported.Value.ProviderDisplayName);
        Assert.Equal("microsoft", sut.Resolve("Office 365")?.Id);
        Assert.Equal("github", sut.Resolve("github.com")?.Id);
        Assert.Equal("google", sut.Resolve("Google Workspace")?.Id);
        Assert.True(sut.TryGetIconPathData("github", out var pathData));
        Assert.Equal("M0 0h24v24H0z", pathData);
        var packDirectory = Assert.Single(Directory.EnumerateDirectories(
            Path.Combine(temp.Path, "BrandIcons", "packs")));
        var noticeDirectory = Path.Combine(packDirectory, "notices");
        Assert.Equal(2, Directory.EnumerateFiles(noticeDirectory).Count());
        Assert.Contains(
            Directory.EnumerateFiles(noticeDirectory),
            path => File.ReadAllText(path) == "Synthetic generic license");
        Assert.Contains(
            Directory.EnumerateFiles(noticeDirectory),
            path => File.ReadAllText(path) == "Synthetic generic notice");

        var reloaded = CreateSut(temp.Path);
        Assert.True(reloaded.Status.IsInstalled);
        Assert.Equal("filename-indexed", reloaded.Status.Version);
        Assert.Equal("microsoft", reloaded.Resolve("Office 365")?.Id);
    }

    [Fact]
    public async Task ImportAsync_RejectsDuplicateGenericBrandIdsAcrossDirectories()
    {
        using var temp = new TempDir();
        var sut = CreateSut(temp.Path);
        await using var archive = new MemoryStream();
        using (var zip = new ZipArchive(archive, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(zip, "light/github.svg", ValidSvg);
            WriteEntry(zip, "dark/GitHub.svg", ValidSvg);
        }
        archive.Position = 0;

        var imported = await sut.ImportAsync(archive, TestContext.Current.CancellationToken);

        Assert.True(imported.IsFailed);
        Assert.False(sut.Status.IsInstalled);
    }

    [Fact]
    public async Task ImportAsync_RejectsUnsafeGenericFilenamesAndMalformedSvg()
    {
        using var temp = new TempDir();
        var sut = CreateSut(temp.Path);
        await using var unsafeNames = new MemoryStream();
        using (var zip = new ZipArchive(unsafeNames, ZipArchiveMode.Create, leaveOpen: true))
            WriteEntry(zip, "icons/not-canonical.svg", ValidSvg);
        unsafeNames.Position = 0;
        Assert.True((await sut.ImportAsync(
            unsafeNames,
            TestContext.Current.CancellationToken)).IsFailed);

        await using var malformed = new MemoryStream();
        using (var zip = new ZipArchive(malformed, ZipArchiveMode.Create, leaveOpen: true))
            WriteEntry(zip, "github.svg", "<svg><script>not a path</script></svg>");
        malformed.Position = 0;

        Assert.True((await sut.ImportAsync(
            malformed,
            TestContext.Current.CancellationToken)).IsFailed);
        Assert.False(sut.Status.IsInstalled);
    }

    [Fact]
    public async Task ImportAsync_RejectsControlCharactersInImportedDisplayNames()
    {
        using var temp = new TempDir();
        var sut = CreateSut(temp.Path);
        await using var archive = new MemoryStream();
        using (var zip = new ZipArchive(archive, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(zip, "simple-icons-1.0.0/data/simple-icons.json", """
                [{"title":"Bad\u000AName","slug":"badname","hex":"334155"}]
                """);
            WriteEntry(zip, "simple-icons-1.0.0/icons/badname.svg", ValidSvg);
        }
        archive.Position = 0;

        var imported = await sut.ImportAsync(archive, TestContext.Current.CancellationToken);

        Assert.True(imported.IsFailed);
        Assert.False(sut.Status.IsInstalled);
    }

    [Fact]
    public async Task Resolve_ExactKnownAliasDoesNotFallBackToBroaderBrandWhenTargetIsMissing()
    {
        using var temp = new TempDir();
        var sut = CreateSut(temp.Path);
        await using var archive = CreateArchive();
        Assert.True((await sut.ImportAsync(archive, TestContext.Current.CancellationToken)).IsSuccess);

        Assert.Null(sut.Resolve("Azure AD"));
        Assert.Null(sut.Resolve("Azure AD tenant"));
        Assert.Equal("microsoftazure", sut.Resolve("Azure")?.Id);
    }

    [Fact]
    public async Task Resolve_DoesNotChooseAnImportedAliasSharedByDifferentBrands()
    {
        using var temp = new TempDir();
        var sut = CreateSut(temp.Path);
        await using var archive = CreateArchiveWithConflictingAlias();
        Assert.True((await sut.ImportAsync(archive, TestContext.Current.CancellationToken)).IsSuccess);

        Assert.Null(sut.Resolve("Shared Login"));
        Assert.Equal("alpha", sut.Resolve("Alpha")?.Id);
        Assert.Equal("beta", sut.Resolve("Beta")?.Id);
        Assert.Equal("x", sut.Resolve("X")?.Id);
        Assert.Null(sut.Resolve("X account"));
    }

    [Fact]
    public async Task ResolveAccount_UsesIssuerOnlyAndNeverUsesAccountName()
    {
        using var temp = new TempDir();
        var sut = CreateSut(temp.Path);
        await using var archive = CreateArchive();
        Assert.True((await sut.ImportAsync(archive, TestContext.Current.CancellationToken)).IsSuccess);

        Assert.Null(sut.ResolveAccount(string.Empty, "github test"));
        Assert.Null(sut.ResolveAccount("Unknown issuer", "github test"));
    }

    [Fact]
    public async Task ImportAsync_InstallsOtpHarborPackWithoutTruncatingAliasesAndPreservesProvenance()
    {
        using var temp = new TempDir();
        var sut = CreateSut(temp.Path);
        await using var archive = CreateOtpHarborArchive(aliasCount: 40);

        var imported = await sut.ImportAsync(
            archive,
            "otp-harbor-icons.otphicons",
            TestContext.Current.CancellationToken);

        Assert.True(imported.IsSuccess);
        Assert.Equal(BrandIconPackFormat.OtpHarbor, imported.Value.Format);
        Assert.Equal("otp-harbor-icons", imported.Value.ProviderId);
        Assert.Equal("c-plus-plus", sut.Resolve(" C++ ")?.Id);
        Assert.Equal("c-plus-plus", sut.Resolve("Service Alias 39")?.Id);
        Assert.Null(sut.Resolve("Service Alias 40"));
        Assert.Null(sut.ResolveAccount(string.Empty, "Service Alias 39"));

        var packDirectory = Assert.Single(Directory.EnumerateDirectories(
            Path.Combine(temp.Path, "BrandIcons", "packs")));
        var index = await File.ReadAllTextAsync(
            Path.Combine(packDirectory, "brand-index.json"),
            TestContext.Current.CancellationToken);
        Assert.Contains("ArchiveSha256", index, StringComparison.Ordinal);
        Assert.Contains("SelectedSource", index, StringComparison.Ordinal);
        Assert.Contains("Service Alias 39", index, StringComparison.Ordinal);
        Assert.Equal(
            "Synthetic license",
            await File.ReadAllTextAsync(
                Path.Combine(
                    packDirectory,
                    "provenance",
                    "licenses",
                    "simple-icons",
                    "license.md"),
                TestContext.Current.CancellationToken));

        var reloaded = CreateSut(temp.Path);
        Assert.Equal("c-plus-plus", reloaded.Resolve("C++")?.Id);
        Assert.Equal("c-plus-plus", reloaded.Resolve("Service Alias 39")?.Id);
        Assert.Equal(BrandIconPackFormat.OtpHarbor, reloaded.Status.Format);
    }

    [Fact]
    public async Task ImportAsync_InvalidOtpHarborReplacementLeavesInstalledPackUntouched()
    {
        using var temp = new TempDir();
        var sut = CreateSut(temp.Path);
        await using var valid = CreateOtpHarborArchive(aliasCount: 3);
        Assert.True((await sut.ImportAsync(
            valid,
            "otp-harbor-icons.otphicons",
            TestContext.Current.CancellationToken)).IsSuccess);
        await using var invalid = CreateOtpHarborArchive(aliasCount: 3, invalidAliasIndex: true);

        var rejected = await sut.ImportAsync(
            invalid,
            "otp-harbor-icons.otphicons",
            TestContext.Current.CancellationToken);

        Assert.True(rejected.IsFailed);
        Assert.Equal("c-plus-plus", sut.Resolve("Service Alias 02")?.Id);
        Assert.Equal("c-plus-plus", CreateSut(temp.Path).Resolve("Service Alias 02")?.Id);
        Assert.Single(Directory.EnumerateDirectories(
            Path.Combine(temp.Path, "BrandIcons", "packs")));
    }

    [Fact]
    public async Task Resolve_UsesExplicitBrandIdBeforeIssuer()
    {
        using var temp = new TempDir();
        var sut = CreateSut(temp.Path);
        await using var archive = CreateArchive();
        Assert.True((await sut.ImportAsync(archive, TestContext.Current.CancellationToken)).IsSuccess);

        Assert.Equal("amazon", sut.Resolve("GitHub", "amazon")?.Id);
    }

    [Fact]
    public async Task Resolver_UsesIssuerOnly_NotAnAccountEmailDomain()
    {
        using var temp = new TempDir();
        var sut = CreateSut(temp.Path);
        await using var archive = CreateArchive();
        Assert.True((await sut.ImportAsync(archive, TestContext.Current.CancellationToken)).IsSuccess);
        const string accountName = "somebody@gmail.com";

        var resolved = sut.Resolve("GitHub");

        Assert.Equal("github", resolved?.Id);
        Assert.DoesNotContain(accountName, resolved!.DisplayName, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TryGetIconPathData_LoadsInstalledSvgWithoutNetworkOrRepeatedParsing()
    {
        using var temp = new TempDir();
        var sut = CreateSut(temp.Path);
        await using var archive = CreateArchive();
        Assert.True((await sut.ImportAsync(archive, TestContext.Current.CancellationToken)).IsSuccess);

        Assert.True(sut.TryGetIconPathData("github", out var first));
        Assert.True(sut.TryGetIconPathData("github", out var second));
        Assert.Equal("M0 0h24v24H0z", first);
        Assert.Same(first, second);
    }

    [Fact]
    public async Task NewInstance_LoadsPreviouslyImportedPackWithoutChangingAccountPersistence()
    {
        using var temp = new TempDir();
        var first = CreateSut(temp.Path);
        await using var archive = CreateArchive();
        Assert.True((await first.ImportAsync(archive, TestContext.Current.CancellationToken)).IsSuccess);

        var reloaded = CreateSut(temp.Path);

        Assert.True(reloaded.Status.IsInstalled);
        Assert.Equal(8, reloaded.Status.BrandCount);
        Assert.Equal("github", reloaded.Resolve("GitHub")?.Id);
    }

    [Fact]
    public async Task NewInstance_LoadsLegacySinglePackPointerWhenProviderRegistryDoesNotExist()
    {
        using var temp = new TempDir();
        var first = CreateSut(temp.Path);
        await using var archive = CreateArchive();
        Assert.True((await first.ImportAsync(
            archive,
            TestContext.Current.CancellationToken)).IsSuccess);
        File.Delete(Path.Combine(
            temp.Path,
            "BrandIcons",
            "installed-packs.json"));

        var reloaded = CreateSut(temp.Path);

        Assert.True(reloaded.Status.IsInstalled);
        Assert.Single(reloaded.InstalledPacks);
        Assert.Equal("simple-icons", reloaded.InstalledPacks[0].ProviderId);
        Assert.Equal("github", reloaded.Resolve("GitHub")?.Id);
    }

    [Fact]
    public async Task ImportAsync_CombinesProvidersByPriorityAndReplacesOnlyMatchingProvider()
    {
        using var temp = new TempDir();
        var sut = CreateSut(temp.Path);
        await using var aegis = CreateAegisArchive(
            "first",
            "github",
            "homeassistant");
        await using var simpleIcons = CreateArchive();

        Assert.True((await sut.ImportAsync(
            aegis,
            "aegis-icons-first.zip",
            TestContext.Current.CancellationToken)).IsSuccess);
        Assert.True((await sut.ImportAsync(
            simpleIcons,
            "simple-icons-16.31.0.zip",
            TestContext.Current.CancellationToken)).IsSuccess);

        Assert.Equal(2, sut.InstalledPacks.Count);
        Assert.Equal(
            ["aegis", "simple-icons"],
            sut.InstalledPacks.Select(value => value.ProviderId).ToArray());
        Assert.Equal(9, sut.AvailableBrands.Count);
        Assert.Equal("github", sut.Resolve("GitHub")?.Id);
        Assert.Equal("#334155", sut.Resolve("GitHub")?.BackgroundColor);
        Assert.Equal("homeassistant", sut.Resolve("Home Assistant")?.Id);

        await using var replacement = CreateAegisArchive("second", "proxmox");
        Assert.True((await sut.ImportAsync(
            replacement,
            "aegis-icons-second.zip",
            TestContext.Current.CancellationToken)).IsSuccess);

        Assert.Equal(2, sut.InstalledPacks.Count);
        Assert.Null(sut.Resolve("Home Assistant"));
        Assert.Equal("proxmox", sut.Resolve("Proxmox")?.Id);
        Assert.Equal("#181717", sut.Resolve("GitHub")?.BackgroundColor);
        Assert.Equal(2, Directory.EnumerateDirectories(
            Path.Combine(temp.Path, "BrandIcons", "packs")).Count());

        var reloaded = CreateSut(temp.Path);
        Assert.Equal(2, reloaded.InstalledPacks.Count);
        Assert.Equal("github", reloaded.Resolve("GitHub")?.Id);
        Assert.Equal("proxmox", reloaded.Resolve("Proxmox")?.Id);
    }

    [Fact]
    public async Task ImportAsync_DeduplicatesMatchingProviderIdsEvenWhenDisplayNamesNormalizeDifferently()
    {
        using var temp = new TempDir();
        var sut = CreateSut(temp.Path);
        await using var aegis = CreateAegisArchive("first", "isc2");
        await using var simpleIcons = CreateSimpleIconsArchive(
            "ISC Squared",
            "isc2",
            "123456");

        Assert.True((await sut.ImportAsync(
            simpleIcons,
            "simple-icons.zip",
            TestContext.Current.CancellationToken)).IsSuccess);
        Assert.True((await sut.ImportAsync(
            aegis,
            "aegis-icons.zip",
            TestContext.Current.CancellationToken)).IsSuccess);

        Assert.Equal(2, sut.InstalledPacks.Count);
        Assert.Single(sut.AvailableBrands);
        Assert.Equal("isc2", sut.AvailableBrands[0].Id);
        Assert.Equal("#334155", sut.AvailableBrands[0].BackgroundColor);
    }

    [Fact(
        Skip = "Latest upstream archives are supplied only by the compatibility workflow.",
        SkipUnless = nameof(LatestCompatibilityArchivesAreAvailable))]
    [Trait("Category", "IconPackCompatibility")]
    public async Task ImportAsync_InstallsLatestAegisAndSimpleIconsTogetherInEitherOrder()
    {
        var aegisPath = Environment.GetEnvironmentVariable(AegisCompatibilityArchiveVariable)!;
        var simpleIconsPath = Environment.GetEnvironmentVariable(SimpleIconsCompatibilityArchiveVariable)!;

        await VerifyLatestProvidersCanCoexistAsync(simpleIconsPath, aegisPath);
        await VerifyLatestProvidersCanCoexistAsync(aegisPath, simpleIconsPath);
    }

    [Fact(
        Skip = "The latest OTP Harbor icon pack is supplied only by the compatibility workflow.",
        SkipUnless = nameof(LatestUnifiedCompatibilityArchiveIsAvailable))]
    [Trait("Category", "IconPackCompatibility")]
    public async Task ImportAsync_LatestUnifiedPackExposesViewportAndPaintForRegressionBrands()
    {
        using var temp = new TempDir();
        var sut = CreateSut(temp.Path);
        var path = Environment.GetEnvironmentVariable(UnifiedCompatibilityArchiveVariable)!;
        await using var archive = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);

        var imported = await sut.ImportAsync(
            archive,
            Path.GetFileName(path),
            TestContext.Current.CancellationToken);

        Assert.True(imported.IsSuccess);
        foreach (var brandId in new[]
                 {
                     "bitdefender", "bitrise", "bitwarden", "bluesky", "diners-club",
                     "ethereum", "firebase", "garmin", "goodreads", "google-assistant"
                 })
        {
            Assert.Contains(sut.AvailableBrands, brand => brand.Id == brandId);
            Assert.True(sut.TryGetIconLayers(brandId, out var layers), brandId);
            Assert.NotEmpty(layers);
            Assert.All(layers, layer => Assert.NotNull(layer.Viewport));
            Assert.Contains(layers, layer => layer.FillColor is not null);
        }
    }

    [Fact(
        Skip = "The latest OTP Harbor icon pack is supplied only by the compatibility workflow.",
        SkipUnless = nameof(LatestUnifiedCompatibilityArchiveIsAvailable))]
    [Trait("Category", "IconPackCompatibility")]
    public async Task ImportAsync_LatestUnifiedPackHasNoUnrenderableBrands()
    {
        using var temp = new TempDir();
        var sut = CreateSut(temp.Path);
        var path = Environment.GetEnvironmentVariable(UnifiedCompatibilityArchiveVariable)!;
        await using var archive = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);

        var imported = await sut.ImportAsync(
            archive,
            Path.GetFileName(path),
            TestContext.Current.CancellationToken);

        Assert.True(imported.IsSuccess);
        var unreadableBrands = sut.AvailableBrands
            .Where(brand => !sut.TryGetIconLayers(brand.Id, out var layers) || layers.Count == 0)
            .Select(brand => brand.Id)
            .ToArray();
        Assert.True(
            unreadableBrands.Length == 0,
            $"Brands without renderable path layers: {string.Join(", ", unreadableBrands.Take(50))}");
    }

    [Fact]
    public async Task ImportAsync_RejectsPathTraversalAndPreservesEmptyCatalog()
    {
        using var temp = new TempDir();
        var sut = CreateSut(temp.Path);
        await using var archive = new MemoryStream();
        using (var zip = new ZipArchive(archive, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(zip, "../data/simple-icons.json", "[]");
        }
        archive.Position = 0;

        var result = await sut.ImportAsync(archive, TestContext.Current.CancellationToken);

        Assert.True(result.IsFailed);
        Assert.False(sut.Status.IsInstalled);
    }

    [Fact]
    public async Task ImportAsync_RejectsAegisManifestTraversalWithoutOverwritingApplicationFiles()
    {
        using var temp = new TempDir();
        var sut = CreateSut(temp.Path);
        var vaultPath = Path.Combine(temp.Path, "vault.json");
        const string originalVault = "ORIGINAL-VAULT-CONTENTS";
        await File.WriteAllTextAsync(
            vaultPath,
            originalVault,
            TestContext.Current.CancellationToken);
        await using var archive = new MemoryStream();
        using (var zip = new ZipArchive(archive, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(zip, "pack.json", """
                {
                  "uuid":"11111111-1111-1111-1111-111111111111",
                  "name":"Malicious traversal pack",
                  "version":1,
                  "icons":[
                    {
                      "name":"Traversal",
                      "filename":"../../../vault.json",
                      "issuer":["Traversal"]
                    }
                  ]
                }
                """);
            WriteEntry(zip, "../../../vault.json", "MALICIOUS-VAULT-CONTENTS");
        }
        archive.Position = 0;

        var result = await sut.ImportAsync(archive, TestContext.Current.CancellationToken);

        Assert.True(result.IsFailed);
        Assert.Equal(
            originalVault,
            await File.ReadAllTextAsync(vaultPath, TestContext.Current.CancellationToken));
        Assert.False(sut.Status.IsInstalled);
    }

    [Fact]
    public async Task ResetAsync_RemovesImportedPackAndRaisesCatalogChanged()
    {
        using var temp = new TempDir();
        var sut = CreateSut(temp.Path);
        await using var archive = CreateArchive();
        Assert.True((await sut.ImportAsync(archive, TestContext.Current.CancellationToken)).IsSuccess);
        var changed = 0;
        sut.CatalogChanged += (_, _) => changed++;

        var result = await sut.ResetAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.False(sut.Status.IsInstalled);
        Assert.Null(sut.Resolve("GitHub"));
        Assert.True(changed > 0);
        Assert.Empty(Directory.EnumerateDirectories(
            Path.Combine(temp.Path, "BrandIcons", "packs")));
    }

    [Fact]
    public async Task ShowIssuerLogo_DefaultsToTrueAndPersistsOutsideApplicationPreferences()
    {
        using var temp = new TempDir();
        var sut = CreateSut(temp.Path);
        var changed = 0;
        sut.CatalogChanged += (_, _) => changed++;

        var saved = await sut.SetShowIssuerLogoAsync(
            false,
            TestContext.Current.CancellationToken);
        var reloaded = CreateSut(temp.Path);

        Assert.True(saved.IsSuccess);
        Assert.False(sut.ShowIssuerLogo);
        Assert.False(reloaded.ShowIssuerLogo);
        Assert.Equal(1, changed);
        Assert.True(File.Exists(Path.Combine(
            temp.Path,
            "BrandIcons",
            "display-settings.json")));
        Assert.False(File.Exists(Path.Combine(temp.Path, "preferences.json")));
    }

    [Fact]
    public async Task AccountBrandPreference_PersistsLocallyWithoutAccountMetadata()
    {
        using var temp = new TempDir();
        var accountId = Guid.NewGuid();
        var sut = CreateSut(temp.Path);
        await using var archive = CreateFilenameIndexedArchive();
        Assert.True((await sut.ImportAsync(
            archive,
            TestContext.Current.CancellationToken)).IsSuccess);

        Assert.Equal(
            ["github", "google", "microsoft"],
            sut.AvailableBrands.Select(brand => brand.Id).ToArray());
        Assert.True((await sut.SetAccountBrandIdAsync(
            accountId,
            "GitHub",
            TestContext.Current.CancellationToken)).IsSuccess);
        Assert.Equal("github", sut.GetAccountBrandId(accountId));

        var settingsPath = Path.Combine(
            temp.Path,
            "BrandIcons",
            "account-brand-settings.json");
        var settings = File.ReadAllText(settingsPath);
        Assert.Contains(accountId.ToString(), settings, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("github", settings, StringComparison.Ordinal);
        Assert.DoesNotContain("issuer", settings, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", settings, StringComparison.OrdinalIgnoreCase);

        var reloaded = CreateSut(temp.Path);
        Assert.Equal("github", reloaded.GetAccountBrandId(accountId));
        Assert.True((await reloaded.SetAccountBrandIdAsync(
            accountId,
            null,
            TestContext.Current.CancellationToken)).IsSuccess);
        Assert.Null(reloaded.GetAccountBrandId(accountId));
    }

    [Fact]
    public async Task AccountBrandPreference_RejectsUnavailableBrandWithoutChangingMapping()
    {
        using var temp = new TempDir();
        var accountId = Guid.NewGuid();
        var sut = CreateSut(temp.Path);
        await using var archive = CreateFilenameIndexedArchive();
        Assert.True((await sut.ImportAsync(
            archive,
            TestContext.Current.CancellationToken)).IsSuccess);
        Assert.True((await sut.SetAccountBrandIdAsync(
            accountId,
            "github",
            TestContext.Current.CancellationToken)).IsSuccess);

        var result = await sut.SetAccountBrandIdAsync(
            accountId,
            "not-installed",
            TestContext.Current.CancellationToken);

        Assert.True(result.IsFailed);
        Assert.Equal("github", sut.GetAccountBrandId(accountId));
    }

    [Fact]
    public async Task ResetAsync_PreservesIssuerLogoVisibilityPreference()
    {
        using var temp = new TempDir();
        var sut = CreateSut(temp.Path);
        await using var archive = CreateArchive();
        Assert.True((await sut.ImportAsync(archive, TestContext.Current.CancellationToken)).IsSuccess);
        Assert.True((await sut.SetShowIssuerLogoAsync(false, TestContext.Current.CancellationToken)).IsSuccess);

        var reset = await sut.ResetAsync(TestContext.Current.CancellationToken);

        Assert.True(reset.IsSuccess);
        Assert.False(sut.ShowIssuerLogo);
        Assert.False(CreateSut(temp.Path).ShowIssuerLogo);
    }

    [Fact]
    public async Task ImportCustomIconAsync_PersistsPerAccountOverrideWithoutReplacingPack()
    {
        using var temp = new TempDir();
        var sut = CreateSut(temp.Path);
        await using var archive = CreateArchive();
        Assert.True((await sut.ImportAsync(
            archive,
            TestContext.Current.CancellationToken)).IsSuccess);
        var accountId = Guid.NewGuid();
        await using var svg = new MemoryStream(Encoding.UTF8.GetBytes(ValidSvg));

        var imported = await sut.ImportCustomIconAsync(
            accountId,
            svg,
            "personal-mark.svg",
            TestContext.Current.CancellationToken);
        var reloaded = CreateSut(temp.Path);

        Assert.True(imported.IsSuccess);
        Assert.StartsWith("custom_", imported.Value.Id, StringComparison.Ordinal);
        Assert.Equal("personal-mark.svg", imported.Value.SourceFileName);
        Assert.Equal(imported.Value.Id, reloaded.GetAccountBrandId(accountId));
        Assert.Equal(imported.Value.Id, reloaded.Resolve(null, imported.Value.Id)?.Id);
        Assert.True(reloaded.TryGetIconPathData(imported.Value.Id, out var pathData));
        Assert.Equal("M0 0h24v24H0z", pathData);
        Assert.True(reloaded.Status.IsInstalled);
        Assert.Equal("github", reloaded.Resolve("github.com")?.Id);
        Assert.DoesNotContain(
            reloaded.AvailableBrands,
            brand => brand.Id == imported.Value.Id);
    }

    [Fact]
    public async Task ImportCustomIconAsync_PreservesAllExplicitlyColoredSvgPaths()
    {
        using var temp = new TempDir();
        var sut = CreateSut(temp.Path);
        var accountId = Guid.NewGuid();
        await using var svg = new MemoryStream(Encoding.UTF8.GetBytes("""
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 23 23">
              <path fill="#f35325" d="M1 1h10v10H1z"/>
              <path fill="#81bc06" d="M12 1h10v10H12z"/>
              <path fill="#05a6f0" d="M1 12h10v10H1z"/>
              <path fill="#ffba08" d="M12 12h10v10H12z"/>
            </svg>
            """));

        var imported = await sut.ImportCustomIconAsync(
            accountId,
            svg,
            "microsoft.svg",
            TestContext.Current.CancellationToken);

        Assert.True(imported.IsSuccess);
        Assert.True(sut.TryGetIconLayers(imported.Value.Id, out var layers));
        Assert.Collection(
            layers,
            layer => Assert.Equal("#F35325", layer.FillColor),
            layer => Assert.Equal("#81BC06", layer.FillColor),
            layer => Assert.Equal("#05A6F0", layer.FillColor),
            layer => Assert.Equal("#FFBA08", layer.FillColor));
        Assert.Equal("M1 1h10v10H1z", layers[0].PathData);
    }

    [Fact]
    public async Task ImportCustomIconAsync_PreservesViewportInlineStylesAndInheritedPaint()
    {
        using var temp = new TempDir();
        var sut = CreateSut(temp.Path);
        var accountId = Guid.NewGuid();
        await using var svg = new MemoryStream(Encoding.UTF8.GetBytes("""
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="180.06 83.35 419.87 333.3" fill="#0079be">
              <g transform="translate(10 20) rotate(15)">
                <path style="fill: #fff" d="M200 100h100v100H200z"/>
              </g>
              <path d="M300 200h100v100H300z"/>
              <path style="fill:none" d="M0 0h1v1H0z"/>
            </svg>
            """));

        var imported = await sut.ImportCustomIconAsync(
            accountId,
            svg,
            "viewport-and-paint.svg",
            TestContext.Current.CancellationToken);

        Assert.True(imported.IsSuccess);
        Assert.True(sut.TryGetIconLayers(imported.Value.Id, out var layers));
        Assert.Collection(
            layers,
            layer =>
            {
                Assert.Equal("#FFFFFF", layer.FillColor);
                Assert.NotNull(layer.Transform);
                Assert.Equal(new BrandIconViewport(180.06, 83.35, 419.87, 333.3), layer.Viewport);
            },
            layer =>
            {
                Assert.Equal("#0079BE", layer.FillColor);
                Assert.Null(layer.Transform);
                Assert.Equal(new BrandIconViewport(180.06, 83.35, 419.87, 333.3), layer.Viewport);
            });
    }

    [Fact]
    public async Task ImportCustomIconAsync_UsesNumericDimensionsWhenViewBoxIsMissing()
    {
        using var temp = new TempDir();
        var sut = CreateSut(temp.Path);
        await using var svg = new MemoryStream(Encoding.UTF8.GetBytes("""
            <svg xmlns="http://www.w3.org/2000/svg" width="256" height="417">
              <path fill="#343434" d="M128 0 0 212l128 76z"/>
            </svg>
            """));

        var imported = await sut.ImportCustomIconAsync(
            Guid.NewGuid(),
            svg,
            "dimensions.svg",
            TestContext.Current.CancellationToken);

        Assert.True(imported.IsSuccess);
        Assert.True(sut.TryGetIconLayers(imported.Value.Id, out var layers));
        Assert.Equal(new BrandIconViewport(0, 0, 256, 417), Assert.Single(layers).Viewport);
    }

    [Fact]
    public async Task ImportCustomIconAsync_PreservesInheritedSolidStrokeWithoutAnArbitraryPathCountCap()
    {
        using var temp = new TempDir();
        var sut = CreateSut(temp.Path);
        var paths = string.Concat(Enumerable.Range(0, 333).Select(index =>
            $"<path d=\"M{index} 0v10\"/>"));
        await using var svg = new MemoryStream(Encoding.UTF8.GetBytes(
            $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 333 10\" fill=\"none\" stroke=\"#3b82f6\" stroke-width=\"2\">{paths}</svg>"));

        var imported = await sut.ImportCustomIconAsync(
            Guid.NewGuid(),
            svg,
            "stroke-only.svg",
            TestContext.Current.CancellationToken);

        Assert.True(imported.IsSuccess);
        Assert.True(sut.TryGetIconLayers(imported.Value.Id, out var layers));
        Assert.Equal(333, layers.Count);
        Assert.All(layers, layer =>
        {
            Assert.Equal("none", layer.FillColor);
            Assert.Equal(new BrandIconStroke("#3B82F6", 2), layer.Stroke);
        });
    }

    [Fact]
    public async Task ImportAsync_InstallsAegisSvgPackAndRestoresProviderMetadata()
    {
        using var temp = new TempDir();
        var sut = CreateSut(temp.Path);
        await using var archive = new MemoryStream();
        using (var zip = new ZipArchive(archive, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(zip, "pack.json", """
                {
                  "uuid":"c553f06f-2a17-46ca-87f5-56af90dd0500",
                  "name":"Synthetic Aegis Pack",
                  "version":7,
                  "icons":[
                    {"name":"GitHub","filename":"icons/GitHub.svg","issuer":["github.com"]}
                  ],
                  "futureOptionalField":true
                }
                """);
            WriteEntry(zip, "icons/GitHub.svg", ValidSvg);
        }
        archive.Position = 0;

        var imported = await sut.ImportAsync(archive, TestContext.Current.CancellationToken);
        var reloaded = CreateSut(temp.Path);

        Assert.True(imported.IsSuccess);
        Assert.Equal(BrandIconPackFormat.Aegis, imported.Value.Format);
        Assert.Equal("Synthetic Aegis Pack", imported.Value.ProviderDisplayName);
        Assert.Equal("github", reloaded.Resolve("github.com")?.Id);
        Assert.Equal(BrandIconPackFormat.Aegis, reloaded.Status.Format);
        Assert.Equal("Synthetic Aegis Pack", reloaded.Status.ProviderDisplayName);
    }

    [Fact]
    public async Task ImportAsync_AegisBookingEntryResolvesAliasAndLoadsGeometry()
    {
        using var temp = new TempDir();
        var sut = CreateSut(temp.Path);
        await using var archive = new MemoryStream();
        using (var zip = new ZipArchive(archive, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(zip, "pack.json", """
                {
                  "uuid":"c553f06f-2a17-46ca-87f5-56af90dd0500",
                  "name":"Aegis Simple Icons",
                  "version":264,
                  "icons":[
                    {
                      "name":"Booking.com",
                      "filename":"SVG/bookingdotcom.svg",
                      "category":null,
                      "issuer":["Booking.com"]
                    }
                  ]
                }
                """);
            WriteEntry(zip, "svg/bookingdotcom.svg", """
                <svg role="img" viewBox="0 0 24 24" xmlns="http://www.w3.org/2000/svg">
                  <title>Booking.com</title>
                  <circle cx="12" cy="12" r="12" fill="#003A9A"></circle>
                  <path d="M24 0H0v24h24ZM8.575 6.563h2.658c2.108 0 3.473 1.15 3.473 2.898z" transform="translate(4.8, 4.8) scale(0.6)" fill="white"></path>
                </svg>
                """);
        }
        archive.Position = 0;

        var imported = await sut.ImportAsync(
            archive,
            "aegis-simple-icons-v264.zip",
            TestContext.Current.CancellationToken);
        var definition = sut.Resolve("booking.com");

        Assert.True(imported.IsSuccess);
        Assert.Equal("Aegis Simple Icons", imported.Value.ProviderDisplayName);
        Assert.Equal("bookingdotcom", definition?.Id);
        Assert.Equal("#003A9A", definition?.BackgroundColor);
        Assert.True(sut.TryGetIconPathData("bookingdotcom", out var pathData));
        Assert.StartsWith("M24 0H0v24h24Z", pathData, StringComparison.Ordinal);
        Assert.True(sut.TryGetIconTransform("bookingdotcom", out var transform));
        Assert.Equal(0.6, transform?.M11);
        Assert.Equal(0.6, transform?.M22);
        Assert.Equal(4.8, transform?.M31);
        Assert.Equal(4.8, transform?.M32);
    }

    [Fact]
    public void Constructor_LoadsLegacyFilenameIndexedCatalogWithoutNewProviderFields()
    {
        using var temp = new TempDir();
        var packId = "local-icons-filename-indexed-v3-aaaaaaaaaaaa";
        var packDirectory = Path.Combine(temp.Path, "BrandIcons", "packs", packId);
        Directory.CreateDirectory(Path.Combine(packDirectory, "icons"));
        File.WriteAllText(Path.Combine(temp.Path, "BrandIcons", "current.json"),
            $$"""{"PackId":"{{packId}}"}""");
        File.WriteAllText(Path.Combine(packDirectory, "brand-index.json"), """
            {
              "Version":"filename-indexed",
              "Brands":[
                {
                  "Id":"github",
                  "DisplayName":"GitHub",
                  "BackgroundColor":"#334155",
                  "IconFileName":"github.svg",
                  "Aliases":["github.com"]
                }
              ]
            }
            """);
        File.WriteAllText(Path.Combine(packDirectory, "icons", "github.svg"), ValidSvg);

        var sut = CreateSut(temp.Path);

        Assert.True(sut.Status.IsInstalled);
        Assert.Equal(BrandIconPackFormat.FilenameIndexed, sut.Status.Format);
        Assert.Equal("github", sut.Resolve("github.com")?.Id);
    }

    private static SimpleIconsBrandIconPackService CreateSut(
        string applicationDataDirectory,
        ILogger<SimpleIconsBrandIconPackService>? logger = null)
    {
        var paths = new Mock<IPlatformApplicationPaths>();
        paths.SetupGet(value => value.ApplicationDataDirectory).Returns(applicationDataDirectory);
        return new SimpleIconsBrandIconPackService(
            paths.Object,
            NoOpPlatformFileSecurity.Instance,
            logger ?? NullLogger<SimpleIconsBrandIconPackService>.Instance);
    }

    private static async Task VerifyLatestProvidersCanCoexistAsync(
        string firstArchivePath,
        string secondArchivePath)
    {
        using var temp = new TempDir();
        var logger = new CapturingLogger<SimpleIconsBrandIconPackService>();
        var sut = CreateSut(temp.Path, logger);
        await using var first = File.OpenRead(firstArchivePath);
        var firstResult = await sut.ImportAsync(
            first,
            Path.GetFileName(firstArchivePath),
            TestContext.Current.CancellationToken);
        Assert.True(firstResult.IsSuccess, string.Join("; ", firstResult.Errors.Select(error => error.Message)));

        await using var second = File.OpenRead(secondArchivePath);
        var secondResult = await sut.ImportAsync(
            second,
            Path.GetFileName(secondArchivePath),
            TestContext.Current.CancellationToken);
        Assert.True(
            secondResult.IsSuccess,
            string.Join("; ", secondResult.Errors.Select(error => error.Message))
            + Environment.NewLine
            + logger.LastException);
        Assert.Equal(2, sut.InstalledPacks.Count);
        Assert.Equal(
            ["aegis", "simple-icons"],
            sut.InstalledPacks.Select(value => value.ProviderId).ToArray());
    }

    private static MemoryStream CreateArchive(bool includeMicrosoftEntra = false)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            const string root = "simple-icons-16.31.0/";
            var metadata = """
                [
                  {"title":"Amazon","slug":"amazon","hex":"FF9900","source":"https://example.invalid"},
                  {"title":"Amazon Web Services","slug":"amazonwebservices","hex":"232F3E","source":"https://example.invalid"},
                  {"title":"GitHub","hex":"181717","source":"https://example.invalid"},
                  {"title":"GitLab","slug":"gitlab","hex":"FC6D26","source":"https://example.invalid"},
                  {"title":"Google","slug":"google","hex":"4285F4","source":"https://example.invalid"},
                  {"title":"Microsoft","slug":"microsoft","hex":"5E5E5E","source":"https://example.invalid"},
                  {"title":"Slack","slug":"slack","hex":"4A154B","source":"https://example.invalid"},
                  {"title":"Microsoft Azure","slug":"microsoftazure","hex":"0078D4","source":"https://example.invalid"}
                ]
                """;
            if (includeMicrosoftEntra)
                metadata = metadata.Replace(
                    "\n]",
                    ",\n  {\"title\":\"Microsoft Entra\",\"slug\":\"microsoftentra\",\"hex\":\"0078D4\",\"source\":\"https://example.invalid\"}\n]");
            WriteEntry(archive, root + "data/simple-icons.json", metadata);
            var slugs = new List<string> { "amazon", "amazonwebservices", "github", "gitlab", "google", "microsoft", "slack", "microsoftazure" };
            if (includeMicrosoftEntra) slugs.Add("microsoftentra");
            foreach (var slug in slugs)
                WriteEntry(archive, root + $"icons/{slug}.svg", "<svg viewBox=\"0 0 24 24\"><path d=\"M0 0h24v24H0z\"/></svg>");
            WriteEntry(archive, root + "LICENSE.md", "Synthetic fixture license");
            WriteEntry(archive, root + "DISCLAIMER.md", "Synthetic fixture disclaimer");
        }
        stream.Position = 0;
        return stream;
    }

    private static MemoryStream CreateArchiveWithConflictingAlias()
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            const string root = "simple-icons-16.31.0/";
            WriteEntry(archive, root + "data/simple-icons.json", """
                [
                  {"title":"Alpha","slug":"alpha","hex":"111111","source":"https://example.invalid","aliases":{"aka":["Shared Login"]}},
                  {"title":"Beta","slug":"beta","hex":"222222","source":"https://example.invalid","aliases":{"aka":["Shared Login"]}},
                  {"title":"X","slug":"x","hex":"333333","source":"https://example.invalid"}
                ]
                """);
            foreach (var slug in new[] { "alpha", "beta", "x" })
                WriteEntry(archive, root + $"icons/{slug}.svg", "<svg viewBox=\"0 0 24 24\"><path d=\"M0 0h24v24H0z\"/></svg>");
        }
        stream.Position = 0;
        return stream;
    }

    private static MemoryStream CreateSimpleIconsArchive(
        string title,
        string slug,
        string hex)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, "package/package.json", "{\"version\":\"17.0.0\"}");
            WriteEntry(
                archive,
                "package/data/simple-icons.json",
                $"[{{\"title\":\"{title}\",\"slug\":\"{slug}\",\"hex\":\"{hex}\"}}]");
            WriteEntry(archive, $"package/icons/{slug}.svg", ValidSvg);
        }
        stream.Position = 0;
        return stream;
    }

    private static MemoryStream CreateOtpHarborArchive(
        int aliasCount,
        bool invalidAliasIndex = false)
    {
        var aliases = Enumerable.Range(0, aliasCount)
            .Select(index => $"Service Alias {index:D2}")
            .Prepend("C++")
            .ToArray();
        var aliasIndex = aliases.Select(alias => new
        {
            key = alias == "C++"
                ? "c++"
                : alias.ToLowerInvariant(),
            brandId = invalidAliasIndex && alias == aliases[^1]
                ? "missing-brand"
                : "c-plus-plus"
        }).ToArray();
        var manifest = System.Text.Json.JsonSerializer.Serialize(new
        {
            formatVersion = 1,
            packId = "otp-harbor-icons",
            name = "OTP Harbor Icons",
            sources = new[]
            {
                new
                {
                    provider = "simple-icons",
                    inputFileName = "source.zip",
                    sha256 = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
                    version = "16.34.0",
                    revision = (string?)null,
                    sourceUrl = "https://github.com/simple-icons/simple-icons/releases/tag/16.34.0",
                    metadata = new Dictionary<string, string?> { ["version"] = "16.34.0" },
                    licenseFiles = new[] { "licenses/simple-icons/license.md" }
                }
            },
            brands = new[]
            {
                new
                {
                    id = "c-plus-plus",
                    displayName = "C++",
                    backgroundColor = "#00599C",
                    icon = "icons/c-plus-plus.svg",
                    issuerAliases = aliases,
                    selectedSource = new
                    {
                        provider = "simple-icons",
                        sourceId = "cplusplus",
                        metadata = new Dictionary<string, string?> { ["nativeBrandColor"] = "#00599C" }
                    },
                    sources = new[]
                    {
                        new
                        {
                            provider = "simple-icons",
                            sourceId = "cplusplus",
                            metadata = new Dictionary<string, string?> { ["nativeBrandColor"] = "#00599C" }
                        }
                    }
                }
            },
            issuerAliases = aliasIndex
        });
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, "pack.json", manifest);
            WriteEntry(archive, "icons/c-plus-plus.svg", ValidSvg);
            WriteEntry(archive, "licenses/simple-icons/license.md", "Synthetic license");
        }
        stream.Position = 0;
        return stream;
    }

    private static MemoryStream CreateFilenameIndexedArchive()
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, "pack/icons/microsoft.svg", ValidSvg);
            WriteEntry(archive, "pack/icons/GitHub.svg", ValidSvg);
            WriteEntry(archive, "pack/google.svg", ValidSvg);
            WriteEntry(archive, "pack/LICENSE.md", "Synthetic generic license");
            WriteEntry(archive, "pack/legal/NOTICE.txt", "Synthetic generic notice");
        }
        stream.Position = 0;
        return stream;
    }

    private static MemoryStream CreateAegisArchive(
        string version,
        params string[] slugs)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var icons = string.Join(
                ',',
                slugs.Select(slug =>
                {
                    var name = slug switch
                    {
                        "github" => "GitHub",
                        "homeassistant" => "Home Assistant",
                        "proxmox" => "Proxmox",
                        _ => slug
                    };
                    return $"{{\"name\":\"{name}\",\"filename\":\"icons/{slug}.svg\",\"issuer\":[\"{name}\"]}}";
                }));
            WriteEntry(
                archive,
                "pack.json",
                $"{{\"uuid\":\"c553f06f-2a17-46ca-87f5-56af90dd0500\",\"name\":\"Synthetic Aegis Pack {version}\",\"version\":1,\"icons\":[{icons}]}}");
            foreach (var slug in slugs)
                WriteEntry(archive, $"icons/{slug}.svg", ValidSvg);
            WriteEntry(archive, "LICENSE", "Synthetic Aegis icon-pack license");
        }
        stream.Position = 0;
        return stream;
    }

    private static void WriteEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Fastest);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    private const string ValidSvg =
        "<svg viewBox=\"0 0 24 24\"><path d=\"M0 0h24v24H0z\"/></svg>";

    private sealed class TempDir : IDisposable
    {
        public TempDir()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "otp-harbor-brand-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch { }
        }
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public Exception? LastException { get; private set; }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (exception is not null) LastException = exception;
        }
    }
}
