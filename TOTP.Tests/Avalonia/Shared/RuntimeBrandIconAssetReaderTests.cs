using System.IO.Compression;
using TOTP.Avalonia.Shared.Controls;
using TOTP.Core.Services.Models;
using TOTP.Tests.Services;

namespace TOTP.Tests.Avalonia.Shared;

public sealed class RuntimeBrandIconAssetReaderTests
{
    [Fact]
    public async Task ReadValidatedAsync_LoadsTheExactArchiveEntryWithoutExtraction()
    {
        using var temp = new TempDir();
        var png = OtpHarborRuntimePngTestData.Create();
        var archivePath = CreateArchive(temp.Path, png);
        var runtime = CreateArchiveDescriptor(archivePath, png);

        var encoded = await RuntimeBrandIconAssetReader.ReadValidatedAsync(
            runtime,
            TestContext.Current.CancellationToken);

        Assert.Equal(png, encoded);
        Assert.Empty(Directory.EnumerateFiles(temp.Path, "*.png", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task ReadValidatedAsync_RechecksHashBeforeReturningDecoderInput()
    {
        using var temp = new TempDir();
        var png = OtpHarborRuntimePngTestData.Create();
        var archivePath = CreateArchive(temp.Path, png);
        var runtime = CreateArchiveDescriptor(archivePath, png) with
        {
            Sha256 = new string('0', 64),
            CacheKey = $"c-plus-plus:{new string('0', 64)}"
        };

        var encoded = await RuntimeBrandIconAssetReader.ReadValidatedAsync(
            runtime,
            TestContext.Current.CancellationToken);

        Assert.Null(encoded);
    }

    [Fact]
    public async Task ReadValidatedAsync_RejectsNoncanonicalEntryBeforeArchiveRead()
    {
        using var temp = new TempDir();
        var png = OtpHarborRuntimePngTestData.Create();
        var archivePath = CreateArchive(temp.Path, png);
        var runtime = CreateArchiveDescriptor(archivePath, png) with
        {
            ArchiveEntryPath = "runtime/foreground-128/other.png"
        };

        var encoded = await RuntimeBrandIconAssetReader.ReadValidatedAsync(
            runtime,
            TestContext.Current.CancellationToken);

        Assert.Null(encoded);
    }

    [Fact]
    public async Task ReadValidatedAsync_RejectsUnsupportedPngProfileBeforeDecoderInput()
    {
        using var temp = new TempDir();
        var png = OtpHarborRuntimePngTestData.Create(includeAncillaryChunk: true);
        var archivePath = CreateArchive(temp.Path, png);
        var runtime = CreateArchiveDescriptor(archivePath, png);

        var encoded = await RuntimeBrandIconAssetReader.ReadValidatedAsync(
            runtime,
            TestContext.Current.CancellationToken);

        Assert.Null(encoded);
    }

    [Fact]
    public async Task ReadValidatedAsync_LoadsLegacyExtractedV2File()
    {
        using var temp = new TempDir();
        var png = OtpHarborRuntimePngTestData.Create();
        var path = Path.Combine(temp.Path, "c-plus-plus.png");
        await File.WriteAllBytesAsync(path, png, TestContext.Current.CancellationToken);
        var sha256 = OtpHarborRuntimePngTestData.Sha256(png);
        var runtime = new BrandRuntimeIcon(
            $"c-plus-plus:{sha256}",
            path,
            sha256,
            128,
            128,
            BrandId: "c-plus-plus");

        var encoded = await RuntimeBrandIconAssetReader.ReadValidatedAsync(
            runtime,
            TestContext.Current.CancellationToken);

        Assert.Equal(png, encoded);
    }

    private static BrandRuntimeIcon CreateArchiveDescriptor(string archivePath, byte[] png)
    {
        var sha256 = OtpHarborRuntimePngTestData.Sha256(png);
        return new BrandRuntimeIcon(
            $"c-plus-plus:{sha256}",
            null,
            sha256,
            128,
            128,
            archivePath,
            "runtime/foreground-128/c-plus-plus.png",
            "c-plus-plus");
    }

    private static string CreateArchive(string directory, byte[] png)
    {
        var path = Path.Combine(directory, $"icons-{Guid.NewGuid():N}.otphicons");
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        var entry = archive.CreateEntry(
            "runtime/foreground-128/c-plus-plus.png",
            CompressionLevel.NoCompression);
        using var output = entry.Open();
        output.Write(png);
        return path;
    }

    private sealed class TempDir : IDisposable
    {
        internal TempDir()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"otp-harbor-runtime-reader-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        internal string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }
}
