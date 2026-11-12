using FluentResults;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TOTP.Core.Models;
using TOTP.Core.Services.Interfaces;
using TOTP.Core.Services.Models;
using TOTP.Infrastructure.Services;

namespace TOTP.Tests.Services;

public sealed class AccountImportServiceTests
{
    [Fact]
    public async Task ExternalReview_ReadyEntryIsNotSilentlySkippedAfterKeepBothGeneratesItsLabel()
    {
        var existing = new Account(Guid.NewGuid(), "Original", "JBSWY3DPEHPK3PXP", "alice");
        var first = new Account(Guid.NewGuid(), "Original", "KRSXG5DSNFXGOIDB", "alice");
        var second = new Account(Guid.NewGuid(), "Original (imported)", "GEZDGNBVGY3TQOJQ", "alice");
        var accounts = new Mock<IAccountManager>();
        accounts.Setup(value => value.GetAllOtpEntriesSortedAsync()).ReturnsAsync(Result.Ok<IReadOnlyList<Account>>([existing]));
        accounts.Setup(value => value.BackupOtpEntriesStorageFileAsync()).ReturnsAsync(Result.Ok());
        IReadOnlyCollection<Account>? written = null;
        accounts.Setup(value => value.CommitImportAsync(It.IsAny<IReadOnlyCollection<Account>>()))
            .Callback<IReadOnlyCollection<Account>>(value => written = value).ReturnsAsync(Result.Ok());
        var result = await new AccountImportService(accounts.Object).ImportWithConflictResolutionAsync([first, second],
            (preview, _) =>
            {
                Assert.Equal(1, preview.NewCount);
                return Task.FromResult<AccountImportResolution?>(new([new(0, AccountImportConflictAction.KeepBoth)]));
            }, TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Added);
        Assert.Equal(0, result.Value.Skipped);
        Assert.NotNull(written);
        Assert.Equal(3, written.Count);
        Assert.Contains(written, account => account.Secret == second.Secret);
        Assert.Equal(3, written.Select(account => account.Issuer).Distinct().Count());
    }

    [Fact]
    public async Task ExternalReview_LaterReplacementKeepsTheReviewedTargetAfterAnEarlierSecretChange()
    {
        var existing = new Account(Guid.NewGuid(), "Old", "JBSWY3DPEHPK3PXP", "alice");
        var first = new Account(Guid.NewGuid(), "Old", "KRSXG5DSNFXGOIDB", "alice");
        var second = new Account(Guid.NewGuid(), "New label", existing.Secret, "alice");
        var accounts = new Mock<IAccountManager>();
        accounts.Setup(value => value.GetAllOtpEntriesSortedAsync()).ReturnsAsync(Result.Ok<IReadOnlyList<Account>>([existing]));
        accounts.Setup(value => value.BackupOtpEntriesStorageFileAsync()).ReturnsAsync(Result.Ok());
        IReadOnlyCollection<Account>? written = null;
        accounts.Setup(value => value.CommitImportAsync(It.IsAny<IReadOnlyCollection<Account>>()))
            .Callback<IReadOnlyCollection<Account>>(value => written = value).ReturnsAsync(Result.Ok());
        var result = await new AccountImportService(accounts.Object).ImportWithConflictResolutionAsync([first, second],
            (preview, _) =>
            {
                Assert.Equal(2, preview.ChangedConflicts.Count);
                return Task.FromResult<AccountImportResolution?>(new([new(0, AccountImportConflictAction.Replace), new(1, AccountImportConflictAction.Replace)]));
            }, TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Replaced);
        Assert.NotNull(written);
        var account = Assert.Single(written);
        Assert.Equal(existing.ID, account.ID);
        Assert.Equal(second.Issuer, account.Issuer);
        Assert.Equal(second.Secret, account.Secret);
    }

