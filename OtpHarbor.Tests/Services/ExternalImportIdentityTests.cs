using FluentResults;
using Moq;
using TOTP.Avalonia.Shared.Import;
using TOTP.Core.Models;
using TOTP.Core.Services.Interfaces;
using TOTP.Core.Services.Models;
using TOTP.Infrastructure.Services;

namespace TOTP.Tests.Services;

public sealed class ExternalImportIdentityTests
{
    private const string Secret = "JBSWY3DPEHPK3PXP";

    [Theory]
    [InlineData("Microsoft", "jack@home.com")]
    [InlineData("Google", "jack@work.com")]
    public async Task DifferentIdentitiesInOneExport_DoNotCollideThroughReusedIdsOrSecrets(string issuer, string name)
    {
        var first = new Account(Guid.NewGuid(), "Google", Secret, "jack@home.com");
        var second = new Account(first.ID, issuer, Secret, name);
        var manager = Manager([]);
        manager.Setup(value => value.BackupOtpEntriesStorageFileAsync()).ReturnsAsync(Result.Ok());
        IReadOnlyCollection<Account>? written = null;
        manager.Setup(value => value.CommitImportAsync(It.IsAny<IReadOnlyCollection<Account>>()))
            .Callback<IReadOnlyCollection<Account>>(batch => written = batch).ReturnsAsync(Result.Ok());
        var result = await new AccountImportService(manager.Object).ImportWithConflictResolutionAsync([first, second], (preview, _) =>
        {
            Assert.Equal(2, preview.NewCount);
            Assert.Equal(0, preview.InputDuplicateCount);
            Assert.Empty(preview.ChangedConflicts);
            Assert.All(preview.Entries, entry => Assert.Null(entry.Existing));
            return Task.FromResult<AccountImportResolution?>(new([]));
        }, TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Added);
        Assert.NotNull(written);
        Assert.Equal(2, written.Select(account => account.ID).Distinct().Count());
        Assert.Contains(written, account => account.Issuer == "Google" && account.AccountName == "jack@home.com");
        Assert.Contains(written, account => account.Issuer == issuer && account.AccountName == name);
    }

