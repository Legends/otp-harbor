using Microsoft.Extensions.Logging.Abstractions;
using TOTP.Core.Models;
using TOTP.Infrastructure.Services;

namespace TOTP.Tests.Unix.Infrastructure;

public sealed class ExportServicePortableTests
{
    private readonly ExportService _sut = new(NullLogger<ExportService>.Instance);

    [Fact]
    public async Task EncryptedStream_RoundTripsWithoutLocalFilePath()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var expected = new Account(
            Guid.NewGuid(), "Portable issuer", "JBSWY3DPEHPK3PXP", "portable-user");
        await using var storageProviderStream = new MemoryStream();

        var exported = await _sut.ExportToEncryptedStreamAsync(
            [expected],
            "portable-test-password",
            storageProviderStream,
            ExportFileFormat.Json,
            cancellationToken);
        storageProviderStream.Position = 0;
        var imported = await _sut.ImportFromStreamAsync(
            storageProviderStream,
            "BACKUP.ToTp",
            "portable-test-password",
            cancellationToken);

        Assert.True(exported.IsSuccess);
        Assert.True(imported.IsSuccess);
        var account = Assert.Single(imported.Value);
        Assert.Equal(expected.ID, account.ID);
        Assert.Equal(expected.Secret, account.Secret);
    }

    [Fact]
    public async Task PlaintextStream_UsesFileNameOnlyForPortableFormatDetection()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var source = new MemoryStream(
            "issuer|account_name|secret|id\nIssuer|user|JBSWY3DPEHPK3PXP|"u8.ToArray(),
            writable: false);

        var imported = await _sut.ImportFromStreamAsync(
            source,
            "provider-item.TXT",
            cancellationToken: cancellationToken);

        Assert.True(imported.IsSuccess);
        Assert.Single(imported.Value);
    }
}
