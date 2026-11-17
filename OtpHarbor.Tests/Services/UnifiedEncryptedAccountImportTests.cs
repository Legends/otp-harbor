using System.Text;
using System.IO.Compression;
using FluentResults;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TOTP.Core.Backup;
using TOTP.Core.Models;
using TOTP.Core.Services.Models;
using TOTP.Infrastructure.Services;

namespace TOTP.Tests.Services;

public sealed class UnifiedEncryptedAccountImportTests
{
    private const string Password = "synthetic-backup-password";
    private static Account Synthetic => new(Guid.NewGuid(), "Demo", "JBSWY3DPEHPK3PXP", null, 60, algorithm: "SHA512", digits: 8);
    private static Task<AccountImportFormat?> Choose(IReadOnlyList<AccountImportFormat> formats, CancellationToken _) => Task.FromResult<AccountImportFormat?>(formats[0]);

    [Theory]
    [InlineData("{\"version\":1,\"db\":\"encrypted-aegis-data\"}")]
    [InlineData("{\"encrypted\":true,\"items\":[]}")]
    [InlineData("not a supported account export")]
    public async Task ForeignEncryptedOrUnknownInput_DoesNotPromptForAnOtpHarborPassword(string content)
    {
        var service = new ExportService(NullLogger<ExportService>.Instance);
        using var input = new MemoryStream(Encoding.UTF8.GetBytes(content));
        var result = await service.ImportFromStreamWithFormatSelectionAsync(input, "backup.otphbp", Choose,
            (_, _) => throw new InvalidOperationException("Unexpected password prompt"), TestContext.Current.CancellationToken);
        Assert.True(result.IsFailed);
    }

    [Fact]
    public async Task OversizedPlaintext_StillFailsBeforePasswordOrParsing()
    {
        var service = new ExportService(NullLogger<ExportService>.Instance);
        using var input = new MemoryStream(new byte[5 * 1024 * 1024 + 1]);
        var result = await service.ImportFromStreamWithFormatSelectionAsync(input, "renamed.json", Choose,
            (_, _) => throw new InvalidOperationException("Unexpected password prompt"), TestContext.Current.CancellationToken);
        Assert.True(result.IsFailed);
    }