    [Theory]
    [InlineData(false, "secret")]
    [InlineData(false, "period")]
    [InlineData(false, "algorithm")]
    [InlineData(false, "digits")]
    [InlineData(false, "none")]
    [InlineData(true, "secret")]
    [InlineData(true, "period")]
    [InlineData(true, "algorithm")]
    [InlineData(true, "digits")]
    [InlineData(true, "none")]
    public async Task SameIssuerAndEmptyAccountNames_AreReviewedForDifferencesOrSkippedAsDuplicates(bool qr, string changed)
    {
        var existing = new Account(Guid.NewGuid(), " Google ", Secret, "  ");
        var incoming = new Account(Guid.NewGuid(), "google", changed == "secret" ? "KRSXG5DSNFXGOIDB" : Secret,
            null, changed == "period" ? 60 : 30, algorithm: changed == "algorithm" ? "SHA512" : "SHA1", digits: changed == "digits" ? 8 : 6);
        var manager = Manager([existing]);
        var reviews = 0;
        void Check(AccountImportPreview preview)
        {
            reviews++;
            Assert.Equal(0, preview.NewCount);
            Assert.NotNull(Assert.Single(preview.Entries).Existing);
            if (changed == "none")
            {
                Assert.Empty(preview.ChangedConflicts);
                Assert.True(ImportReviewPolicy.IsOnlyExactDuplicates(preview, new(1)));
                return;
            }
            var conflict = Assert.Single(preview.ChangedConflicts);
            Assert.False(conflict.IssuerChanged);
            Assert.False(conflict.AccountNameChanged);
            Assert.Equal(changed == "secret", conflict.SecretChanged);
            Assert.Equal(changed == "period", conflict.PeriodChanged);
            Assert.Equal(changed == "algorithm", conflict.AlgorithmChanged);
            Assert.Equal(changed == "digits", conflict.DigitsChanged);
        }
        if (qr)
        {
            var payload = $"otpauth://totp/google?secret={incoming.Secret}&period={incoming.PeriodSeconds}&algorithm={incoming.Algorithm}&digits={incoming.Digits}";
            var result = await new QrAccountImportService(manager.Object).ImportWithConflictResolutionAsync(payload, (preview, _) =>
            {
                Check(QrImportReviewAdapter.ToAccountPreview(preview));
                return Task.FromResult<QrAccountImportResolution?>(null);
            }, TestContext.Current.CancellationToken);
            Assert.True(result.IsSuccess);
            Assert.Equal(QrAccountImportStatus.Cancelled, result.Value.Status);
        }
        else
        {
            var result = await new AccountImportService(manager.Object).ImportWithConflictResolutionAsync([incoming], (preview, _) =>
            {
                Check(preview);
                return Task.FromResult<AccountImportResolution?>(null);
            }, TestContext.Current.CancellationToken);
            Assert.True(result.IsSuccess);
            Assert.Equal(AccountImportStatus.Cancelled, result.Value.Status);
        }
        Assert.Equal(1, reviews);
        manager.Verify(value => value.GetAllOtpEntriesSortedAsync(), Times.Once);
        manager.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(false, null, "jack@work.com")]
    [InlineData(false, "jack@work.com", null)]
    [InlineData(true, null, "jack@work.com")]
    [InlineData(true, "jack@work.com", null)]
    public async Task EmptyAndNamedAccountsWithSameIssuer_AreDistinct(bool qr, string? existingName, string? incomingName)
    {
        var manager = Manager([new Account(Guid.NewGuid(), "Google", Secret, existingName)]);
        void Check(AccountImportPreview preview)
        {
            Assert.Equal(1, preview.NewCount);
            Assert.Empty(preview.ChangedConflicts);
            Assert.Null(Assert.Single(preview.Entries).Existing);
        }
        if (qr)
        {
            var label = incomingName is null ? "Google" : $"Google:{Uri.EscapeDataString(incomingName)}";
            var result = await new QrAccountImportService(manager.Object).ImportWithConflictResolutionAsync(
                $"otpauth://totp/{label}?secret={Secret}", (preview, _) =>
                {
                    Check(QrImportReviewAdapter.ToAccountPreview(preview));
                    return Task.FromResult<QrAccountImportResolution?>(null);
                }, TestContext.Current.CancellationToken);
            Assert.True(result.IsSuccess);
        }
        else
        {
            var result = await new AccountImportService(manager.Object).ImportWithConflictResolutionAsync(
                [new Account(Guid.NewGuid(), "Google", Secret, incomingName)], (preview, _) =>
                {
                    Check(preview);
                    return Task.FromResult<AccountImportResolution?>(null);
                }, TestContext.Current.CancellationToken);
            Assert.True(result.IsSuccess);
        }
        manager.Verify(value => value.GetAllOtpEntriesSortedAsync(), Times.Once);
        manager.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(AccountImportConflictAction.Replace, 1, 1, 0)]
    [InlineData(AccountImportConflictAction.KeepBoth, 2, 0, 0)]
    [InlineData(AccountImportConflictAction.Skip, 1, 0, 1)]
    public async Task EmptyAccountConflictWithinExport_HonorsEachReviewedDecision(AccountImportConflictAction action, int added, int replaced, int skipped)
    {
        var first = new Account(Guid.NewGuid(), "Google", Secret, null);
        var second = new Account(Guid.NewGuid(), "google", Secret, "", 60);
        var manager = Manager([]);
        manager.Setup(value => value.BackupOtpEntriesStorageFileAsync()).ReturnsAsync(Result.Ok());
        IReadOnlyCollection<Account>? written = null;
        manager.Setup(value => value.CommitImportAsync(It.IsAny<IReadOnlyCollection<Account>>()))
            .Callback<IReadOnlyCollection<Account>>(batch => written = batch).ReturnsAsync(Result.Ok());
        var result = await new AccountImportService(manager.Object).ImportWithConflictResolutionAsync([first, second], (preview, _) =>
        {
            Assert.Null(written);
            Assert.Equal(1, preview.NewCount);
            Assert.Equal(1, preview.InputDuplicateCount);
            var conflict = Assert.Single(preview.ChangedConflicts);
            Assert.Equal(1, conflict.ImportIndex);
            Assert.True(conflict.PeriodChanged);
            Assert.True(preview.Entries[1].ExistingIsFromExport);
            return Task.FromResult<AccountImportResolution?>(new([new(1, action)]));
        }, TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        Assert.Equal(added, result.Value.Added);
        Assert.Equal(replaced, result.Value.Replaced);
        Assert.Equal(skipped, result.Value.Skipped);
        Assert.NotNull(written);
        Assert.Equal(added, written.Count);
        Assert.All(written, account => Assert.Null(account.AccountName));
        Assert.Contains(written, account => account.PeriodSeconds == (action == AccountImportConflictAction.Skip ? 30 : 60));
        manager.Verify(value => value.BackupOtpEntriesStorageFileAsync(), Times.Once);
        manager.Verify(value => value.CommitImportAsync(It.IsAny<IReadOnlyCollection<Account>>()), Times.Once);
    }

    private static Mock<IAccountManager> Manager(IReadOnlyList<Account> existing)
    {
        var manager = new Mock<IAccountManager>(MockBehavior.Strict);
        manager.Setup(value => value.GetAllOtpEntriesSortedAsync()).ReturnsAsync(Result.Ok(existing));
        return manager;
    }
}
