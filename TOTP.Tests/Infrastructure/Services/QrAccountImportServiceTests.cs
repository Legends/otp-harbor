using FluentResults;
using Moq;
using System.Text;
using TOTP.Core.Models;
using TOTP.Core.Services.Interfaces;
using TOTP.Core.Services.Models;
using TOTP.Infrastructure.Services;
using TOTP.Tests.TestData;

namespace TOTP.Tests.Infrastructure.Services;

public sealed class QrAccountImportServiceTests
{
    private const string Payload =
        "otpauth://totp/Example:alice?secret=JBSWY3DPEHPK3PXP&issuer=Example";

    [Fact]
    public async Task ImportAsync_WhenIdentityIsNew_AddsNormalizedAccountWithoutConflictPrompt()
    {
        var accounts = Manager([]);
        Account? added = null;
        accounts.Setup(value => value.AddNewAsync(It.IsAny<Account>()))
            .ReturnsAsync((Account account) =>
            {
                added = account;
                return Result.Ok();
            });
        var resolver = new Mock<
            Func<QrAccountConflict, CancellationToken, Task<QrAccountConflictDecision>>>();
        var sut = new QrAccountImportService(accounts.Object);

        var result = await sut.ImportAsync(
            Payload,
            resolver.Object,
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(QrAccountImportStatus.Added, result.Value.Status);
        Assert.Equal(added!.ID, result.Value.AccountId);
        Assert.Equal("Example", added.Issuer);
        Assert.Equal("alice", added.AccountName);
        Assert.Equal("JBSWY3DPEHPK3PXP", added.Secret);
        Assert.Equal(30, added.PeriodSeconds);
        resolver.Verify(value => value(It.IsAny<QrAccountConflict>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ImportAsync_WhenCustomPeriodIsSupported_PreservesPeriod()
    {
        var accounts = Manager([]);
        Account? added = null;
        accounts.Setup(value => value.AddNewAsync(It.IsAny<Account>()))
            .ReturnsAsync((Account account) =>
            {
                added = account;
                return Result.Ok();
            });
        var sut = new QrAccountImportService(accounts.Object);

        var result = await sut.ImportAsync(
            Payload + "&period=60",
            (_, _) => Task.FromResult(QrAccountConflictDecision.Cancel),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(60, added!.PeriodSeconds);
    }

    [Fact]
    public async Task ImportAsync_WhenOnlyPeriodDiffers_RequiresConflictDecisionAndUpdatesPeriod()
    {
        var existing = new Account(
            Guid.NewGuid(),
            "Example",
            "JBSWY3DPEHPK3PXP",
            "alice",
            30);
        var accounts = Manager([existing]);
        Account? updated = null;
        accounts.Setup(value => value.UpdateAsync(existing, It.IsAny<Account>()))
            .ReturnsAsync((Account _, Account account) =>
            {
                updated = account;
                return Result.Ok();
            });
        var sut = new QrAccountImportService(accounts.Object);

        var result = await sut.ImportAsync(
            Payload + "&period=60",
            (_, _) => Task.FromResult(QrAccountConflictDecision.UpdateExisting),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(QrAccountImportStatus.Updated, result.Value.Status);
        Assert.Equal(60, updated!.PeriodSeconds);
    }

    [Fact]
    public async Task ImportAsync_WhenExactAccountExists_DoesNotWriteOrPrompt()
    {
        var existing = Existing("JBSWY3DPEHPK3PXP");
        var accounts = Manager([existing]);
        var resolver = new Mock<
            Func<QrAccountConflict, CancellationToken, Task<QrAccountConflictDecision>>>();
        var sut = new QrAccountImportService(accounts.Object);

        var result = await sut.ImportAsync(
            Payload,
            resolver.Object,
            TestContext.Current.CancellationToken);

        Assert.Equal(QrAccountImportStatus.DuplicateUnchanged, result.Value.Status);
        accounts.Verify(value => value.AddNewAsync(It.IsAny<Account>()), Times.Never);
        accounts.Verify(value => value.UpdateAsync(It.IsAny<Account>(), It.IsAny<Account>()), Times.Never);
        resolver.Verify(value => value(It.IsAny<QrAccountConflict>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(QrAccountConflictDecision.UpdateExisting, QrAccountImportStatus.Updated)]
    [InlineData(QrAccountConflictDecision.KeepBoth, QrAccountImportStatus.KeptBoth)]
    [InlineData(QrAccountConflictDecision.Cancel, QrAccountImportStatus.Cancelled)]
    public async Task ImportAsync_WhenIdentityConflicts_AppliesExplicitDecision(
        QrAccountConflictDecision decision,
        QrAccountImportStatus expectedStatus)
    {
        var existing = Existing("KRUGS4ZANFZSAYJA");
        var accounts = Manager([existing]);
        accounts.Setup(value => value.UpdateAsync(existing, It.IsAny<Account>()))
            .ReturnsAsync(Result.Ok());
        accounts.Setup(value => value.AddNewAsync(It.IsAny<Account>()))
            .ReturnsAsync(Result.Ok());
        var sut = new QrAccountImportService(accounts.Object);

        var result = await sut.ImportAsync(
            Payload,
            (conflict, _) =>
            {
                Assert.Equal("Example", conflict.Issuer);
                Assert.Equal("alice", conflict.AccountName);
                return Task.FromResult(decision);
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(expectedStatus, result.Value.Status);
        accounts.Verify(
            value => value.UpdateAsync(existing, It.Is<Account>(account => account.ID == existing.ID)),
            decision == QrAccountConflictDecision.UpdateExisting ? Times.Once : Times.Never);
        accounts.Verify(
            value => value.AddNewAsync(It.IsAny<Account>()),
            decision == QrAccountConflictDecision.KeepBoth ? Times.Once : Times.Never);
    }

    [Fact]
    public async Task ImportAsync_WhenPayloadIsInvalid_FailsBeforeAccountAccess()
    {
        var accounts = new Mock<IAccountManager>();
        var sut = new QrAccountImportService(accounts.Object);

        var result = await sut.ImportAsync(
            "otpauth://totp/Example:alice?secret=NOT*BASE32",
            (_, _) => Task.FromResult(QrAccountConflictDecision.Cancel),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsFailed);
        accounts.Verify(value => value.GetAllOtpEntriesSortedAsync(), Times.Never);
    }

    [Fact]
    public async Task ImportAsync_WhenParametersAreUnsupported_FailsBeforeAccountAccess()
    {
        var accounts = new Mock<IAccountManager>();
        var sut = new QrAccountImportService(accounts.Object);

        var result = await sut.ImportAsync(
            Payload + "&digits=8",
            (_, _) => Task.FromResult(QrAccountConflictDecision.Cancel),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsFailed);
        accounts.Verify(value => value.GetAllOtpEntriesSortedAsync(), Times.Never);
    }

    [Fact]
    public async Task ImportAsync_WhenGoogleMigrationContainsMultipleAccounts_BacksUpAndImportsAll()
    {
        var accounts = Manager([]);
        accounts.Setup(value => value.BackupOtpEntriesStorageFileAsync()).ReturnsAsync(Result.Ok());
        var added = new List<Account>();
        accounts.Setup(value => value.AddNewAsync(It.IsAny<Account>()))
            .ReturnsAsync((Account account) =>
            {
                added.Add(account);
                return Result.Ok();
            });
        var sut = new QrAccountImportService(accounts.Object);
        var payload = MigrationPayload(
            [
                new MigrationAccount(
                    Convert.FromHexString("48656C6C6F21DEADBEEF"),
                    "Example:alice@example.com",
                    "Example"),
                new MigrationAccount(
                    Convert.FromHexString("3132333435363738393031323334353637383930"),
                    "bob@example.com",
                    "Work")
            ],
            batchSize: 2,
            batchIndex: 0);

        var result = await sut.ImportAsync(
            payload,
            (_, _) => Task.FromResult(QrAccountConflictDecision.Cancel),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(QrAccountImportStatus.BulkImported, result.Value.Status);
        Assert.Equal(2, result.Value.TotalCount);
        Assert.Equal(2, result.Value.ImportedCount);
        Assert.Equal(0, result.Value.DuplicateCount);
        Assert.True(result.Value.HasMoreBatches);
        Assert.Collection(
            added,
            account =>
            {
                Assert.Equal("Example", account.Issuer);
                Assert.Equal("alice@example.com", account.AccountName);
                Assert.Equal("JBSWY3DPEHPK3PXP", account.Secret);
            },
            account =>
            {
                Assert.Equal("Work", account.Issuer);
                Assert.Equal("bob@example.com", account.AccountName);
                Assert.Equal("GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ", account.Secret);
            });
        accounts.Verify(value => value.BackupOtpEntriesStorageFileAsync(), Times.Once);
    }

    [Fact]
    public async Task ImportAsync_WhenGoogleMigrationContainsTenAccountsAndShortSecrets_ImportsAll()
    {
        var accounts = Manager([]);
        accounts.Setup(value => value.BackupOtpEntriesStorageFileAsync()).ReturnsAsync(Result.Ok());
        var added = new List<Account>();
        accounts.Setup(value => value.AddNewAsync(It.IsAny<Account>()))
            .ReturnsAsync((Account account) =>
            {
                added.Add(account);
                return Result.Ok();
            });
        var sut = new QrAccountImportService(accounts.Object);

        var result = await sut.ImportAsync(
            GoogleAuthenticatorMigrationTestData.TenAccountPayload,
            (_, _) => Task.FromResult(QrAccountConflictDecision.Cancel),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(QrAccountImportStatus.BulkImported, result.Value.Status);
        Assert.Equal(10, result.Value.TotalCount);
        Assert.Equal(10, result.Value.ImportedCount);
        Assert.Equal(10, added.Count);
        Assert.Contains(added, account => account.Issuer == "München UTF8 Test");
    }

    [Fact]
    public async Task ImportAsync_WhenGoogleMigrationConflictIsCancelled_PerformsNoWrites()
    {
        var existing = Existing("KRUGS4ZANFZSAYJA");
        var accounts = Manager([existing]);
        var sut = new QrAccountImportService(accounts.Object);
        var payload = MigrationPayload(
            [
                new MigrationAccount(
                    Convert.FromHexString("3132333435363738393031323334353637383930"),
                    "new@example.com",
                    "Work"),
                new MigrationAccount(
                    Convert.FromHexString("48656C6C6F21DEADBEEF"),
                    "Example:alice",
                    "Example")
            ]);

        var result = await sut.ImportAsync(
            payload,
            (_, _) => Task.FromResult(QrAccountConflictDecision.Cancel),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(QrAccountImportStatus.Cancelled, result.Value.Status);
        accounts.Verify(value => value.BackupOtpEntriesStorageFileAsync(), Times.Never);
        accounts.Verify(value => value.AddNewAsync(It.IsAny<Account>()), Times.Never);
        accounts.Verify(value => value.UpdateAsync(It.IsAny<Account>(), It.IsAny<Account>()), Times.Never);
    }

    [Fact]
    public async Task ImportAsync_WhenGoogleMigrationContainsUnsupportedHotp_FailsBeforeAccountAccess()
    {
        var accounts = new Mock<IAccountManager>();
        var sut = new QrAccountImportService(accounts.Object);
        var payload = MigrationPayload(
            [new MigrationAccount(
                Convert.FromHexString("48656C6C6F21DEADBEEF"),
                "alice",
                "Example",
                Type: 1)]);

        var result = await sut.ImportAsync(
            payload,
            (_, _) => Task.FromResult(QrAccountConflictDecision.Cancel),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsFailed);
        accounts.Verify(value => value.GetAllOtpEntriesSortedAsync(), Times.Never);
    }

    [Fact]
    public async Task ImportAsync_WhenGoogleMigrationWasAlreadyImported_RemainsIdempotent()
    {
        var existing = Existing("JBSWY3DPEHPK3PXP");
        var accounts = Manager([existing]);
        var sut = new QrAccountImportService(accounts.Object);
        var payload = MigrationPayload(
            [new MigrationAccount(
                Convert.FromHexString("48656C6C6F21DEADBEEF"),
                "Example:alice",
                "Example")]);

        var result = await sut.ImportAsync(
            payload,
            (_, _) => Task.FromResult(QrAccountConflictDecision.Cancel),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(QrAccountImportStatus.BulkImported, result.Value.Status);
        Assert.Equal(0, result.Value.ImportedCount);
        Assert.Equal(1, result.Value.DuplicateCount);
        accounts.Verify(value => value.BackupOtpEntriesStorageFileAsync(), Times.Never);
        accounts.Verify(value => value.AddNewAsync(It.IsAny<Account>()), Times.Never);
        accounts.Verify(value => value.UpdateAsync(It.IsAny<Account>(), It.IsAny<Account>()), Times.Never);
    }

    private static Mock<IAccountManager> Manager(IReadOnlyList<Account> existing)
    {
        var accounts = new Mock<IAccountManager>();
        accounts.Setup(value => value.GetAllOtpEntriesSortedAsync())
            .ReturnsAsync(Result.Ok(existing));
        return accounts;
    }

    private static Account Existing(string secret) =>
        new(Guid.NewGuid(), "Example", secret, "alice");

    private sealed record MigrationAccount(
        byte[] Secret,
        string Name,
        string Issuer,
        int Algorithm = 1,
        int Digits = 1,
        int Type = 2);

    private static string MigrationPayload(
        IReadOnlyList<MigrationAccount> entries,
        int batchSize = 1,
        int batchIndex = 0,
        int batchId = 42)
    {
        var payload = new List<byte>();
        foreach (var entry in entries)
            WriteBytes(payload, 1, EncodeAccount(entry));
        WriteVarintField(payload, 2, 1);
        WriteVarintField(payload, 3, batchSize);
        WriteVarintField(payload, 4, batchIndex);
        WriteVarintField(payload, 5, batchId);
        return "otpauth-migration://offline?data="
               + Uri.EscapeDataString(Convert.ToBase64String(payload.ToArray()));
    }

    private static byte[] EncodeAccount(MigrationAccount account)
    {
        var payload = new List<byte>();
        WriteBytes(payload, 1, account.Secret);
        WriteBytes(payload, 2, Encoding.UTF8.GetBytes(account.Name));
        WriteBytes(payload, 3, Encoding.UTF8.GetBytes(account.Issuer));
        WriteVarintField(payload, 4, account.Algorithm);
        WriteVarintField(payload, 5, account.Digits);
        WriteVarintField(payload, 6, account.Type);
        return payload.ToArray();
    }

    private static void WriteBytes(List<byte> target, int field, byte[] value)
    {
        WriteVarint(target, (ulong)((field << 3) | 2));
        WriteVarint(target, (ulong)value.Length);
        target.AddRange(value);
    }

    private static void WriteVarintField(List<byte> target, int field, int value)
    {
        WriteVarint(target, (ulong)(field << 3));
        WriteVarint(target, (ulong)value);
    }

    private static void WriteVarint(List<byte> target, ulong value)
    {
        while (value >= 0x80)
        {
            target.Add((byte)(value | 0x80));
            value >>= 7;
        }

        target.Add((byte)value);
    }
}