    [Fact]
    public async Task ExternalReview_ReplacementOfAnEarlierExportEntryTargetsItsKeptCopy()
    {
        var existing = new Account(Guid.NewGuid(), "Original", "JBSWY3DPEHPK3PXP", "alice");
        var first = new Account(Guid.NewGuid(), "First", existing.Secret, "alice");
        var second = new Account(Guid.NewGuid(), "First", "KRSXG5DSNFXGOIDB", "alice");
        var accounts = new Mock<IAccountManager>();
        accounts.Setup(value => value.GetAllOtpEntriesSortedAsync()).ReturnsAsync(Result.Ok<IReadOnlyList<Account>>([existing]));
        accounts.Setup(value => value.BackupOtpEntriesStorageFileAsync()).ReturnsAsync(Result.Ok());
        IReadOnlyCollection<Account>? written = null;
        accounts.Setup(value => value.CommitImportAsync(It.IsAny<IReadOnlyCollection<Account>>()))
            .Callback<IReadOnlyCollection<Account>>(value => written = value).ReturnsAsync(Result.Ok());
        var result = await new AccountImportService(accounts.Object).ImportWithConflictResolutionAsync([first, second],
            (preview, _) =>
            {
                Assert.True(preview.Entries[1].ExistingIsFromExport);
                return Task.FromResult<AccountImportResolution?>(new([new(0, AccountImportConflictAction.KeepBoth), new(1, AccountImportConflictAction.Replace)]));
            }, TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        Assert.NotNull(written);
        Assert.Equal(2, written.Count);
        Assert.Contains(existing, written);
        Assert.Contains(written, account => account.ID != existing.ID && account.Secret == second.Secret && account.Issuer == second.Issuer);
    }

    [Theory]
    [InlineData(AccountImportConflictAction.Skip, 0, 0)]
    [InlineData(AccountImportConflictAction.Replace, 0, 1)]
    [InlineData(AccountImportConflictAction.KeepBoth, 1, 0)]
    public async Task ExternalReview_SameSecretWithDifferentLabelRequiresExplicitDecision(
        AccountImportConflictAction action, int added, int replaced)
    {
        var current = new Account(Guid.NewGuid(), "Old label", "JBSWY3DPEHPK3PXP", "alice");
        var incoming = new Account(Guid.NewGuid(), "New label", current.Secret, "alice", 60, algorithm: "SHA512", digits: 8);
        var accounts = new Mock<IAccountManager>();
        accounts.Setup(value => value.GetAllOtpEntriesSortedAsync()).ReturnsAsync(Result.Ok<IReadOnlyList<Account>>([current]));
        accounts.Setup(value => value.BackupOtpEntriesStorageFileAsync()).ReturnsAsync(Result.Ok());
        IReadOnlyCollection<Account>? written = null;
        accounts.Setup(value => value.CommitImportAsync(It.IsAny<IReadOnlyCollection<Account>>()))
            .Callback<IReadOnlyCollection<Account>>(value => written = value).ReturnsAsync(Result.Ok());
        var sut = new AccountImportService(accounts.Object);
        var result = await sut.ImportWithConflictResolutionAsync([incoming], (preview, _) =>
        {
            Assert.Null(written);
            var conflict = Assert.Single(preview.ChangedConflicts);
            Assert.True(conflict.IssuerChanged);
            Assert.False(conflict.SecretChanged);
            Assert.Equal("Old label", Assert.Single(preview.Entries).Existing!.Issuer);
            Assert.DoesNotContain(current.Secret, System.Text.Json.JsonSerializer.Serialize(preview));
            return Task.FromResult<AccountImportResolution?>(new([new(0, action)]));
        }, TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        Assert.Equal(added, result.Value.Added);
        Assert.Equal(replaced, result.Value.Replaced);
        if (action == AccountImportConflictAction.Skip)
        {
            Assert.Null(written);
            accounts.Verify(value => value.BackupOtpEntriesStorageFileAsync(), Times.Never);
        }
        else
        {
            Assert.NotNull(written);
            Assert.Equal(action == AccountImportConflictAction.KeepBoth ? 2 : 1, written.Count);
            Assert.Contains(written, account => account.Algorithm == "SHA512" && account.Digits == 8 && account.PeriodSeconds == 60);
            if (action == AccountImportConflictAction.KeepBoth) Assert.Contains(current, written);
        }
    }

    [Fact]
    public async Task ExternalReview_DuplicatesWithinExportAreComparedBeforeAnyWrite()
    {
        var first = new Account(Guid.NewGuid(), "Demo", "JBSWY3DPEHPK3PXP", "alice");
        var second = new Account(Guid.NewGuid(), "Demo", "KRSXG5DSNFXGOIDB", "alice");
        var accounts = new Mock<IAccountManager>();
        accounts.Setup(value => value.GetAllOtpEntriesSortedAsync()).ReturnsAsync(Result.Ok<IReadOnlyList<Account>>([]));
        var result = await new AccountImportService(accounts.Object).ImportWithConflictResolutionAsync([first, second], (preview, _) =>
        {
            Assert.Equal(1, preview.NewCount);
            Assert.Equal(1, Assert.Single(preview.ChangedConflicts).ImportIndex);
            Assert.True(preview.ChangedConflicts[0].SecretChanged);
            Assert.NotNull(preview.Entries[1].Existing);
            return Task.FromResult<AccountImportResolution?>(null);
        }, TestContext.Current.CancellationToken);
        Assert.Equal(AccountImportStatus.Cancelled, result.Value.Status);
        accounts.Verify(value => value.BackupOtpEntriesStorageFileAsync(), Times.Never);
        accounts.Verify(value => value.CommitImportAsync(It.IsAny<IReadOnlyCollection<Account>>()), Times.Never);
    }

    [Fact]
    public async Task ImportAsync_DuplicatesWithinTheFileAreVisibleBeforeAnyWrite()
    {
        var accounts = new Mock<IAccountManager>();
        accounts.Setup(value => value.GetAllOtpEntriesSortedAsync())
            .ReturnsAsync(Result.Ok<IReadOnlyList<Account>>([]));
        var service = new AccountImportService(accounts.Object);
        var reviewed = false;
        var result = await service.ImportAsync(
            [new Account(Guid.NewGuid(), "Demo", "JBSWY3DPEHPK3PXP", "user"),
             new Account(Guid.NewGuid(), "demo", "JBSWY3DPEHPK3PXP", "USER")],
            ImportConflictStrategy.SkipExisting,
            (preview, _) =>
            {
                Assert.Equal(2, preview.TotalCount);
                Assert.Equal(0, preview.ConflictCount);
                Assert.Equal(1, preview.InputDuplicateCount);
                Assert.Equal(1, preview.NewCount);
                Assert.Equal(2, preview.Entries.Count);
                reviewed = true;
                return Task.FromResult(false);
            }, TestContext.Current.CancellationToken);
        Assert.True(reviewed);
        Assert.Equal(AccountImportStatus.Cancelled, result.Value.Status);
        accounts.Verify(value => value.CommitImportAsync(It.IsAny<IReadOnlyCollection<Account>>()), Times.Never);
        accounts.Verify(value => value.BackupOtpEntriesStorageFileAsync(), Times.Never);
    }

    [Fact]
    public async Task ImportAsync_WhenAnyTargetIsInvalid_DoesNotReadOrMutateVault()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var accounts = new Mock<IAccountManager>(MockBehavior.Strict);
        var sut = new AccountImportService(accounts.Object);

        var result = await sut.ImportAsync(
            [new Account(Guid.NewGuid(), "Issuer", "not*base32", "user")],
            ImportConflictStrategy.SkipExisting,
            (_, _) => Task.FromResult(true),
            cancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(AccountImportStatus.InvalidTargets, result.Value.Status);
        accounts.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ImportAsync_WhenPeriodIsOutsidePolicy_DoesNotReadOrMutateVault()
    {
        var accounts = new Mock<IAccountManager>(MockBehavior.Strict);
        var sut = new AccountImportService(accounts.Object);

        var result = await sut.ImportAsync(
            [new Account(Guid.NewGuid(), "Issuer", "JBSWY3DPEHPK3PXP", "user", 3601)],
            ImportConflictStrategy.SkipExisting,
            (_, _) => Task.FromResult(true),
            TestContext.Current.CancellationToken);

        Assert.Equal(AccountImportStatus.InvalidTargets, result.Value.Status);
        accounts.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ImportAsync_WithValidGroup_PreservesGroupForNewAccount()
    {
        var group = new AccountGroup(Guid.NewGuid(), "Work", "#4F6BED");
        var incoming = new Account(
            Guid.NewGuid(),
            "Issuer",
            "JBSWY3DPEHPK3PXP",
            "user",
            group: group);
        var accounts = new Mock<IAccountManager>();
        accounts.Setup(value => value.GetAllOtpEntriesSortedAsync())
            .ReturnsAsync(Result.Ok<IReadOnlyList<Account>>([]));
        accounts.Setup(value => value.BackupOtpEntriesStorageFileAsync()).ReturnsAsync(Result.Ok());
        accounts.Setup(value => value.CommitImportAsync(It.IsAny<IReadOnlyCollection<Account>>()))
            .ReturnsAsync(Result.Ok());
        var sut = new AccountImportService(accounts.Object);

        var result = await sut.ImportAsync(
            [incoming],
            ImportConflictStrategy.SkipExisting,
            (_, _) => Task.FromResult(true),
            TestContext.Current.CancellationToken);

        Assert.Equal(AccountImportStatus.Completed, result.Value.Status);
        accounts.Verify(value => value.CommitImportAsync(
            It.Is<IReadOnlyCollection<Account>>(batch =>
                batch.Count == 1 && batch.Single().Group == group)), Times.Once);
    }

    [Fact]
    public async Task ImportAsync_WithSixHundredAccounts_CreatesOneBackupAndOneAtomicCommit()
    {
        var imported = Enumerable.Range(1, 600)
            .Select(index => new Account(
                Guid.NewGuid(),
                $"Issuer {index}",
                "JBSWY3DPEHPK3PXP",
                $"account-{index}"))
            .ToArray();
        var accounts = new Mock<IAccountManager>();
        accounts.Setup(value => value.GetAllOtpEntriesSortedAsync())
            .ReturnsAsync(Result.Ok<IReadOnlyList<Account>>([]));
        accounts.Setup(value => value.BackupOtpEntriesStorageFileAsync()).ReturnsAsync(Result.Ok());
        accounts.Setup(value => value.CommitImportAsync(It.IsAny<IReadOnlyCollection<Account>>()))
            .ReturnsAsync(Result.Ok());
        var sut = new AccountImportService(accounts.Object);

        var result = await sut.ImportAsync(
            imported,
            ImportConflictStrategy.SkipExisting,
            (_, _) => Task.FromResult(true),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(600, result.Value.Added);
        accounts.Verify(value => value.BackupOtpEntriesStorageFileAsync(), Times.Once);
        accounts.Verify(value => value.CommitImportAsync(
            It.Is<IReadOnlyCollection<Account>>(batch => batch.Count == 600)), Times.Once);
        accounts.Verify(value => value.AddNewAsync(It.IsAny<Account>()), Times.Never);
        accounts.Verify(value => value.UpdateAsync(It.IsAny<Account>(), It.IsAny<Account>()), Times.Never);
    }

    [Fact]
    public async Task ImportAsync_AtMaximumAccountCount_IsAccepted()
    {
        var repeated = new Account(
            Guid.NewGuid(),
            "Issuer",
            "JBSWY3DPEHPK3PXP",
            "account");
        var imported = Enumerable.Repeat(repeated, 10_000).ToArray();
        var accounts = new Mock<IAccountManager>();
        accounts.Setup(value => value.GetAllOtpEntriesSortedAsync())
            .ReturnsAsync(Result.Ok<IReadOnlyList<Account>>([]));
        accounts.Setup(value => value.BackupOtpEntriesStorageFileAsync()).ReturnsAsync(Result.Ok());
        accounts.Setup(value => value.CommitImportAsync(It.IsAny<IReadOnlyCollection<Account>>()))
            .ReturnsAsync(Result.Ok());
        var sut = new AccountImportService(accounts.Object);

        var result = await sut.ImportAsync(
            imported,
            ImportConflictStrategy.SkipExisting,
            (_, _) => Task.FromResult(true),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(AccountImportStatus.Completed, result.Value.Status);
        Assert.Equal(1, result.Value.Added);
        Assert.Equal(9_999, result.Value.Skipped);
        accounts.Verify(value => value.CommitImportAsync(
            It.Is<IReadOnlyCollection<Account>>(batch => batch.Count == 1)), Times.Once);
    }

    [Fact]
    public async Task ImportAsync_AboveMaximumAccountCount_IsRejectedBeforeVaultAccess()
    {
        var repeated = new Account(
            Guid.NewGuid(),
            "Issuer",
            "JBSWY3DPEHPK3PXP",
            "account");
        var accounts = new Mock<IAccountManager>(MockBehavior.Strict);
        var sut = new AccountImportService(accounts.Object);

        var result = await sut.ImportAsync(
            Enumerable.Repeat(repeated, 10_001).ToArray(),
            ImportConflictStrategy.SkipExisting,
            (_, _) => Task.FromResult(true),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(AccountImportStatus.InvalidTargets, result.Value.Status);
        accounts.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ImportAsync_WhenAtomicCommitFails_DoesNotReportPartialSuccess()
    {
        var incoming = new Account(Guid.NewGuid(), "Issuer", "JBSWY3DPEHPK3PXP", "user");
        var accounts = new Mock<IAccountManager>();
        accounts.Setup(value => value.GetAllOtpEntriesSortedAsync())
            .ReturnsAsync(Result.Ok<IReadOnlyList<Account>>([]));
        accounts.Setup(value => value.BackupOtpEntriesStorageFileAsync()).ReturnsAsync(Result.Ok());
        accounts.Setup(value => value.CommitImportAsync(It.IsAny<IReadOnlyCollection<Account>>()))
            .ReturnsAsync(Result.Fail("commit failed"));
        var sut = new AccountImportService(accounts.Object);

        var result = await sut.ImportAsync(
            [incoming],
            ImportConflictStrategy.SkipExisting,
            (_, _) => Task.FromResult(true),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsFailed);
        accounts.Verify(value => value.CommitImportAsync(It.IsAny<IReadOnlyCollection<Account>>()), Times.Once);
    }

    [Fact]
    public async Task ImportWithConflictResolutionAsync_WhenBackupAddsFavorite_PresentsAndAppliesMetadataConflict()
    {
        var existing = new Account(
            Guid.NewGuid(),
            "Issuer",
            "JBSWY3DPEHPK3PXP",
            "user");
        var incoming = existing.WithFavorite(true);
        var accounts = new Mock<IAccountManager>();
        accounts.Setup(value => value.GetAllOtpEntriesSortedAsync())
            .ReturnsAsync(Result.Ok<IReadOnlyList<Account>>([existing]));
        accounts.Setup(value => value.BackupOtpEntriesStorageFileAsync()).ReturnsAsync(Result.Ok());
        accounts.Setup(value => value.CommitImportAsync(It.IsAny<IReadOnlyCollection<Account>>()))
            .ReturnsAsync(Result.Ok());
        var sut = new AccountImportService(accounts.Object);

        var result = await sut.ImportWithConflictResolutionAsync(
            [incoming],
            (preview, _) =>
            {
                var conflict = Assert.Single(preview.ChangedConflicts);
                Assert.True(conflict.FavoriteChanged);
                Assert.False(conflict.IssuerChanged);
                Assert.False(conflict.AccountNameChanged);
                Assert.False(conflict.SecretChanged);
                Assert.False(conflict.PeriodChanged);
                return Task.FromResult<AccountImportResolution?>(new AccountImportResolution(
                    [new AccountImportConflictResolution(
                        conflict.ImportIndex,
                        AccountImportConflictAction.Replace)]));
            },
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.Replaced);
        accounts.Verify(value => value.CommitImportAsync(
            It.Is<IReadOnlyCollection<Account>>(batch =>
                batch.Count == 1 && batch.Single().IsFavorite)), Times.Once);
    }

    [Fact]
    public async Task ImportAsync_WhenReplacingFromFormatWithoutFavorite_PreservesExistingFavorite()
    {
        var existing = new Account(
            Guid.NewGuid(),
            "Issuer",
            "JBSWY3DPEHPK3PXP",
            "user",
            isFavorite: true);
        var incoming = new Account(
            existing.ID,
            existing.Issuer,
            "KRSXG5DSNFXGOIDB",
            existing.AccountName);
        var accounts = new Mock<IAccountManager>();
        accounts.Setup(value => value.GetAllOtpEntriesSortedAsync())
            .ReturnsAsync(Result.Ok<IReadOnlyList<Account>>([existing]));
        accounts.Setup(value => value.BackupOtpEntriesStorageFileAsync()).ReturnsAsync(Result.Ok());
        accounts.Setup(value => value.CommitImportAsync(It.IsAny<IReadOnlyCollection<Account>>()))
            .ReturnsAsync(Result.Ok());
        var sut = new AccountImportService(accounts.Object);

        var result = await sut.ImportAsync(
            [incoming],
            ImportConflictStrategy.ReplaceExisting,
            (_, _) => Task.FromResult(true),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.Replaced);
        accounts.Verify(value => value.CommitImportAsync(
            It.Is<IReadOnlyCollection<Account>>(batch =>
                batch.Count == 1 && batch.Single().IsFavorite)), Times.Once);
    }

    [Fact]
    public async Task ImportAsync_WhenPlainFormatHasNoGroups_PreservesExistingMemberships()
    {
        var groups = new[]
        {
            new AccountGroup(Guid.NewGuid(), "test1", "#4C956C"),
            new AccountGroup(Guid.NewGuid(), "test2", "#4F6BED")
        };
        var existing = new Account(
            Guid.NewGuid(),
            "Issuer",
            "JBSWY3DPEHPK3PXP",
            "user",
            groups: groups);
        var incoming = new Account(
            existing.ID,
            existing.Issuer,
            "KRSXG5DSNFXGOIDB",
            existing.AccountName);
        var accounts = new Mock<IAccountManager>();
        accounts.Setup(value => value.GetAllOtpEntriesSortedAsync())
            .ReturnsAsync(Result.Ok<IReadOnlyList<Account>>([existing]));
        accounts.Setup(value => value.BackupOtpEntriesStorageFileAsync()).ReturnsAsync(Result.Ok());
        accounts.Setup(value => value.CommitImportAsync(It.IsAny<IReadOnlyCollection<Account>>()))
            .ReturnsAsync(Result.Ok());
        var sut = new AccountImportService(accounts.Object);

        var result = await sut.ImportAsync(
            [incoming],
            ImportConflictStrategy.ReplaceExisting,
            (_, _) => Task.FromResult(true),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        accounts.Verify(value => value.CommitImportAsync(
            It.Is<IReadOnlyCollection<Account>>(batch =>
                batch.Single().Groups.SequenceEqual(groups))), Times.Once);
    }

    [Fact]
    public async Task RestoreBackupWithConflictResolutionAsync_ProfileDifferenceIsAuthoritative()
    {
        var existingGroup = new AccountGroup(Guid.NewGuid(), "Current", "#4C956C");
        var existing = new Account(
            Guid.NewGuid(),
            "Issuer",
            "JBSWY3DPEHPK3PXP",
            "user",
            groups: [existingGroup]);
        var incoming = new Account(
            existing.ID,
            existing.Issuer,
            existing.Secret,
            existing.AccountName);
        var accounts = new Mock<IAccountManager>();
        accounts.Setup(value => value.GetAllOtpEntriesSortedAsync())
            .ReturnsAsync(Result.Ok<IReadOnlyList<Account>>([existing]));
        accounts.Setup(value => value.BackupOtpEntriesStorageFileAsync()).ReturnsAsync(Result.Ok());
        accounts.Setup(value => value.CommitImportAsync(It.IsAny<IReadOnlyCollection<Account>>()))
            .ReturnsAsync(Result.Ok());
        var sut = new AccountImportService(accounts.Object);

        var result = await sut.RestoreBackupWithConflictResolutionAsync(
            [incoming],
            (preview, _) =>
            {
                var conflict = Assert.Single(preview.ChangedConflicts);
                Assert.True(conflict.GroupMembershipChanged);
                Assert.False(conflict.SecretChanged);
                return Task.FromResult<AccountImportResolution?>(new AccountImportResolution(
                    [new AccountImportConflictResolution(
                        conflict.ImportIndex,
                        AccountImportConflictAction.Replace)]));
            },
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.Replaced);
        accounts.Verify(value => value.CommitImportAsync(
            It.Is<IReadOnlyCollection<Account>>(batch =>
                batch.Count == 1 && batch.Single().Groups.Count == 0)), Times.Once);
    }

    [Fact]
    public async Task ImportAsync_WithInvalidGroupMetadata_RejectsBeforeVaultRead()
    {
        var invalidGroup = new AccountGroup(Guid.NewGuid(), "Work", "#123456");
        var accounts = new Mock<IAccountManager>(MockBehavior.Strict);
        var sut = new AccountImportService(accounts.Object);

        var result = await sut.ImportAsync(
            [new Account(
                Guid.NewGuid(),
                "Issuer",
                "JBSWY3DPEHPK3PXP",
                "user",
                group: invalidGroup)],
            ImportConflictStrategy.SkipExisting,
            (_, _) => Task.FromResult(true),
            TestContext.Current.CancellationToken);

        Assert.Equal(AccountImportStatus.InvalidTargets, result.Value.Status);
        accounts.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ImportAsync_WithKeepBoth_AssignsNewIdentityAndCollisionFreeIssuer()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var existing = new Account(Guid.NewGuid(), "Issuer", "JBSWY3DPEHPK3PXP", "user");
        var incoming = new Account(existing.ID, "Issuer", "KRSXG5DSNFXGOIDB", "user");
        var accounts = new Mock<IAccountManager>();
        accounts.Setup(value => value.GetAllOtpEntriesSortedAsync())
            .ReturnsAsync(Result.Ok<IReadOnlyList<Account>>([existing]));
        accounts.Setup(value => value.BackupOtpEntriesStorageFileAsync()).ReturnsAsync(Result.Ok());
        accounts.Setup(value => value.CommitImportAsync(It.IsAny<IReadOnlyCollection<Account>>()))
            .ReturnsAsync(Result.Ok());
        var sut = new AccountImportService(accounts.Object);

        var result = await sut.ImportAsync(
            [incoming],
            ImportConflictStrategy.KeepBoth,
            (preview, _) => Task.FromResult(preview.ConflictCount == 1),
            cancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(AccountImportStatus.Completed, result.Value.Status);
        Assert.Equal(1, result.Value.Added);
        accounts.Verify(value => value.CommitImportAsync(
            It.Is<IReadOnlyCollection<Account>>(batch =>
                batch.Count == 2
                && batch.Any(account =>
                    account.ID != existing.ID
                    && account.Issuer == "Issuer (imported)"
                    && account.PeriodSeconds == 30))), Times.Once);
    }

    [Fact]
    public async Task ImportAsync_WhenConfirmationDeclined_DoesNotCreateBackupOrWrite()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var accounts = new Mock<IAccountManager>();
        accounts.Setup(value => value.GetAllOtpEntriesSortedAsync())
            .ReturnsAsync(Result.Ok<IReadOnlyList<Account>>([]));
        var sut = new AccountImportService(accounts.Object);

        var result = await sut.ImportAsync(
            [new Account(Guid.NewGuid(), "Issuer", "JBSWY3DPEHPK3PXP", "user")],
            ImportConflictStrategy.SkipExisting,
            (_, _) => Task.FromResult(false),
            cancellationToken);

        Assert.Equal(AccountImportStatus.Cancelled, result.Value.Status);
        accounts.Verify(value => value.BackupOtpEntriesStorageFileAsync(), Times.Never);
        accounts.Verify(value => value.AddNewAsync(It.IsAny<Account>()), Times.Never);
    }

    [Fact]
    public async Task ImportAsync_WhenSkipExistingMatchesEveryAccount_ReturnsWithoutConfirmationOrBackup()
    {
        var existing = Enumerable.Range(1, 22)
            .Select(index => new Account(
                Guid.NewGuid(),
                "Issuer",
                "JBSWY3DPEHPK3PXP",
                $"user-{index}"))
            .ToList();
        var accounts = new Mock<IAccountManager>();
        accounts.Setup(value => value.GetAllOtpEntriesSortedAsync())
            .ReturnsAsync(Result.Ok<IReadOnlyList<Account>>(existing));
        var confirmationCount = 0;
        var sut = new AccountImportService(accounts.Object);

        var result = await sut.ImportAsync(
            existing,
            ImportConflictStrategy.SkipExisting,
            (_, _) =>
            {
                confirmationCount++;
                return Task.FromResult(true);
            },
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(AccountImportStatus.Completed, result.Value.Status);
        Assert.Equal(22, result.Value.Skipped);
        Assert.Equal(0, confirmationCount);
        accounts.Verify(value => value.BackupOtpEntriesStorageFileAsync(), Times.Never);
        accounts.Verify(value => value.AddNewAsync(It.IsAny<Account>()), Times.Never);
        accounts.Verify(value => value.UpdateAsync(It.IsAny<Account>(), It.IsAny<Account>()), Times.Never);
    }

    [Fact]
    public async Task ImportWithConflictResolutionAsync_WhenAccountWasRenamed_CanRestoreBackupVersion()
    {
        var unchangedOne = new Account(
            Guid.NewGuid(), "GitHub", "JBSWY3DPEHPK3PXP", "octocat@example.invalid");
        var backupRenamed = new Account(
            Guid.NewGuid(), "Microsoft", "JBSWY3DPEHPK3PXP", "original@example.invalid");
        var currentRenamed = new Account(
            backupRenamed.ID, "Microsoft", backupRenamed.Secret, "renamed@example.invalid");
        var unchangedTwo = new Account(
            Guid.NewGuid(), "Google", "JBSWY3DPEHPK3PXP", "user@example.invalid");
        var deletedAccount = new Account(
            Guid.NewGuid(), "Cloudflare", "KRSXG5DSNFXGOIDB", "admin@example.invalid");
        var accounts = new Mock<IAccountManager>();
        accounts.Setup(value => value.GetAllOtpEntriesSortedAsync())
            .ReturnsAsync(Result.Ok<IReadOnlyList<Account>>(
                [unchangedOne, currentRenamed, unchangedTwo]));
        accounts.Setup(value => value.BackupOtpEntriesStorageFileAsync()).ReturnsAsync(Result.Ok());
        accounts.Setup(value => value.CommitImportAsync(It.IsAny<IReadOnlyCollection<Account>>()))
            .ReturnsAsync(Result.Ok());
        var sut = new AccountImportService(accounts.Object);

        var result = await sut.ImportWithConflictResolutionAsync(
            [unchangedOne, backupRenamed, unchangedTwo, deletedAccount],
            (preview, _) =>
            {
                Assert.Equal(4, preview.TotalCount);
                Assert.Equal(1, preview.NewCount);
                Assert.Equal(2, preview.UnchangedCount);
                var conflict = Assert.Single(preview.ChangedConflicts);
                Assert.Equal("Microsoft: renamed@example.invalid",
                    $"{conflict.CurrentIssuer}: {conflict.CurrentAccountName}");
                Assert.Equal("Microsoft: original@example.invalid",
                    $"{conflict.BackupIssuer}: {conflict.BackupAccountName}");
                Assert.False(conflict.IssuerChanged);
                Assert.True(conflict.AccountNameChanged);
                Assert.False(conflict.SecretChanged);
                Assert.False(conflict.PeriodChanged);
                return Task.FromResult<AccountImportResolution?>(new AccountImportResolution(
                    [new AccountImportConflictResolution(
                        conflict.ImportIndex,
                        AccountImportConflictAction.Replace)]));
            },
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(AccountImportStatus.Completed, result.Value.Status);
        Assert.Equal(1, result.Value.Added);
        Assert.Equal(1, result.Value.Replaced);
        Assert.Equal(2, result.Value.Skipped);
        accounts.Verify(value => value.BackupOtpEntriesStorageFileAsync(), Times.Once);
        accounts.Verify(value => value.CommitImportAsync(
            It.Is<IReadOnlyCollection<Account>>(batch =>
                batch.Count == 4
                && batch.Any(replacement =>
                    replacement.ID == currentRenamed.ID
                    && replacement.AccountName == "original@example.invalid")
                && batch.Any(added => added.ID == deletedAccount.ID))), Times.Once);
    }

    [Fact]
    public async Task RestoreBackupWithConflictResolutionAsync_MixedVault_UsesIdsAndLogicalIdentitySafely()
    {
        var currentFixture = await LoadScenarioFixtureAsync("mixed-account-current-v3.json");
        var olderFixture = await LoadScenarioFixtureAsync("mixed-account-older.json");
        var unchanged = currentFixture.Single(account => account.Issuer == "Scenario Exact");
        var currentCollision = currentFixture.Single(account => account.Issuer == "Scenario Collision");
        var currentOnly = currentFixture.Single(account => account.Issuer == "Scenario Current Only");
        var olderCollision = olderFixture.Single(account => account.Issuer == "Scenario Collision");
        var olderNew = olderFixture.Single(account => account.Issuer == "Scenario Older New");
        Assert.NotEqual(currentCollision.ID, olderCollision.ID);

        var accounts = new Mock<IAccountManager>();
        accounts.Setup(value => value.GetAllOtpEntriesSortedAsync())
            .ReturnsAsync(Result.Ok<IReadOnlyList<Account>>(currentFixture));
        accounts.Setup(value => value.BackupOtpEntriesStorageFileAsync()).ReturnsAsync(Result.Ok());
        accounts.Setup(value => value.CommitImportAsync(It.IsAny<IReadOnlyCollection<Account>>()))
            .ReturnsAsync(Result.Ok());
        var sut = new AccountImportService(accounts.Object);

        var result = await sut.RestoreBackupWithConflictResolutionAsync(
            olderFixture,
            (preview, _) =>
            {
                Assert.Equal(3, preview.TotalCount);
                Assert.Equal(1, preview.UnchangedCount);
                Assert.Equal(1, preview.NewCount);
                var conflict = Assert.Single(preview.ChangedConflicts);
                Assert.Equal(1, conflict.ImportIndex);
                Assert.Equal(currentCollision.Issuer, conflict.CurrentIssuer);
                Assert.Equal(currentCollision.AccountName, conflict.CurrentAccountName);
                Assert.Equal(olderCollision.Issuer, conflict.BackupIssuer);
                Assert.Equal(olderCollision.AccountName, conflict.BackupAccountName);
                Assert.True(conflict.SecretChanged);
                return Task.FromResult<AccountImportResolution?>(new AccountImportResolution(
                    [new AccountImportConflictResolution(
                        conflict.ImportIndex,
                        AccountImportConflictAction.Replace)]));
            },
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(AccountImportStatus.Completed, result.Value.Status);
        Assert.Equal(1, result.Value.Added);
        Assert.Equal(1, result.Value.Replaced);
        Assert.Equal(1, result.Value.Skipped);
        accounts.Verify(value => value.BackupOtpEntriesStorageFileAsync(), Times.Once);
        accounts.Verify(value => value.CommitImportAsync(
            It.Is<IReadOnlyCollection<Account>>(batch =>
                batch.Count == 4
                && batch.Any(account => account.ID == unchanged.ID
                    && account.Secret == unchanged.Secret)
                && batch.Any(account => account.ID == currentCollision.ID
                    && account.Secret == olderCollision.Secret)
                && batch.All(account => account.ID != olderCollision.ID)
                && batch.Any(account => account.ID == currentOnly.ID
                    && account.Secret == currentOnly.Secret)
                && batch.Any(account => account.ID == olderNew.ID))), Times.Once);
    }

    [Fact]
    public async Task ImportWithConflictResolutionAsync_WhenResolutionIsIncomplete_FailsBeforeBackupOrWrites()
    {
        var existing = new Account(Guid.NewGuid(), "Issuer", "JBSWY3DPEHPK3PXP", "current");
        var incoming = new Account(existing.ID, "Issuer", existing.Secret, "backup");
        var accounts = new Mock<IAccountManager>();
        accounts.Setup(value => value.GetAllOtpEntriesSortedAsync())
            .ReturnsAsync(Result.Ok<IReadOnlyList<Account>>([existing]));
        var sut = new AccountImportService(accounts.Object);

        var result = await sut.ImportWithConflictResolutionAsync(
            [incoming],
            (_, _) => Task.FromResult<AccountImportResolution?>(
                new AccountImportResolution([])),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsFailed);
        accounts.Verify(value => value.BackupOtpEntriesStorageFileAsync(), Times.Never);
        accounts.Verify(value => value.UpdateAsync(It.IsAny<Account>(), It.IsAny<Account>()), Times.Never);
        accounts.Verify(value => value.AddNewAsync(It.IsAny<Account>()), Times.Never);
    }

    [Fact]
    public async Task ImportWithConflictResolutionAsync_DetectsEverySupportedAccountFieldChange()
    {
        var id = Guid.NewGuid();
        var existing = new Account(id, "Current issuer", "JBSWY3DPEHPK3PXP", "current", 30);
        var incoming = new Account(id, "Backup issuer", "KRSXG5DSNFXGOIDB", "backup", 60);
        var accounts = new Mock<IAccountManager>();
        accounts.Setup(value => value.GetAllOtpEntriesSortedAsync())
            .ReturnsAsync(Result.Ok<IReadOnlyList<Account>>([existing]));
        var sut = new AccountImportService(accounts.Object);

        var result = await sut.ImportWithConflictResolutionAsync(
            [incoming],
            (preview, _) =>
            {
                var conflict = Assert.Single(preview.ChangedConflicts);
                Assert.True(conflict.IssuerChanged);
                Assert.True(conflict.AccountNameChanged);
                Assert.True(conflict.SecretChanged);
                Assert.True(conflict.PeriodChanged);
                return Task.FromResult<AccountImportResolution?>(new AccountImportResolution(
                    [new AccountImportConflictResolution(
                        conflict.ImportIndex,
                        AccountImportConflictAction.Skip)]));
            },
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.Skipped);
        accounts.Verify(value => value.BackupOtpEntriesStorageFileAsync(), Times.Never);
    }

    private static async Task<List<Account>> LoadScenarioFixtureAsync(string name)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "OtpHarbor.sln")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var path = Path.Combine(directory.FullName, "scripts", "testing", "fixtures", name);
        await using var stream = File.OpenRead(path);
        var export = new ExportService(NullLogger<ExportService>.Instance);
        var decoded = await export.ImportFromStreamAsync(
            stream, name, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(decoded.IsSuccess);
        return decoded.Value;
    }
}
