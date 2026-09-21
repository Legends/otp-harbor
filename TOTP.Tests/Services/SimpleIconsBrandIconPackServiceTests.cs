using System.IO.Compression;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TOTP.Core.Services.Interfaces;
using TOTP.Core.Services.Models;
using TOTP.Infrastructure.Services;
using TOTP.Tests.Common;

namespace TOTP.Tests.Services;

public sealed class SimpleIconsBrandIconPackServiceTests
{
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

        var imported = await sut.ImportAsync(archive, TestContext.Current.CancellationToken);

        Assert.True(imported.IsSuccess);
        Assert.Equal("filename-indexed", imported.Value.Version);
        Assert.Equal(3, imported.Value.BrandCount);
        Assert.Equal(BrandIconPackFormat.FilenameIndexed, imported.Value.Format);
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
    public async Task ResolveAccount_UsesLabelOnlyWhenIssuerIsMissing()
    {
        using var temp = new TempDir();
        var sut = CreateSut(temp.Path);
        await using var archive = CreateArchive();
        Assert.True((await sut.ImportAsync(archive, TestContext.Current.CancellationToken)).IsSuccess);

        Assert.Equal("github", sut.ResolveAccount(string.Empty, "github test")?.Id);
        Assert.Null(sut.ResolveAccount("Unknown issuer", "github test"));
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

    private static SimpleIconsBrandIconPackService CreateSut(string applicationDataDirectory)
    {
        var paths = new Mock<IPlatformApplicationPaths>();
        paths.SetupGet(value => value.ApplicationDataDirectory).Returns(applicationDataDirectory);
        return new SimpleIconsBrandIconPackService(
            paths.Object,
            NoOpPlatformFileSecurity.Instance,
            NullLogger<SimpleIconsBrandIconPackService>.Instance);
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
}
