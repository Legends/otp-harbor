using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NSec.Cryptography;
using TOTP.Core.Services.Interfaces;
using TOTP.Core.Services.Models;
using TOTP.Infrastructure.Services;
using TOTP.Tests.Common;

namespace TOTP.Tests.Infrastructure.Services;

public sealed class PortableUpdateServiceTests
{
    [Fact]
    public async Task CheckAndDownloadAsync_VerifiesAppcastAndPackageBeforeReadyState()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var temp = new TempDir();
        var fixture = CreateSignedFixture("verified package"u8.ToArray());
        using var client = new HttpClient(new FixtureHandler(fixture));
        var paths = new Mock<IPlatformApplicationPaths>();
        paths.SetupGet(value => value.ApplicationDataDirectory).Returns(temp.Path);
        var sut = CreateSut(fixture.PublicKey, client, paths.Object);

        var check = await sut.CheckAsync(cancellationToken);
        Assert.True(check.IsSuccess);
        Assert.NotNull(check.Value.Offer);
        Assert.Equal("Security fixes.", check.Value.Offer.ReleaseNotes);

        var download = await sut.DownloadAsync(
            check.Value.Offer,
            cancellationToken: cancellationToken);

        Assert.True(download.IsSuccess);
        Assert.True(File.Exists(download.Value.FilePath));
        Assert.Equal(fixture.PackageBytes, await File.ReadAllBytesAsync(
            download.Value.FilePath,
            cancellationToken));
        Assert.EndsWith(".ready.zip", download.Value.FilePath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DownloadAsync_WhenPackageWasTampered_RejectsAndDeletesPartialFile()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var temp = new TempDir();
        var signed = CreateSignedFixture("signed package"u8.ToArray());
        var tampered = signed with { PackageBytes = "tampered package"u8.ToArray() };
        using var client = new HttpClient(new FixtureHandler(tampered));
        var paths = new Mock<IPlatformApplicationPaths>();
        paths.SetupGet(value => value.ApplicationDataDirectory).Returns(temp.Path);
        var sut = CreateSut(signed.PublicKey, client, paths.Object);
        var check = await sut.CheckAsync(cancellationToken);
        Assert.True(check.IsSuccess);

        var download = await sut.DownloadAsync(
            check.Value.Offer!,
            cancellationToken: cancellationToken);

        Assert.True(download.IsFailed);
        var updateDirectory = Path.Combine(temp.Path, "Updates");
        Assert.Empty(Directory.Exists(updateDirectory)
            ? Directory.GetFiles(updateDirectory)
            : []);
    }

