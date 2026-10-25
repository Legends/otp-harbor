using System.IO.Compression;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using TOTP.Core.Icons;
using TOTP.Core.Services.Models;
using TOTP.Infrastructure.Icons;

namespace TOTP.Tests.Services;

public sealed class IconPackImporterTests
{
    private const string ValidSvg = "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 24 24\"><path d=\"M0 0h24v24H0z\"/></svg>";

    [Fact]
    public async Task SimpleIconsImporter_AcceptsReleaseRootAndFutureOptionalMetadata()
    {
        await using var source = CreateArchive(archive =>
        {
            WriteEntry(archive, "simple-icons-16.33.0/data/simple-icons.json", """
                [
                  {
                    "title":"GitHub",
                    "hex":"181717",
                    "source":"https://github.com/logos",
                    "aliases":{"aka":["GitHub.com"]},
                    "futureOptionalField":{"value":true}
                  }
                ]
                """);
            WriteEntry(archive, "simple-icons-16.33.0/icons/github.svg", ValidSvg);
            WriteEntry(archive, "simple-icons-16.33.0/LICENSE.md", "Synthetic license");
        });
        var sut = new SimpleIconsImporter();

        var result = await sut.ImportAsync(
            new IconPackSource { Stream = source, FileName = "simple-icons-16.33.0.zip" },
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal("16.33.0", result.Value.Version);
        Assert.Equal(BrandIconPackFormat.SimpleIcons, result.Value.Format);
        var icon = Assert.Single(result.Value.Icons);
        Assert.Equal("github", icon.Id);
        Assert.Contains("GitHub.com", icon.Issuers);
        Assert.Single(result.Value.Notices);
    }

    [Fact]
    public async Task SimpleIconsImporter_AcceptsPackageLayoutAndReadsPackageVersion()
    {
        await using var source = CreateArchive(archive =>
        {
            WriteEntry(archive, "package/package.json", """
                {"name":"simple-icons","version":"17.0.0","future":true}
                """);
            WriteEntry(archive, "package/data/simple-icons.json", """
                [{"title":"Simple Icons","slug":"simpleicons","hex":"111111","future":"ignored"}]
                """);
            WriteEntry(archive, "package/icons/simpleicons.svg", ValidSvg);
        });

        var result = await new SimpleIconsImporter().ImportAsync(
            new IconPackSource { Stream = source },
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal("17.0.0", result.Value.Version);
        Assert.Equal("simpleicons", Assert.Single(result.Value.Icons).Id);
    }

    [Fact]
    public async Task AegisImporter_AcceptsOfficialManifestShapeAndIssuerAliases()
    {
        await using var source = CreateArchive(archive =>
        {
            WriteEntry(archive, "aegis-icons/pack.json", """
                {
                  "uuid":"c553f06f-2a17-46ca-87f5-56af90dd0500",
                  "name":"Synthetic Aegis Pack",
                  "version":20261002,
                  "futureOptionalField":"ignored",
                  "icons":[
                    {
                      "name":"GitHub",
                      "filename":"icons/GitHub.svg",
                      "category":"Services",
                      "issuer":["github","github.com"],
                      "future":42
                    },
                    {
                      "name":"Raster-only",
                      "filename":"icons/raster.png",
                      "issuer":["raster"]
                    },
                    {
                      "name":"GitHub generic duplicate",
                      "filename":"generic/GitHub.svg",
                      "issuer":["github-generic"]
                    }
                  ]
                }
                """);
            WriteEntry(archive, "aegis-icons/ICONS/GitHub.svg", ValidSvg);
            WriteEntry(archive, "aegis-icons/generic/GitHub.svg", ValidSvg);
            WriteEntry(archive, "aegis-icons/icons/raster.png", "not imported");
            WriteEntry(archive, "aegis-icons/LICENSE.md", "Synthetic license");
        });
        var sut = new AegisIconPackImporter();

        var result = await sut.ImportAsync(
            new IconPackSource { Stream = source, FileName = "aegis-icons.zip" },
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal("Synthetic Aegis Pack", result.Value.ProviderDisplayName);
        Assert.Equal("20261002", result.Value.Version);
        Assert.Equal(BrandIconPackFormat.Aegis, result.Value.Format);
        var icon = Assert.Single(result.Value.Icons);
        Assert.Equal("github", icon.Id);
        Assert.Contains("github.com", icon.Issuers);
        Assert.Single(result.Value.Notices);
    }

    [Fact]
    public async Task Resolver_DoesNotFallBackWhenRecognizedAegisPackIsInvalid()
    {
        await using var source = CreateArchive(archive =>
        {
            WriteEntry(archive, "pack.json", "{\"name\":\"missing required fields\"}");
            WriteEntry(archive, "github.svg", ValidSvg);
        });
        var sut = new IconPackImporterResolver(
            [
                new SimpleIconsImporter(),
                new AegisIconPackImporter(),
                new FilenameIndexedIconPackImporter()
            ],
            NullLogger<IconPackImporterResolver>.Instance);

        var result = await sut.ImportAsync(
            new IconPackSource { Stream = source },
            TestContext.Current.CancellationToken);

        Assert.True(result.IsFailed);
    }

    [Fact]
    public async Task Resolver_DoesNotFallBackWhenSimpleIconsMetadataIsAmbiguous()
    {
        await using var source = CreateArchive(archive =>
        {
            WriteEntry(archive, "first/data/simple-icons.json", "[]");
            WriteEntry(archive, "second/data/simple-icons.json", "[]");
            WriteEntry(archive, "github.svg", ValidSvg);
        });
        var sut = new IconPackImporterResolver(
            [
                new SimpleIconsImporter(),
                new AegisIconPackImporter(),
                new FilenameIndexedIconPackImporter()
            ],
            NullLogger<IconPackImporterResolver>.Instance);

        var result = await sut.ImportAsync(
            new IconPackSource { Stream = source },
            TestContext.Current.CancellationToken);

        Assert.True(result.IsFailed);
    }

    [Fact]
    public async Task OtpHarborImporter_AcceptsFormatV1AndPreservesCanonicalMetadata()
    {
        await using var source = CreateOtpHarborArchive();
        var sut = new OtpHarborIconPackImporter();

        var result = await sut.ImportAsync(
            new IconPackSource
            {
                Stream = source,
                FileName = "otp-harbor-icons.otphicons"
            },
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(BrandIconPackFormat.OtpHarbor, result.Value.Format);
        Assert.Equal("otp-harbor-icons", result.Value.ProviderId);
        Assert.Equal("format-1", result.Value.Version);
        var icon = Assert.Single(result.Value.Icons);
        Assert.Equal("c-plus-plus", icon.Id);
        Assert.Equal("#00599C", icon.BackgroundColor);
        Assert.Contains("C++", icon.Issuers);
        Assert.Equal("simple-icons", icon.SelectedSource?.Provider);
        Assert.Equal("cplusplus", icon.SelectedSource?.SourceId);
        Assert.Equal("otp-harbor-icons", result.Value.Metadata?.PackId);
        Assert.Contains(
            result.Value.Metadata!.IssuerAliases,
            alias => alias.Key == "c++" && alias.BrandId == "c-plus-plus");
        var license = Assert.Single(result.Value.Notices);
        Assert.Equal("licenses/simple-icons/license.md", license.RelativePath);
    }

    [Fact]
    public async Task OtpHarborImporter_AcceptsFormatV2AndPreservesValidatedRuntimeMetadata()
    {
        await using var source = CreateOtpHarborV2Archive();

        var result = await new OtpHarborIconPackImporter().ImportAsync(
            new IconPackSource { Stream = source, FileName = "icons.otphicons" },
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal("format-2", result.Value.Version);
        Assert.Equal(2, result.Value.Metadata?.FormatVersion);
        var icon = Assert.Single(result.Value.Icons);
        Assert.Null(icon.SvgData);
        Assert.Null(icon.SourceFileName);
        var runtime = icon.RuntimeIcon;
        Assert.NotNull(runtime);
        Assert.Equal("runtime/foreground-128/c-plus-plus.png", runtime.ArchivePath);
        Assert.Equal("otp-harbor-foreground-v1", runtime.RenderProfile);
        Assert.Equal(128, runtime.Width);
        Assert.Equal(128, runtime.Height);
    }

    [Fact]
    public async Task OtpHarborImporter_RejectsLegacySvgMemberInFormatV2()
    {
        await using var source = CreateOtpHarborV2Archive(includeLegacyIcon: true);

        var result = await new OtpHarborIconPackImporter().ImportAsync(
            new IconPackSource { Stream = source, FileName = "icons.otphicons" },
            TestContext.Current.CancellationToken);

        Assert.True(result.IsFailed);
    }

    [Theory]
    [InlineData("hash")]
    [InlineData("dimensions")]
    [InlineData("forbidden-chunk")]
    public async Task OtpHarborImporter_RejectsInvalidFormatV2RuntimeAssets(string corruption)
    {
        var png = OtpHarborRuntimePngTestData.Create(
            width: corruption == "dimensions" ? 64 : 128,
            includeAncillaryChunk: corruption == "forbidden-chunk",
            red: 255,
            green: 255,
            blue: 255);
        await using var source = CreateOtpHarborV2Archive(
            png,
            sha256: corruption == "hash"
                ? new string('0', 64)
                : null);

        var result = await new OtpHarborIconPackImporter().ImportAsync(
            new IconPackSource { Stream = source, FileName = "icons.otphicons" },
            TestContext.Current.CancellationToken);

        Assert.True(result.IsFailed);
    }

    [Fact]
    public async Task OtpHarborImporter_DoesNotRepeatBuilderOwnedPixelVisibilityValidation()
    {
        var png = OtpHarborRuntimePngTestData.Create(red: 0, green: 89, blue: 156);
        await using var source = CreateOtpHarborV2Archive(png);

        var result = await new OtpHarborIconPackImporter().ImportAsync(
            new IconPackSource { Stream = source, FileName = "icons.otphicons" },
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task OtpHarborImporter_ChecksEveryReferencedRuntimeHashBeforeSuccess()
    {
        await using var source = CreateOtpHarborV2Archive(addSecondBrandWithInvalidHash: true);

        var result = await new OtpHarborIconPackImporter().ImportAsync(
            new IconPackSource { Stream = source, FileName = "icons.otphicons" },
            TestContext.Current.CancellationToken);

        Assert.True(result.IsFailed);
    }

    [Fact]
    public async Task OtpHarborImporter_RejectsPngWithMatchingHashButCorruptChunkChecksum()
    {
        var png = OtpHarborRuntimePngTestData.Create();
        png[^1] ^= 0xff;
        await using var source = CreateOtpHarborV2Archive(png);

        var result = await new OtpHarborIconPackImporter().ImportAsync(
            new IconPackSource { Stream = source, FileName = "icons.otphicons" },
            TestContext.Current.CancellationToken);

        Assert.True(result.IsFailed);
    }

    [Fact]
    public async Task OtpHarborImporter_RejectsDuplicateNormalizedRuntimePath()
    {
        await using var source = CreateOtpHarborV2Archive(duplicateRuntimeEntry: true);

        var result = await new OtpHarborIconPackImporter().ImportAsync(
            new IconPackSource { Stream = source, FileName = "icons.otphicons" },
            TestContext.Current.CancellationToken);

        Assert.True(result.IsFailed);
    }

    [Fact]
    public async Task OtpHarborImporter_RejectsUnsupportedRuntimeRenderProfile()
    {
        await using var source = CreateOtpHarborV2Archive(runtimeRenderProfile: "unknown-profile");

        var result = await new OtpHarborIconPackImporter().ImportAsync(
            new IconPackSource { Stream = source, FileName = "icons.otphicons" },
            TestContext.Current.CancellationToken);

        Assert.True(result.IsFailed);
    }

    [Fact]
    public async Task OtpHarborImporter_RejectsCorruptZipArchive()
    {
        await using var source = new MemoryStream("not a zip archive"u8.ToArray());

        var result = await new OtpHarborIconPackImporter().ImportAsync(
            new IconPackSource { Stream = source, FileName = "icons.otphicons" },
            TestContext.Current.CancellationToken);

        Assert.True(result.IsFailed);
    }

    [Fact]
    public async Task OtpHarborImporter_RejectsRuntimePngAboveEncodedSizeLimit()
    {
        var oversized = new byte[RuntimePngProfileValidator.MaximumEncodedBytes + 1];
        await using var source = CreateOtpHarborV2Archive(oversized);

        var result = await new OtpHarborIconPackImporter().ImportAsync(
            new IconPackSource { Stream = source, FileName = "icons.otphicons" },
            TestContext.Current.CancellationToken);

        Assert.True(result.IsFailed);
    }

    [Fact]
    public async Task OtpHarborImporter_PropagatesCancellation()
    {
        await using var source = CreateOtpHarborV2Archive();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new OtpHarborIconPackImporter().ImportAsync(
                new IconPackSource { Stream = source, FileName = "icons.otphicons" },
                cancellation.Token));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task OtpHarborImporter_RejectsNoncanonicalOrUnreferencedFormatV2Paths(
        bool noncanonicalRuntimePath,
        bool addUnreferencedEntry)
    {
        await using var source = CreateOtpHarborV2Archive(
            runtimeManifestPath: noncanonicalRuntimePath
                ? "runtime/foreground-128/wrong.png"
                : null,
            addUnreferencedEntry: addUnreferencedEntry);

        var result = await new OtpHarborIconPackImporter().ImportAsync(
            new IconPackSource { Stream = source, FileName = "icons.otphicons" },
            TestContext.Current.CancellationToken);

        Assert.True(result.IsFailed);
    }

    [Fact]
    public async Task OtpHarborImporter_RejectsRuntimeMemberDeclaredByFormatV1()
    {
        var png = OtpHarborRuntimePngTestData.Create();
        await using var source = CreateOtpHarborV2Archive(png, declaredFormatVersion: 1);

        var result = await new OtpHarborIconPackImporter().ImportAsync(
            new IconPackSource { Stream = source, FileName = "icons.otphicons" },
            TestContext.Current.CancellationToken);

        Assert.True(result.IsFailed);
    }

    [Fact]
    public async Task OtpHarborImporter_AllowsResolvedLocalSvgReferencesButRejectsExternalReferences()
    {
        const string localReferenceSvg = """
            <svg xmlns="http://www.w3.org/2000/svg">
              <defs>
                <linearGradient id="base"><stop offset="0" stop-color="#000000"/></linearGradient>
                <linearGradient id="derived" href="#base"/>
              </defs>
              <path fill="url(#derived)" d="M0 0h24v24H0z"/>
            </svg>
            """;
        await using var local = CreateOtpHarborArchive(localReferenceSvg);

        var accepted = await new OtpHarborIconPackImporter().ImportAsync(
            new IconPackSource { Stream = local, FileName = "icons.otphicons" },
            TestContext.Current.CancellationToken);

        Assert.True(accepted.IsSuccess);

        const string externalReferenceSvg = """
            <svg xmlns="http://www.w3.org/2000/svg">
              <path d="M0 0h24v24H0z"/>
              <image href="https://example.invalid/icon.png"/>
            </svg>
            """;
        await using var external = CreateOtpHarborArchive(externalReferenceSvg);
        var rejected = await new OtpHarborIconPackImporter().ImportAsync(
            new IconPackSource { Stream = external, FileName = "icons.otphicons" },
            TestContext.Current.CancellationToken);

        Assert.True(rejected.IsFailed);
    }

    [Fact]
    public async Task OtpHarborImporter_AcceptsValidSvgLargerThanLegacy64KiBLimit()
    {
        var random = new Random(17);
        var paddingBytes = new byte[48 * 1024];
        random.NextBytes(paddingBytes);
        var svg = $"<svg xmlns=\"http://www.w3.org/2000/svg\"><desc>{Convert.ToHexString(paddingBytes)}</desc><path d=\"M0 0h24v24H0z\"/></svg>";
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(svg) > 64 * 1024);
        await using var archive = CreateOtpHarborArchive(svg);

        var result = await new OtpHarborIconPackImporter().ImportAsync(
            new IconPackSource { Stream = archive, FileName = "icons.otphicons" },
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Resolver_ClaimsMalformedOtpHarborPackWithoutLegacyFallback()
    {
        await using var source = CreateArchive(archive =>
        {
            WriteEntry(archive, "pack.json", "{\"formatVersion\":2,\"packId\":\"otp-harbor-icons\",\"issuerAliases\":[]}");
            WriteEntry(archive, "github.svg", ValidSvg);
        });
        var sut = new IconPackImporterResolver(
            [
                new OtpHarborIconPackImporter(),
                new SimpleIconsImporter(),
                new AegisIconPackImporter(),
                new FilenameIndexedIconPackImporter()
            ],
            NullLogger<IconPackImporterResolver>.Instance);

        var result = await sut.ImportAsync(
            new IconPackSource { Stream = source, FileName = "icons.otphicons" },
            TestContext.Current.CancellationToken);

        Assert.True(result.IsFailed);
    }

    [Fact]
    public async Task SvgIconImporter_NormalizesSafeLocalSvgAndRejectsExternalContent()
    {
        var sut = new SvgIconImporter();
        await using var valid = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(ValidSvg));

        var imported = await sut.ImportAsync(
            new CustomIconSource
            {
                Stream = valid,
                FileName = "My Brand.svg",
                Issuers = ["My Brand"]
            },
            TestContext.Current.CancellationToken);

        Assert.True(imported.IsSuccess);
        Assert.StartsWith("custom_", imported.Value.Id, StringComparison.Ordinal);
        Assert.Equal("My Brand", imported.Value.Name);
        Assert.Equal("custom-svg", imported.Value.ProviderId);

        await using var unsafeSvg = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(
            "<svg xmlns=\"http://www.w3.org/2000/svg\"><path d=\"M0 0\"/><image href=\"https://example.invalid/icon.png\"/></svg>"));
        var rejected = await sut.ImportAsync(
            new CustomIconSource { Stream = unsafeSvg, FileName = "unsafe.svg" },
            TestContext.Current.CancellationToken);
        Assert.True(rejected.IsFailed);
        Assert.Equal(
            CustomIconImportFailureReason.UnsafeContent,
            Assert.IsType<CustomIconImportError>(Assert.Single(rejected.Errors)).Reason);
    }

    private static MemoryStream CreateArchive(Action<ZipArchive> write)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
            write(archive);
        stream.Position = 0;
        return stream;
    }

    private static MemoryStream CreateOtpHarborArchive(string svg = ValidSvg)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, "pack.json", """
                {
                  "formatVersion":1,
                  "packId":"otp-harbor-icons",
                  "name":"OTP Harbor Icons",
                  "sources":[{
                    "provider":"simple-icons",
                    "inputFileName":"source.zip",
                    "sha256":"0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
                    "version":"16.34.0",
                    "revision":null,
                    "sourceUrl":"https://github.com/simple-icons/simple-icons/releases/tag/16.34.0",
                    "metadata":{"version":"16.34.0"},
                    "licenseFiles":["licenses/simple-icons/license.md"]
                  }],
                  "brands":[{
                    "id":"c-plus-plus",
                    "displayName":"C++",
                    "backgroundColor":"#00599C",
                    "icon":"icons/c-plus-plus.svg",
                    "issuerAliases":["C++","C Plus Plus"],
                    "selectedSource":{
                      "provider":"simple-icons",
                      "sourceId":"cplusplus",
                      "metadata":{"nativeBrandColor":"#00599C"}
                    },
                    "sources":[{
                      "provider":"simple-icons",
                      "sourceId":"cplusplus",
                      "metadata":{"nativeBrandColor":"#00599C"}
                    }]
                  }],
                  "issuerAliases":[
                    {"key":"c++","brandId":"c-plus-plus"},
                    {"key":"c plus plus","brandId":"c-plus-plus"}
                  ]
                }
                """);
            WriteEntry(archive, "icons/c-plus-plus.svg", svg);
            WriteEntry(archive, "licenses/simple-icons/license.md", "Synthetic license");
        }
        stream.Position = 0;
        return stream;
    }

    private static MemoryStream CreateOtpHarborV2Archive(
        byte[]? png = null,
        string? sha256 = null,
        int declaredFormatVersion = 2,
        string? runtimeManifestPath = null,
        bool addUnreferencedEntry = false,
        bool includeLegacyIcon = false,
        bool addSecondBrandWithInvalidHash = false,
        bool duplicateRuntimeEntry = false,
        string runtimeRenderProfile = "otp-harbor-foreground-v1")
    {
        png ??= OtpHarborRuntimePngTestData.Create();
        sha256 ??= OtpHarborRuntimePngTestData.Sha256(png);
        var brand = new Dictionary<string, object?>
        {
            ["id"] = "c-plus-plus",
            ["displayName"] = "C++",
            ["backgroundColor"] = "#00599C",
            ["issuerAliases"] = new[] { "C++", "C Plus Plus" },
            ["selectedSource"] = new
            {
                provider = "simple-icons",
                sourceId = "cplusplus",
                metadata = new Dictionary<string, string?> { ["nativeBrandColor"] = "#00599C" }
            },
            ["sources"] = new[]
            {
                new
                {
                    provider = "simple-icons",
                    sourceId = "cplusplus",
                    metadata = new Dictionary<string, string?> { ["nativeBrandColor"] = "#00599C" }
                }
            },
            ["runtimeIcon"] = new
            {
                path = runtimeManifestPath ?? "runtime/foreground-128/c-plus-plus.png",
                mediaType = "image/png",
                width = 128,
                height = 128,
                sha256,
                renderProfile = runtimeRenderProfile
            }
        };
        if (includeLegacyIcon) brand["icon"] = "icons/c-plus-plus.svg";
        var brands = new List<Dictionary<string, object?>> { brand };
        if (addSecondBrandWithInvalidHash)
        {
            brands.Add(new Dictionary<string, object?>
            {
                ["id"] = "github",
                ["displayName"] = "GitHub",
                ["backgroundColor"] = "#181717",
                ["issuerAliases"] = new[] { "GitHub" },
                ["selectedSource"] = new
                {
                    provider = "simple-icons",
                    sourceId = "github",
                    metadata = new Dictionary<string, string?> { ["nativeBrandColor"] = "#181717" }
                },
                ["sources"] = new[]
                {
                    new
                    {
                        provider = "simple-icons",
                        sourceId = "github",
                        metadata = new Dictionary<string, string?> { ["nativeBrandColor"] = "#181717" }
                    }
                },
                ["runtimeIcon"] = new
                {
                    path = "runtime/foreground-128/github.png",
                    mediaType = "image/png",
                    width = 128,
                    height = 128,
                    sha256 = new string('0', 64),
                    renderProfile = "otp-harbor-foreground-v1"
                }
            });
        }
        var manifest = JsonSerializer.Serialize(new
        {
            formatVersion = declaredFormatVersion,
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
            brands,
            issuerAliases = addSecondBrandWithInvalidHash
                ? new[]
                {
                    new { key = "c++", brandId = "c-plus-plus" },
                    new { key = "c plus plus", brandId = "c-plus-plus" },
                    new { key = "github", brandId = "github" }
                }
                :
                [
                    new { key = "c++", brandId = "c-plus-plus" },
                    new { key = "c plus plus", brandId = "c-plus-plus" }
                ]
        });
        return CreateArchive(archive =>
        {
            WriteEntry(archive, "pack.json", manifest);
            if (includeLegacyIcon) WriteEntry(archive, "icons/c-plus-plus.svg", ValidSvg);
            WriteEntry(archive, "runtime/foreground-128/c-plus-plus.png", png);
            if (duplicateRuntimeEntry)
                WriteEntry(archive, "runtime/foreground-128/c-plus-plus.png", png);
            if (addSecondBrandWithInvalidHash)
                WriteEntry(archive, "runtime/foreground-128/github.png", png);
            WriteEntry(archive, "licenses/simple-icons/license.md", "Synthetic license");
            if (addUnreferencedEntry) WriteEntry(archive, "runtime/unreferenced.png", png);
        });
    }

    private static void WriteEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(content);
    }

    private static void WriteEntry(ZipArchive archive, string name, byte[] content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.NoCompression);
        using var output = entry.Open();
        output.Write(content);
    }
}
