using System.IO.Compression;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TOTP.Core.Services.Interfaces;
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
        await using var archive = CreateArchive();

        var imported = await sut.ImportAsync(archive, TestContext.Current.CancellationToken);

        Assert.True(imported.IsSuccess);
        Assert.Equal("16.31.0", imported.Value.Version);
        Assert.Equal(8, imported.Value.BrandCount);
        Assert.Equal("github", sut.Resolve("GitHub")?.Id);
        Assert.Equal("github", sut.Resolve("github")?.Id);
        Assert.Equal("github", sut.Resolve("github.com")?.Id);
        Assert.Equal("github", sut.Resolve("github test")?.Id);
        Assert.Equal("gitlab", sut.Resolve("gitlab test")?.Id);
        Assert.Equal("microsoft", sut.Resolve("Microsoft Account")?.Id);
        Assert.Equal("microsoft", sut.Resolve("  MICROSOFT   ACCOUNT ")?.Id);
        Assert.Equal("microsoft", sut.Resolve("Microsoft test")?.Id);
        Assert.Equal("slack", sut.Resolve("Slack test")?.Id);
        Assert.Equal("amazonwebservices", sut.Resolve("AWS")?.Id);
        Assert.Equal("amazonwebservices", sut.Resolve("AWS test")?.Id);
        Assert.Equal("amazonwebservices", sut.Resolve("Amazon Web Services")?.Id);
        Assert.Equal("amazon", sut.Resolve("Amazon")?.Id);
        Assert.Equal("amazon", sut.Resolve("Amazon test")?.Id);
        Assert.Equal("microsoftazure", sut.Resolve("Azure test")?.Id);
        Assert.Null(sut.Resolve("My Private Server"));
        Assert.Null(sut.Resolve(null));
        Assert.Null(sut.Resolve("  "));
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

    private static MemoryStream CreateArchive()
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            const string root = "simple-icons-16.31.0/";
            WriteEntry(archive, root + "data/simple-icons.json", """
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
                """);
            foreach (var slug in new[] { "amazon", "amazonwebservices", "github", "gitlab", "google", "microsoft", "slack", "microsoftazure" })
                WriteEntry(archive, root + $"icons/{slug}.svg", "<svg viewBox=\"0 0 24 24\"><path d=\"M0 0h24v24H0z\"/></svg>");
            WriteEntry(archive, root + "LICENSE.md", "Synthetic fixture license");
            WriteEntry(archive, root + "DISCLAIMER.md", "Synthetic fixture disclaimer");
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