    [Fact]
    public async Task LegacyUnversionedEncryptedBackup_RemainsReadableAfterRenaming()
    {
        var service = new ExportService(NullLogger<ExportService>.Instance);
        var account = Synthetic;
        using var encrypted = new MemoryStream();
        byte[] data;
        do
        {
            encrypted.SetLength(0);
            Assert.True((await service.ExportToEncryptedStreamAsync([account], Password, encrypted, ExportFileFormat.Json, TestContext.Current.CancellationToken)).IsSuccess);
            data = encrypted.ToArray();
        } while (Enum.IsDefined((ExportFileFormat)data[5]));
        using var input = new NonSeekableStream(data.Take(4).Concat(data.Skip(5)).ToArray());
        var result = await service.ImportFromStreamWithFormatSelectionAsync(input, "legacy-renamed.bin", Choose,
            (request, token) => request.TryReadAsync(Password, token), TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        Assert.Equal(account.ID, Assert.Single(result.Value).ID);
    }

    [Theory]
    [InlineData("backup.otphbp", ExportFileFormat.Json)]
    [InlineData("backup.totp", ExportFileFormat.Csv)]
    [InlineData("renamed.bin", ExportFileFormat.Txt)]
    public async Task EncryptedBackup_IsDetectedByContent_RetriesPasswordAndPreservesAccounts(string fileName, ExportFileFormat format)
    {
        var service = new ExportService(NullLogger<ExportService>.Instance);
        var account = Synthetic;
        using var encrypted = new MemoryStream();
        Assert.True((await service.ExportToEncryptedStreamAsync([account], Password, encrypted, format, TestContext.Current.CancellationToken)).IsSuccess);
        using var input = new NonSeekableStream(encrypted.ToArray());
        AccountImportPasswordRequest? retained = null;
        var result = await service.ImportFromStreamWithFormatSelectionAsync(input, fileName, Choose, async (request, token) =>
        {
            retained = request;
            Assert.False(request.UsesMasterPassword);
            Assert.True((await request.TryReadAsync("incorrect", token)).IsFailed);
            return await request.TryReadAsync(Password, token);
        }, TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        var imported = Assert.Single(result.Value);
        Assert.Equal(account.ID, imported.ID);
        Assert.Equal(account.Secret, imported.Secret);
        Assert.Equal(account.Issuer, imported.Issuer);
        Assert.Equal(account.Algorithm, imported.Algorithm);
        Assert.Equal(account.Digits, imported.Digits);
        Assert.Equal(account.PeriodSeconds, imported.PeriodSeconds);
        Assert.Equal(AccountImportFormat.EncryptedOtpHarborBackup, Assert.Single(result.Successes.OfType<AccountImportReadSummary>()).Format);
        Assert.NotNull(retained);
        Assert.True((await retained.TryReadAsync(Password, TestContext.Current.CancellationToken)).IsFailed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancelOrTamper_ReturnsNoAccounts(bool tamper)
    {
        var service = new ExportService(NullLogger<ExportService>.Instance);
        using var encrypted = new MemoryStream();
        Assert.True((await service.ExportToEncryptedStreamAsync([Synthetic], Password, encrypted, ExportFileFormat.Json, TestContext.Current.CancellationToken)).IsSuccess);
        var data = encrypted.ToArray();
        if (tamper) data[^1] ^= 1;
        using var input = new MemoryStream(data);
        var result = await service.ImportFromStreamWithFormatSelectionAsync(input, "renamed.csv", Choose,
            (request, token) => tamper ? request.TryReadAsync(Password, token)
                : Task.FromResult(Result.Fail<List<Account>>(new AccountImportReadCancelled())), TestContext.Current.CancellationToken);
        Assert.True(result.IsFailed);
        if (!tamper) Assert.Single(result.Errors.OfType<AccountImportReadCancelled>());
        Assert.Empty(result.Successes.OfType<AccountImportReadSummary>());
    }

    [Fact]
    public async Task PlaintextWithBackupExtension_DoesNotAskForPassword()
    {
        var service = new ExportService(NullLogger<ExportService>.Instance);
        using var plain = new MemoryStream();
        Assert.True((await service.ExportToStreamAsync([Synthetic], plain, ExportFileFormat.Json, TestContext.Current.CancellationToken)).IsSuccess);
        plain.Position = 0;
        var result = await service.ImportFromStreamWithFormatSelectionAsync(plain, "misleading.otphbp", Choose,
            (_, _) => throw new InvalidOperationException("Unexpected password prompt"), TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        Assert.Equal(AccountImportFormat.OtpHarborJson, Assert.Single(result.Successes.OfType<AccountImportReadSummary>()).Format);
    }

    [Fact]
    public async Task AutomaticSnapshot_UsesExistingReaderAndMasterPasswordGuidanceRegardlessOfSuffix()
    {
        var snapshot = new Mock<IAutomaticBackupSnapshotService>(MockBehavior.Strict);
        snapshot.Setup(value => value.ReadAccountsAsync(It.IsAny<Stream>(), Password, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok<IReadOnlyList<Account>>([Synthetic]));
        var service = new ExportService(NullLogger<ExportService>.Instance, snapshot.Object);
        using var input = new MemoryStream(Encoding.UTF8.GetBytes("{\"format\":\"otp-harbor-automatic-backup\",\"version\":1}"));
        var result = await service.ImportFromStreamWithFormatSelectionAsync(input, "renamed.json", Choose, (request, token) =>
        {
            Assert.True(request.UsesMasterPassword);
            return request.TryReadAsync(Password, token);
        }, TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        Assert.Single(result.Value);
        Assert.Equal(AccountImportFormat.EncryptedOtpHarborBackup, Assert.Single(result.Successes.OfType<AccountImportReadSummary>()).Format);
        snapshot.VerifyAll();
    }

    [Fact]
    public async Task AutomaticZipSnapshot_DispatchesByManifestContentWithoutAnExtension()
    {
        var snapshot = new Mock<IAutomaticBackupSnapshotService>(MockBehavior.Strict);
        snapshot.Setup(value => value.ReadAccountsAsync(It.IsAny<Stream>(), Password, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok<IReadOnlyList<Account>>([Synthetic]));
        var service = new ExportService(NullLogger<ExportService>.Instance, snapshot.Object);
        using var input = ZipManifest("{\"format\":\"otp-harbor-automatic-backup\",\"version\":2}");
        var result = await service.ImportFromStreamWithFormatSelectionAsync(input, "renamed", Choose, (request, token) =>
        {
            Assert.True(request.UsesMasterPassword);
            return request.TryReadAsync(Password, token);
        }, TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        Assert.Single(result.Value);
        snapshot.VerifyAll();
    }

    [Theory]
    [InlineData("{\"format\":\"other-application\"}")]
    [InlineData("[]")]
    [InlineData("{")]
    [InlineData(null)]
    public async Task UnsupportedMalformedAndOversizedZipManifests_FailWithoutPasswordPrompt(string? manifest)
    {
        manifest ??= "{\"format\":\"otp-harbor-automatic-backup\",\"padding\":\"" + new string('x', 65536) + "\"}";
        var service = new ExportService(NullLogger<ExportService>.Instance);
        using var input = ZipManifest(manifest);
        var result = await service.ImportFromStreamWithFormatSelectionAsync(input, "file.otphbackup", Choose,
            (_, _) => throw new InvalidOperationException("Unexpected password prompt"), TestContext.Current.CancellationToken);
        Assert.True(result.IsFailed);
        Assert.Empty(result.Successes.OfType<AccountImportReadSummary>());
    }

    private static MemoryStream ZipManifest(string manifest)
    {
        var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        using (var writer = new StreamWriter(zip.CreateEntry("manifest.json").Open(), new UTF8Encoding(false)))
            writer.Write(manifest);
        stream.Position = 0;
        return stream;
    }

    private sealed class NonSeekableStream(byte[] data) : MemoryStream(data)
    {
        public override bool CanSeek => false;
        public override long Seek(long offset, SeekOrigin loc) => throw new NotSupportedException();
    }
}
