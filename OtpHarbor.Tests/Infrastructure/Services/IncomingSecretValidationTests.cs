using FluentResults;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TOTP.Core.Models;
using TOTP.Core.Services.Interfaces;
using TOTP.Core.Services.Models;
using TOTP.Infrastructure.Parser;
using TOTP.Infrastructure.Services;

namespace TOTP.Tests.Infrastructure.Services;

public sealed class IncomingSecretValidationTests
{
    public static TheoryData<string> ValidSecrets => new()
    {
        "AA", "ORSXG5A", "ANYTHING", "JBSWY3DPEHPK3PXP", "jbsw y3dp-ehpk 3pxp====",
        "TEST", "ASDF", "FOO", "JBSWY3DPEHPK3PXPZ", " te-st== "
    };

    public static TheoryData<string> InvalidSecrets => new()
    {
        "", " ", "A", "====", "not*base32", "ABC0189", "AB=CD"
    };

    [Theory]
    [MemberData(nameof(ValidSecrets))]
    public async Task EveryIncomingFormat_AcceptsShortValidBase32(string secret)
    {
        var normalized = TOTP.Core.Validation.SecretValidation.NormalizeBase32Secret(secret);
        var parsed = OtpauthParser.Parse("otpauth://totp/Example?secret=" + Uri.EscapeDataString(secret));
        Assert.Equal(normalized, parsed.SecretBase32);
        Assert.True(new QrPayloadValidator().Validate("otpauth://totp/Example?secret=" + Uri.EscapeDataString(secret)).IsValid);

        var source = new Account(Guid.NewGuid(), "Example", secret);
        var service = new ExportService(NullLogger<ExportService>.Instance);
        foreach (var format in new[] { ExportFileFormat.Json, ExportFileFormat.Csv, ExportFileFormat.Txt })
        {
            await using var stream = new MemoryStream();
            Assert.True((await service.ExportToStreamAsync([source], stream, format, TestContext.Current.CancellationToken)).IsSuccess);
            stream.Position = 0;
            var imported = await service.ImportFromStreamAsync(stream, "fixture." + format.ToString().ToLowerInvariant(), cancellationToken: TestContext.Current.CancellationToken);
            Assert.True(imported.IsSuccess);
            Assert.Equal(secret, Assert.Single(imported.Value).Secret);
        }
        await using var backup = new MemoryStream();
        Assert.True((await service.ExportToEncryptedStreamAsync([source], "synthetic password", backup, ExportFileFormat.Json, TestContext.Current.CancellationToken)).IsSuccess);
        backup.Position = 0;
        var restored = await service.ImportFromStreamAsync(backup, "backup.otphbp", "synthetic password", TestContext.Current.CancellationToken);
        Assert.True(restored.IsSuccess);
        Assert.Equal(secret, Assert.Single(restored.Value).Secret);
        backup.Position = 0;
        var legacyRestored = await service.ImportFromStreamAsync(backup, "backup.totp", "synthetic password", TestContext.Current.CancellationToken);
        Assert.True(legacyRestored.IsSuccess);
        Assert.Equal(secret, Assert.Single(legacyRestored.Value).Secret);
    }

    [Theory]
    [MemberData(nameof(InvalidSecrets))]
    public async Task InvalidBase32_FailsAtUriNativeFileAndRestoreBoundaries(string secret)
    {
        var uri = "otpauth://totp/Example?secret=" + Uri.EscapeDataString(secret);
        Assert.Throws<ArgumentException>(() => OtpauthParser.Parse(uri));
        Assert.False(new QrPayloadValidator().Validate(uri).IsValid);
        var manager = new Mock<IAccountManager>(MockBehavior.Strict);
        var qr = await new QrAccountImportService(manager.Object).ImportAsync(uri,
            (_, _) => Task.FromResult(QrAccountConflictDecision.Cancel), TestContext.Current.CancellationToken);
        Assert.True(qr.IsFailed);
        var source = new Account(Guid.NewGuid(), "Example", secret);
        var importer = new AccountImportService(manager.Object);
        var imported = await importer.ImportAsync([source], ImportConflictStrategy.SkipExisting,
            (_, _) => Task.FromResult(true), TestContext.Current.CancellationToken);
        var restored = await importer.RestoreBackupAsync([source], ImportConflictStrategy.ReplaceExisting,
            (_, _) => Task.FromResult(true), TestContext.Current.CancellationToken);
        Assert.Equal(AccountImportStatus.InvalidTargets, imported.Value.Status);
        Assert.Equal(AccountImportStatus.InvalidTargets, restored.Value.Status);
        manager.VerifyNoOtherCalls();

        var exporter = new ExportService(NullLogger<ExportService>.Instance);
        foreach (var format in new[] { ExportFileFormat.Json, ExportFileFormat.Csv, ExportFileFormat.Txt })
        {
            await using var stream = new MemoryStream();
            Assert.True((await exporter.ExportToStreamAsync([source], stream, format, TestContext.Current.CancellationToken)).IsSuccess);
            stream.Position = 0;
            var result = await exporter.ImportFromStreamAsync(stream, "invalid." + format.ToString().ToLowerInvariant(), cancellationToken: TestContext.Current.CancellationToken);
            Assert.True(result.IsFailed);
        }
    }

