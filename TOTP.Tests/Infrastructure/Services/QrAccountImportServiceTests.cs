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
    public async Task ImportAsync_WhenIssuerIsAbsent_PreservesLabelAsIssuer()
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
            "otpauth://totp/user@host?secret=JBSWY3DPEHPK3PXP",
            (_, _) => Task.FromResult(QrAccountConflictDecision.Cancel),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal("user@host", added!.Issuer);
        Assert.Null(added.AccountName);
        Assert.Equal("JBSWY3DPEHPK3PXP", added.Secret);
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
            30,
            isFavorite: true);
        var accounts = Manager([existing]);
        accounts.Setup(value => value.BackupOtpEntriesStorageFileAsync()).ReturnsAsync(Result.Ok());
        IReadOnlyCollection<Account>? committed = null;
        accounts.Setup(value => value.CommitImportAsync(It.IsAny<IReadOnlyCollection<Account>>()))
            .ReturnsAsync((IReadOnlyCollection<Account> snapshot) =>
            {
                committed = snapshot;
                return Result.Ok();
            });
        var sut = new QrAccountImportService(accounts.Object);

        var result = await sut.ImportAsync(
            Payload + "&period=60",
            (_, _) => Task.FromResult(QrAccountConflictDecision.UpdateExisting),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(QrAccountImportStatus.Updated, result.Value.Status);
        var updated = Assert.Single(committed!);
        Assert.Equal(60, updated!.PeriodSeconds);
        Assert.True(updated.IsFavorite);
        accounts.Verify(value => value.BackupOtpEntriesStorageFileAsync(), Times.Once);
        accounts.Verify(
            value => value.CommitImportAsync(It.IsAny<IReadOnlyCollection<Account>>()),
            Times.Once);
        accounts.Verify(
            value => value.UpdateAsync(It.IsAny<Account>(), It.IsAny<Account>()),
            Times.Never);
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
        accounts.Setup(value => value.BackupOtpEntriesStorageFileAsync()).ReturnsAsync(Result.Ok());
        IReadOnlyCollection<Account>? committed = null;
        accounts.Setup(value => value.CommitImportAsync(It.IsAny<IReadOnlyCollection<Account>>()))
            .ReturnsAsync((IReadOnlyCollection<Account> snapshot) =>
            {
                committed = snapshot;
                return Result.Ok();
            });
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
        if (decision == QrAccountConflictDecision.UpdateExisting)
        {
            var updated = Assert.Single(committed!);
            Assert.Equal(existing.ID, updated.ID);
            Assert.Equal("JBSWY3DPEHPK3PXP", updated.Secret);
        }
        else if (decision == QrAccountConflictDecision.KeepBoth)
        {
            Assert.Equal(2, committed!.Count);
            Assert.Contains(committed, account => account.ID == existing.ID);
            Assert.Contains(committed, account => account.ID != existing.ID);
        }
        else
        {
            Assert.Null(committed);
        }

        accounts.Verify(
            value => value.CommitImportAsync(It.IsAny<IReadOnlyCollection<Account>>()),
            decision == QrAccountConflictDecision.Cancel ? Times.Never : Times.Once);
        accounts.Verify(
            value => value.UpdateAsync(It.IsAny<Account>(), It.IsAny<Account>()),
            Times.Never);
        accounts.Verify(value => value.AddNewAsync(It.IsAny<Account>()), Times.Never);
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
        IReadOnlyCollection<Account>? committed = null;
        accounts.Setup(value => value.CommitImportAsync(It.IsAny<IReadOnlyCollection<Account>>()))
            .ReturnsAsync((IReadOnlyCollection<Account> snapshot) =>
            {
                committed = snapshot;
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
            batchIndex: 0,
            version: 2);

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
            committed!,
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
        accounts.Verify(
            value => value.CommitImportAsync(It.IsAny<IReadOnlyCollection<Account>>()),
            Times.Once);
        accounts.Verify(value => value.AddNewAsync(It.IsAny<Account>()), Times.Never);
        accounts.Verify(
            value => value.UpdateAsync(It.IsAny<Account>(), It.IsAny<Account>()),
            Times.Never);
    }

    [Fact]
    public async Task ImportWithConflictResolutionAsync_PreviewsEntireMigrationAndAppliesSkipAtomically()
    {
        var unchanged = Existing("JBSWY3DPEHPK3PXP");
        var changed = new Account(
            Guid.NewGuid(),
            "Work",
            "KRUGS4ZANFZSAYJA",
            "bob@example.com",
            isFavorite: true);
        var accounts = Manager([unchanged, changed]);
        accounts.Setup(value => value.BackupOtpEntriesStorageFileAsync()).ReturnsAsync(Result.Ok());
        IReadOnlyCollection<Account>? committed = null;
        accounts.Setup(value => value.CommitImportAsync(It.IsAny<IReadOnlyCollection<Account>>()))
            .ReturnsAsync((IReadOnlyCollection<Account> snapshot) =>
            {
                committed = snapshot;
                return Result.Ok();
            });
        var sut = new QrAccountImportService(accounts.Object);
        var payload = MigrationPayload(
            [
                new MigrationAccount(
                    Convert.FromHexString("48656C6C6F21DEADBEEF"),
                    "Example:alice",
                    "Example"),
                new MigrationAccount(
                    Convert.FromHexString("3132333435363738393031323334353637383930"),
                    "carol@example.com",
                    "New"),
                new MigrationAccount(
                    Convert.FromHexString("48656C6C6F21DEADBEEF"),
                    "bob@example.com",
                    "Work")
            ]);

        var result = await sut.ImportWithConflictResolutionAsync(
            payload,
            (preview, _) =>
            {
                Assert.Equal(3, preview.TotalCount);
                Assert.Equal(1, preview.NewCount);
                Assert.Equal(1, preview.UnchangedCount);
                var conflict = Assert.Single(preview.ChangedConflicts);
                Assert.Equal(2, conflict.ImportIndex);
                Assert.True(conflict.SecretChanged);
                Assert.False(conflict.PeriodChanged);
                return Task.FromResult<QrAccountImportResolution?>(new(
                    [new QrAccountImportConflictResolution(
                        conflict.ImportIndex,
                        QrAccountConflictDecision.Skip)]));
            },
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.AddedCount);
        Assert.Equal(1, result.Value.DuplicateCount);
        Assert.Equal(1, result.Value.SkippedCount);
        Assert.Equal(0, result.Value.UpdatedCount);
        Assert.Equal(3, committed!.Count);
        Assert.Contains(committed, account => account.Issuer == "New");
        Assert.Contains(committed, account =>
            account.ID == changed.ID
            && account.Secret == changed.Secret
            && account.IsFavorite);
        accounts.Verify(value => value.BackupOtpEntriesStorageFileAsync(), Times.Once);
        accounts.Verify(
            value => value.CommitImportAsync(It.IsAny<IReadOnlyCollection<Account>>()),
            Times.Once);
        accounts.Verify(value => value.AddNewAsync(It.IsAny<Account>()), Times.Never);
        accounts.Verify(
            value => value.UpdateAsync(It.IsAny<Account>(), It.IsAny<Account>()),
            Times.Never);
    }

    [Fact]
    public async Task ImportWithConflictResolutionAsync_WhenReviewIsCancelled_PerformsNoWrites()
    {
        var accounts = Manager([]);
        var sut = new QrAccountImportService(accounts.Object);
        var payload = MigrationPayload(
            [new MigrationAccount(
                Convert.FromHexString("48656C6C6F21DEADBEEF"),
                "Example:alice",
                "Example")]);

        var result = await sut.ImportWithConflictResolutionAsync(
            payload,
            (_, _) => Task.FromResult<QrAccountImportResolution?>(null),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(QrAccountImportStatus.Cancelled, result.Value.Status);
        accounts.Verify(value => value.BackupOtpEntriesStorageFileAsync(), Times.Never);
        accounts.Verify(
            value => value.CommitImportAsync(It.IsAny<IReadOnlyCollection<Account>>()),
            Times.Never);
        accounts.Verify(value => value.AddNewAsync(It.IsAny<Account>()), Times.Never);
        accounts.Verify(
            value => value.UpdateAsync(It.IsAny<Account>(), It.IsAny<Account>()),
            Times.Never);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task ImportWithConflictResolutionAsync_ReportsEveryChangedFieldCombination(
        bool secretChanged,
        bool periodChanged)
    {
        var existing = new Account(
            Guid.NewGuid(),
            "Example",
            secretChanged ? "KRUGS4ZANFZSAYJA" : "JBSWY3DPEHPK3PXP",
            "alice",
            periodChanged ? 60 : 30);
        var accounts = Manager([existing]);
        var sut = new QrAccountImportService(accounts.Object);

        var result = await sut.ImportWithConflictResolutionAsync(
            Payload,
            (preview, _) =>
            {
                var conflict = Assert.Single(preview.ChangedConflicts);
                Assert.Equal(0, conflict.ImportIndex);
                Assert.Equal("Example", conflict.Issuer);
                Assert.Equal("alice", conflict.AccountName);
                Assert.Equal(secretChanged, conflict.SecretChanged);
                Assert.Equal(periodChanged, conflict.PeriodChanged);
                return Task.FromResult<QrAccountImportResolution?>(new(
                    [new(0, QrAccountConflictDecision.Skip)]));
            },
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(QrAccountImportStatus.Skipped, result.Value.Status);
        Assert.Equal(1, result.Value.SkippedCount);
        accounts.Verify(value => value.BackupOtpEntriesStorageFileAsync(), Times.Never);
        accounts.Verify(
            value => value.CommitImportAsync(It.IsAny<IReadOnlyCollection<Account>>()),
            Times.Never);
    }

    [Theory]
    [InlineData(QrAccountConflictDecision.Skip, QrAccountImportStatus.Skipped, 1)]
    [InlineData(QrAccountConflictDecision.UpdateExisting, QrAccountImportStatus.Updated, 1)]
    [InlineData(QrAccountConflictDecision.KeepBoth, QrAccountImportStatus.KeptBoth, 2)]
    public async Task ImportWithConflictResolutionAsync_AppliesEveryConflictDecision(
        QrAccountConflictDecision decision,
        QrAccountImportStatus expectedStatus,
        int expectedAccountCount)
    {
        var existing = Existing("KRUGS4ZANFZSAYJA");
        var accounts = Manager([existing]);
        accounts.Setup(value => value.BackupOtpEntriesStorageFileAsync()).ReturnsAsync(Result.Ok());
        IReadOnlyCollection<Account>? committed = null;
        accounts.Setup(value => value.CommitImportAsync(It.IsAny<IReadOnlyCollection<Account>>()))
            .ReturnsAsync((IReadOnlyCollection<Account> snapshot) =>
            {
                committed = snapshot;
                return Result.Ok();
            });
        var sut = new QrAccountImportService(accounts.Object);

        var result = await sut.ImportWithConflictResolutionAsync(
            Payload,
            (preview, _) => Task.FromResult<QrAccountImportResolution?>(new(
                [new(Assert.Single(preview.ChangedConflicts).ImportIndex, decision)])),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(expectedStatus, result.Value.Status);
        if (decision == QrAccountConflictDecision.Skip)
        {
            Assert.Null(committed);
            Assert.Equal(1, result.Value.SkippedCount);
        }
        else
        {
            Assert.Equal(expectedAccountCount, committed!.Count);
            accounts.Verify(value => value.BackupOtpEntriesStorageFileAsync(), Times.Once);
            accounts.Verify(
                value => value.CommitImportAsync(It.IsAny<IReadOnlyCollection<Account>>()),
                Times.Once);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task ImportWithConflictResolutionAsync_RejectsInvalidConflictResolution(
        int invalidResolutionKind)
    {
        var accounts = Manager([Existing("KRUGS4ZANFZSAYJA")]);
        var sut = new QrAccountImportService(accounts.Object);

        var result = await sut.ImportWithConflictResolutionAsync(
            Payload,
            (_, _) => Task.FromResult<QrAccountImportResolution?>(new(
                invalidResolutionKind switch
                {
                    0 => [],
                    1 => [new(99, QrAccountConflictDecision.Skip)],
                    2 =>
                    [
                        new(0, QrAccountConflictDecision.Skip),
                        new(0, QrAccountConflictDecision.UpdateExisting)
                    ],
                    3 => [new(0, QrAccountConflictDecision.Cancel)],
                    _ =>
                    [
                        new(0, QrAccountConflictDecision.Skip),
                        new(99, QrAccountConflictDecision.Skip)
                    ]
                })),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsFailed);
        Assert.Contains(
            result.Errors,
            error => error.Message == "The QR import conflict resolution is invalid.");
        accounts.Verify(value => value.BackupOtpEntriesStorageFileAsync(), Times.Never);
        accounts.Verify(
            value => value.CommitImportAsync(It.IsAny<IReadOnlyCollection<Account>>()),
            Times.Never);
    }

    [Fact]
    public async Task ImportWithConflictResolutionAsync_MixedMigrationAppliesAllChoicesOnce()
    {
        var unchanged = Existing("JBSWY3DPEHPK3PXP");
        var skip = new Account(Guid.NewGuid(), "Skip", "KRUGS4ZANFZSAYJA", "one", 30);
        var update = new Account(
            Guid.NewGuid(),
            "Update",
            "KRUGS4ZANFZSAYJA",
            "two",
            60,
            isFavorite: true);
        var keepBoth = new Account(Guid.NewGuid(), "Keep", "KRUGS4ZANFZSAYJA", "three", 30);
        var accounts = Manager([unchanged, skip, update, keepBoth]);
        accounts.Setup(value => value.BackupOtpEntriesStorageFileAsync()).ReturnsAsync(Result.Ok());
        IReadOnlyCollection<Account>? committed = null;
        accounts.Setup(value => value.CommitImportAsync(It.IsAny<IReadOnlyCollection<Account>>()))
            .ReturnsAsync((IReadOnlyCollection<Account> snapshot) =>
            {
                committed = snapshot;
                return Result.Ok();
            });
        var sut = new QrAccountImportService(accounts.Object);
        var payload = MigrationPayload(
        [
            Migration("Example:alice", "Example", "48656C6C6F21DEADBEEF"),
            Migration("new", "New", "3132333435363738393031323334353637383930"),
            Migration("one", "Skip", "48656C6C6F21DEADBEEF"),
            Migration("two", "Update", "48656C6C6F21DEADBEEF"),
            Migration("three", "Keep", "48656C6C6F21DEADBEEF")
        ]);

        var result = await sut.ImportWithConflictResolutionAsync(
            payload,
            (preview, _) =>
            {
                Assert.Equal(5, preview.TotalCount);
                Assert.Equal(1, preview.NewCount);
                Assert.Equal(1, preview.UnchangedCount);
                Assert.Equal(3, preview.ChangedConflicts.Count);
                return Task.FromResult<QrAccountImportResolution?>(new(
                [
                    new(2, QrAccountConflictDecision.Skip),
                    new(3, QrAccountConflictDecision.UpdateExisting),
                    new(4, QrAccountConflictDecision.KeepBoth)
                ]));
            },
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.AddedCount);
        Assert.Equal(1, result.Value.UpdatedCount);
        Assert.Equal(1, result.Value.KeptBothCount);
        Assert.Equal(1, result.Value.DuplicateCount);
        Assert.Equal(1, result.Value.SkippedCount);
        Assert.Equal(6, committed!.Count);
        Assert.Contains(committed, account => account.ID == skip.ID && account.Secret == skip.Secret);
        Assert.Contains(committed, account =>
            account.ID == update.ID
            && account.Secret == "JBSWY3DPEHPK3PXP"
            && account.PeriodSeconds == 30
            && account.IsFavorite);
        Assert.Equal(2, committed.Count(account => account.Issuer == "Keep"));
        accounts.Verify(value => value.BackupOtpEntriesStorageFileAsync(), Times.Once);
        accounts.Verify(
            value => value.CommitImportAsync(It.IsAny<IReadOnlyCollection<Account>>()),
            Times.Once);
    }

    [Fact]
    public async Task ImportWithConflictResolutionAsync_AllUnchangedOrSkipped_PerformsNoWrites()
    {
        var unchanged = Existing("JBSWY3DPEHPK3PXP");
        var changed = new Account(Guid.NewGuid(), "Work", "KRUGS4ZANFZSAYJA", "bob", 30);
        var accounts = Manager([unchanged, changed]);
        var sut = new QrAccountImportService(accounts.Object);
        var payload = MigrationPayload(
        [
            Migration("Example:alice", "Example", "48656C6C6F21DEADBEEF"),
            Migration("bob", "Work", "48656C6C6F21DEADBEEF")
        ]);

        var result = await sut.ImportWithConflictResolutionAsync(
            payload,
            (preview, _) => Task.FromResult<QrAccountImportResolution?>(new(
                [new(Assert.Single(preview.ChangedConflicts).ImportIndex, QrAccountConflictDecision.Skip)])),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.DuplicateCount);
        Assert.Equal(1, result.Value.SkippedCount);
        Assert.Equal(0, result.Value.ImportedCount);
        accounts.Verify(value => value.BackupOtpEntriesStorageFileAsync(), Times.Never);
        accounts.Verify(
            value => value.CommitImportAsync(It.IsAny<IReadOnlyCollection<Account>>()),
            Times.Never);
    }

    [Fact]
    public async Task ImportWithConflictResolutionAsync_DuplicateIncomingIdentities_AreReviewedAgainstStagedAccounts()
    {
        var accounts = Manager([]);
        accounts.Setup(value => value.BackupOtpEntriesStorageFileAsync()).ReturnsAsync(Result.Ok());
        IReadOnlyCollection<Account>? committed = null;
        accounts.Setup(value => value.CommitImportAsync(It.IsAny<IReadOnlyCollection<Account>>()))
            .ReturnsAsync((IReadOnlyCollection<Account> snapshot) =>
            {
                committed = snapshot;
                return Result.Ok();
            });
        var sut = new QrAccountImportService(accounts.Object);
        var payload = MigrationPayload(
        [
            Migration("alice", "Example", "48656C6C6F21DEADBEEF"),
            Migration("alice", "Example", "3132333435363738393031323334353637383930")
        ]);

        var result = await sut.ImportWithConflictResolutionAsync(
            payload,
            (preview, _) =>
            {
                Assert.Equal(1, preview.NewCount);
                var conflict = Assert.Single(preview.ChangedConflicts);
                Assert.Equal(1, conflict.ImportIndex);
                return Task.FromResult<QrAccountImportResolution?>(new(
                    [new(1, QrAccountConflictDecision.Skip)]));
            },
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.AddedCount);
        Assert.Equal(1, result.Value.SkippedCount);
        var imported = Assert.Single(committed!);
        Assert.Equal("JBSWY3DPEHPK3PXP", imported.Secret);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-qr-uri")]
    [InlineData("otpauth-migration://offline?data=not-base64")]
    public async Task ImportWithConflictResolutionAsync_InvalidPayloadExamplesFailWithoutWrites(
        string invalidPayload)
    {
        var accounts = new Mock<IAccountManager>();
        var sut = new QrAccountImportService(accounts.Object);

        var result = await sut.ImportWithConflictResolutionAsync(
            invalidPayload,
            (_, _) => Task.FromResult<QrAccountImportResolution?>(new([])),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsFailed);
        accounts.Verify(value => value.GetAllOtpEntriesSortedAsync(), Times.Never);
    }

    [Fact]
    public async Task ImportWithConflictResolutionAsync_OversizedPayloadFailsWithoutWrites()
    {
        var accounts = new Mock<IAccountManager>();
        var sut = new QrAccountImportService(accounts.Object);

        var result = await sut.ImportWithConflictResolutionAsync(
            new string('x', 4097),
            (_, _) => Task.FromResult<QrAccountImportResolution?>(new([])),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsFailed);
        accounts.Verify(value => value.GetAllOtpEntriesSortedAsync(), Times.Never);
    }

    [Fact]
    public async Task ImportAsync_WhenGoogleMigrationRecoveryBackupFails_PerformsNoWrites()
    {
        var accounts = Manager([]);
        accounts.Setup(value => value.BackupOtpEntriesStorageFileAsync())
            .ReturnsAsync(Result.Fail("backup failed"));
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

        Assert.True(result.IsFailed);
        accounts.Verify(
            value => value.CommitImportAsync(It.IsAny<IReadOnlyCollection<Account>>()),
            Times.Never);
        accounts.Verify(value => value.AddNewAsync(It.IsAny<Account>()), Times.Never);
        accounts.Verify(
            value => value.UpdateAsync(It.IsAny<Account>(), It.IsAny<Account>()),
            Times.Never);
    }

    [Fact]
    public async Task ImportAsync_WhenGoogleMigrationAtomicCommitFails_ReturnsFailureWithoutPerAccountWrites()
    {
        var accounts = Manager([]);
        accounts.Setup(value => value.BackupOtpEntriesStorageFileAsync()).ReturnsAsync(Result.Ok());
        accounts.Setup(value => value.CommitImportAsync(It.IsAny<IReadOnlyCollection<Account>>()))
            .ReturnsAsync(Result.Fail("commit failed"));
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

        Assert.True(result.IsFailed);
        accounts.Verify(
            value => value.CommitImportAsync(It.IsAny<IReadOnlyCollection<Account>>()),
            Times.Once);
        accounts.Verify(value => value.AddNewAsync(It.IsAny<Account>()), Times.Never);
        accounts.Verify(
            value => value.UpdateAsync(It.IsAny<Account>(), It.IsAny<Account>()),
            Times.Never);
    }

    [Fact]
    public async Task ImportAsync_WhenGoogleMigrationConflictIsUpdated_CommitsResolvedSnapshotOnce()
    {
        var existing = new Account(
            Guid.NewGuid(),
            "Example",
            "KRUGS4ZANFZSAYJA",
            "alice",
            isFavorite: true);
        var accounts = Manager([existing]);
        accounts.Setup(value => value.BackupOtpEntriesStorageFileAsync()).ReturnsAsync(Result.Ok());
        IReadOnlyCollection<Account>? committed = null;
        accounts.Setup(value => value.CommitImportAsync(It.IsAny<IReadOnlyCollection<Account>>()))
            .ReturnsAsync((IReadOnlyCollection<Account> snapshot) =>
            {
                committed = snapshot;
                return Result.Ok();
            });
        var sut = new QrAccountImportService(accounts.Object);
        var payload = MigrationPayload(
            [new MigrationAccount(
                Convert.FromHexString("48656C6C6F21DEADBEEF"),
                "Example:alice",
                "Example")]);

        var result = await sut.ImportAsync(
            payload,
            (_, _) => Task.FromResult(QrAccountConflictDecision.UpdateExisting),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.UpdatedCount);
        var updated = Assert.Single(committed!);
        Assert.Equal(existing.ID, updated.ID);
        Assert.Equal("JBSWY3DPEHPK3PXP", updated.Secret);
        Assert.True(updated.IsFavorite);
        accounts.Verify(
            value => value.CommitImportAsync(It.IsAny<IReadOnlyCollection<Account>>()),
            Times.Once);
        accounts.Verify(value => value.AddNewAsync(It.IsAny<Account>()), Times.Never);
        accounts.Verify(
            value => value.UpdateAsync(It.IsAny<Account>(), It.IsAny<Account>()),
            Times.Never);
    }

    [Fact]
    public async Task ImportAsync_WhenGoogleMigrationIsLastBatch_ReportsNoMoreBatches()
    {
        var accounts = Manager([]);
        accounts.Setup(value => value.BackupOtpEntriesStorageFileAsync()).ReturnsAsync(Result.Ok());
        accounts.Setup(value => value.CommitImportAsync(It.IsAny<IReadOnlyCollection<Account>>()))
            .ReturnsAsync(Result.Ok());
        var sut = new QrAccountImportService(accounts.Object);
        var payload = MigrationPayload(
            [new MigrationAccount(
                Convert.FromHexString("48656C6C6F21DEADBEEF"),
                "Example:alice",
                "Example")],
            batchSize: 2,
            batchIndex: 1);

        var result = await sut.ImportAsync(
            payload,
            (_, _) => Task.FromResult(QrAccountConflictDecision.Cancel),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.HasMoreBatches);
        Assert.Equal(1, result.Value.BatchIndex);
        Assert.Equal(2, result.Value.BatchSize);
    }

    [Fact]
    public async Task ImportAsync_WhenGoogleMigrationContainsTenAccountsAndShortSecrets_ImportsAll()
    {
        var accounts = Manager([]);
        accounts.Setup(value => value.BackupOtpEntriesStorageFileAsync()).ReturnsAsync(Result.Ok());
        IReadOnlyCollection<Account>? committed = null;
        accounts.Setup(value => value.CommitImportAsync(It.IsAny<IReadOnlyCollection<Account>>()))
            .ReturnsAsync((IReadOnlyCollection<Account> snapshot) =>
            {
                committed = snapshot;
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
        Assert.Equal(10, committed!.Count);
        Assert.Contains(committed, account => account.Issuer == "München UTF8 Test");
        accounts.Verify(
            value => value.CommitImportAsync(It.IsAny<IReadOnlyCollection<Account>>()),
            Times.Once);
        accounts.Verify(value => value.AddNewAsync(It.IsAny<Account>()), Times.Never);
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
        accounts.Verify(
            value => value.CommitImportAsync(It.IsAny<IReadOnlyCollection<Account>>()),
            Times.Never);
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
        accounts.Verify(
            value => value.CommitImportAsync(It.IsAny<IReadOnlyCollection<Account>>()),
            Times.Never);
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

    private static MigrationAccount Migration(
        string name,
        string issuer,
        string secretHex) =>
        new(Convert.FromHexString(secretHex), name, issuer);

    private static string MigrationPayload(
        IReadOnlyList<MigrationAccount> entries,
        int batchSize = 1,
        int batchIndex = 0,
        int batchId = 42,
        int version = 1)
    {
        var payload = new List<byte>();
        foreach (var entry in entries)
            WriteBytes(payload, 1, EncodeAccount(entry));
        WriteVarintField(payload, 2, version);
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