    [Fact]
    public async Task CheckAsync_WhenFeedUsesHttp_FailsBeforeNetworkAccess()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AutoUpdate:Enabled"] = "true",
            ["AutoUpdate:AppcastUrl"] = "http://example.invalid/appcast.xml",
            ["AutoUpdate:PublicKey"] = Convert.ToBase64String(new byte[32])
        }).Build();
        using var client = new HttpClient(new ThrowingHandler());
        var sut = new PortableUpdateService(
            configuration,
            VersionProvider(),
            client,
            new SignedAppcastVerifier(),
            new SignedPayloadVerifier(),
            Mock.Of<IPlatformApplicationPaths>(),
            NoOpPlatformFileSecurity.Instance,
            NullLogger<PortableUpdateService>.Instance);

        var result = await sut.CheckAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsFailed);
        Assert.Equal(
            PortableUpdateErrorCode.ConfigurationInvalid,
            Assert.IsType<PortableUpdateError>(Assert.Single(result.Errors)).Code);
    }

    [Fact]
    public async Task CheckAsync_WhenFeedIsMissing_ReportsUnavailableInsteadOfVerificationFailure()
    {
        using var client = new HttpClient(new StatusHandler(HttpStatusCode.NotFound));
        var sut = CreateSut(
            Convert.ToBase64String(new byte[32]),
            client,
            Mock.Of<IPlatformApplicationPaths>());

        var result = await sut.CheckAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsFailed);
        Assert.Equal(
            PortableUpdateErrorCode.FeedUnavailable,
            Assert.IsType<PortableUpdateError>(Assert.Single(result.Errors)).Code);
    }

    [Theory]
    [InlineData("package-manager")]
    [InlineData("store")]
    public async Task CheckAsync_WhenInstallIsExternallyManaged_DoesNotAccessNetwork(string mode)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AutoUpdate:Enabled"] = "true",
            ["AutoUpdate:DistributionMode"] = mode
        }).Build();
        using var client = new HttpClient(new ThrowingHandler());
        var sut = new PortableUpdateService(
            configuration,
            VersionProvider(),
            client,
            new SignedAppcastVerifier(),
            new SignedPayloadVerifier(),
            Mock.Of<IPlatformApplicationPaths>(),
            NoOpPlatformFileSecurity.Instance,
            NullLogger<PortableUpdateService>.Instance);

        var result = await sut.CheckAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(PortableUpdateCheckStatus.Disabled, result.Value.Status);
    }

    [Fact]
    public async Task DownloadAsync_WhenResponseStreamFails_DeletesPartialFile()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var temp = new TempDir();
        var fixture = CreateSignedFixture("verified package"u8.ToArray());
        using var client = new HttpClient(new FixtureHandler(fixture, interruptPackage: true));
        var paths = new Mock<IPlatformApplicationPaths>();
        paths.SetupGet(value => value.ApplicationDataDirectory).Returns(temp.Path);
        var sut = CreateSut(fixture.PublicKey, client, paths.Object);
        var check = await sut.CheckAsync(cancellationToken);

        var download = await sut.DownloadAsync(check.Value.Offer!, cancellationToken: cancellationToken);

        Assert.True(download.IsFailed);
        Assert.Empty(Directory.GetFiles(Path.Combine(temp.Path, "Updates")));
    }

    [Fact]
    public async Task DownloadAsync_WhenReadyFilePermissionsCannotBeApplied_DeletesPackage()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var temp = new TempDir();
        var fixture = CreateSignedFixture("verified package"u8.ToArray());
        using var client = new HttpClient(new FixtureHandler(fixture));
        var paths = new Mock<IPlatformApplicationPaths>();
        paths.SetupGet(value => value.ApplicationDataDirectory).Returns(temp.Path);
        var security = new DelegatingPlatformFileSecurity
        {
            RestrictFile = path =>
            {
                if (path.Contains(".ready.", StringComparison.OrdinalIgnoreCase))
                    throw new UnauthorizedAccessException("synthetic permission failure");
            }
        };
        var sut = CreateSut(fixture.PublicKey, client, paths.Object, security);
        var check = await sut.CheckAsync(cancellationToken);

        var download = await sut.DownloadAsync(
            check.Value.Offer!,
            cancellationToken: cancellationToken);

        Assert.True(download.IsFailed);
        Assert.Empty(Directory.GetFiles(Path.Combine(temp.Path, "Updates")));
    }

    private static PortableUpdateService CreateSut(
        string publicKey,
        HttpClient client,
        IPlatformApplicationPaths paths,
        IPlatformFileSecurity? fileSecurity = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AutoUpdate:Enabled"] = "true",
            ["AutoUpdate:AppcastUrl"] = "https://example.invalid/appcast.xml",
            ["AutoUpdate:PublicKey"] = publicKey
        }).Build();
        return new PortableUpdateService(
            configuration,
            VersionProvider(),
            client,
            new SignedAppcastVerifier(),
            new SignedPayloadVerifier(),
            paths,
            fileSecurity ?? NoOpPlatformFileSecurity.Instance,
            NullLogger<PortableUpdateService>.Instance);
    }

    private static IApplicationVersionProvider VersionProvider() =>
        Mock.Of<IApplicationVersionProvider>(value =>
            value.CurrentVersion == new Version(1, 0, 0));

    private static SignedFixture CreateSignedFixture(byte[] packageBytes)
    {
        using var key = Key.Create(SignatureAlgorithm.Ed25519, new KeyCreationParameters
        {
            ExportPolicy = KeyExportPolicies.AllowPlaintextExport
        });
        var publicKey = Convert.ToBase64String(key.PublicKey.Export(KeyBlobFormat.RawPublicKey));
        var packageSignature = Convert.ToBase64String(
            SignatureAlgorithm.Ed25519.Sign(key, packageBytes));
        var operatingSystem = OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "macos" : "linux";
        var architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture
            .ToString().ToLowerInvariant();
        var appcast = $"<?xml version=\"1.0\"?><rss version=\"2.0\" xmlns:sparkle=\"http://www.andymatuschak.org/xml-namespaces/sparkle\"><channel><item><title>Update</title><description>Security fixes.</description><sparkle:version>99.0.0</sparkle:version><sparkle:os>{operatingSystem}</sparkle:os><sparkle:architecture>{architecture}</sparkle:architecture><enclosure url=\"https://example.invalid/update.zip\" sparkle:version=\"99.0.0\" sparkle:edSignature=\"{packageSignature}\" /></item></channel></rss>";
        var appcastBytes = Encoding.UTF8.GetBytes(appcast);
        var appcastSignature = Convert.ToBase64String(
            SignatureAlgorithm.Ed25519.Sign(key, appcastBytes));
        return new SignedFixture(
            publicKey,
            appcastBytes,
            Encoding.UTF8.GetBytes(appcastSignature),
            packageBytes);
    }

    private sealed record SignedFixture(
        string PublicKey,
        byte[] AppcastBytes,
        byte[] AppcastSignatureBytes,
        byte[] PackageBytes);

    private sealed class FixtureHandler(SignedFixture fixture, bool interruptPackage = false) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var bytes = request.RequestUri!.AbsolutePath switch
            {
                "/appcast.xml" => fixture.AppcastBytes,
                "/appcast.xml.signature" => fixture.AppcastSignatureBytes,
                "/update.zip" => fixture.PackageBytes,
                _ => throw new InvalidOperationException("Unexpected test URI.")
            };
            HttpContent content = interruptPackage && request.RequestUri.AbsolutePath == "/update.zip"
                ? new StreamContent(new InterruptingStream(bytes))
                : new ByteArrayContent(bytes);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = content
            });
        }
    }

    private sealed class InterruptingStream(byte[] bytes) : MemoryStream(bytes)
    {
        private bool _hasRead;

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (_hasRead) throw new IOException("Synthetic interrupted download.");
            _hasRead = true;
            return await base.ReadAsync(buffer[..Math.Min(buffer.Length, 4)], cancellationToken);
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Network must not be reached.");
    }

    private sealed class StatusHandler(HttpStatusCode statusCode) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(statusCode));
    }

    private sealed class TempDir : IDisposable
    {
        public TempDir()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "totp-update-tests-" + Guid.NewGuid().ToString("N"));
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