    [Theory]
    [MemberData(nameof(ValidSecrets))]
    public async Task ManualQrAndBackupRestore_UseSameIncomingPolicy(string secret)
    {
        var dal = new Mock<IAccountDAL>();
        dal.Setup(value => value.AddNewAsync(It.IsAny<Account>())).ReturnsAsync(Result.Ok());
        var result = await new AccountManager(dal.Object).AddNewAsync(new Account(Guid.NewGuid(), "Example", secret));
        Assert.True(result.IsSuccess);

        var manager = new Mock<IAccountManager>();
        manager.Setup(value => value.GetAllOtpEntriesSortedAsync()).ReturnsAsync(Result.Ok<IReadOnlyList<Account>>([]));
        manager.Setup(value => value.AddNewAsync(It.IsAny<Account>())).ReturnsAsync(Result.Ok());
        var uri = "otpauth://totp/Example?secret=" + Uri.EscapeDataString(secret);
        Assert.True((await new QrAccountImportService(manager.Object).ImportAsync(uri,
            (_, _) => Task.FromResult(QrAccountConflictDecision.Cancel), TestContext.Current.CancellationToken)).IsSuccess);

        manager.Setup(value => value.BackupOtpEntriesStorageFileAsync()).ReturnsAsync(Result.Ok());
        manager.Setup(value => value.CommitImportAsync(It.IsAny<IReadOnlyCollection<Account>>())).ReturnsAsync(Result.Ok());
        var restored = await new AccountImportService(manager.Object).RestoreBackupAsync(
            [new Account(Guid.NewGuid(), "Example", secret)], ImportConflictStrategy.ReplaceExisting,
            (_, _) => Task.FromResult(true), TestContext.Current.CancellationToken);
        Assert.Equal(AccountImportStatus.Completed, restored.Value.Status);
    }

    [Theory]
    [MemberData(nameof(InvalidSecrets))]
    public async Task AccountMutations_RejectMalformedNewOrReplacementSecrets(string secret)
    {
        var dal = new Mock<IAccountDAL>(MockBehavior.Strict);
        var service = new AccountManager(dal.Object);
        var previous = new Account(Guid.NewGuid(), "Example", "JBSWY3DPEHPK3PXP");
        var replacement = new Account(previous.ID, "Example", secret);
        Assert.True((await service.AddNewAsync(replacement)).IsFailed);
        Assert.True((await service.UpdateAsync(previous, replacement)).IsFailed);
        dal.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task UnchangedStoredLegacySecret_RemainsUsableWithoutRewriting()
    {
        var previous = new Account(Guid.NewGuid(), "Example", "TEST");
        var updated = new Account(previous.ID, "Renamed", previous.Secret, algorithm: "SHA512", digits: 8);
        var dal = new Mock<IAccountDAL>();
        dal.Setup(value => value.UpdateAsync(It.IsAny<Account>())).ReturnsAsync(Result.Ok());
        Assert.True((await new AccountManager(dal.Object).UpdateAsync(previous, updated)).IsSuccess);
        dal.Verify(value => value.UpdateAsync(It.Is<Account>(account => account.Secret == previous.Secret)), Times.Once);
        Assert.Equal(8, new OtpNetTotpGenerator().Generate(updated.Secret, 30, updated.Algorithm, updated.Digits).Code.Length);
    }

    [Theory]
    [MemberData(nameof(TotpConfigurationTests.Configurations), MemberType = typeof(TotpConfigurationTests))]
    public async Task DecoderCompatibleLegacyBackup_RestoresSameSeedAndCode(string algorithm, int digits)
    {
        const string secret = "TEST";
        var source = new Account(Guid.NewGuid(), "Example", secret, algorithm: algorithm, digits: digits);
        var generator = new OtpNetTotpGenerator(new TotpConfigurationTests.FixedClock(1111111109));
        var before = generator.Generate(secret, 30, algorithm, digits);
        var exporter = new ExportService(NullLogger<ExportService>.Instance);
        await using var backup = new MemoryStream();
        Assert.True((await exporter.ExportToEncryptedStreamAsync([source], "synthetic password", backup,
            ExportFileFormat.Json, TestContext.Current.CancellationToken)).IsSuccess);
        backup.Position = 0;
        var decoded = await exporter.ImportFromStreamAsync(backup, "old-backup.totp", "synthetic password", TestContext.Current.CancellationToken);
        Assert.True(decoded.IsSuccess);

        var manager = new Mock<IAccountManager>();
        manager.Setup(value => value.GetAllOtpEntriesSortedAsync()).ReturnsAsync(Result.Ok<IReadOnlyList<Account>>([]));
        manager.Setup(value => value.BackupOtpEntriesStorageFileAsync()).ReturnsAsync(Result.Ok());
        Account? committed = null;
        manager.Setup(value => value.CommitImportAsync(It.IsAny<IReadOnlyCollection<Account>>()))
            .Callback<IReadOnlyCollection<Account>>(accounts => committed = Assert.Single(accounts)).ReturnsAsync(Result.Ok());
        var result = await new AccountImportService(manager.Object).RestoreBackupAsync(decoded.Value,
            ImportConflictStrategy.ReplaceExisting, (_, _) => Task.FromResult(true), TestContext.Current.CancellationToken);
        Assert.Equal(AccountImportStatus.Completed, result.Value.Status);
        Assert.NotNull(committed);
        Assert.Equal(secret, committed.Secret);
        Assert.Equal(algorithm, committed.Algorithm);
        Assert.Equal(digits, committed.Digits);
        Assert.Equal(before, generator.Generate(committed.Secret, committed.PeriodSeconds, committed.Algorithm, committed.Digits));
    }
}
