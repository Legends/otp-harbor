using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using TOTP.Core.Enums;
using TOTP.Core.Models;
using TOTP.Core.Security.Models;
using TOTP.Core.Services.Models;
using TOTP.Infrastructure.Services;

namespace TOTP.Tests.Security;

public sealed partial class AuthenticationWorkflowTests
{
    [Theory]
    [InlineData(InitialAppLockChoice.MasterPassword)]
    [InlineData(InitialAppLockChoice.StrongBiometrics)]
    [InlineData(InitialAppLockChoice.DeviceScreenLock)]
    [InlineData(InitialAppLockChoice.AppLockDisabled)]
    public async Task EncryptedBackup_RestoresCompleteProfilesIntoAnotherVaultAfterAuthenticationChange(InitialAppLockChoice choice)
    {
        using var source = new AuthenticationWorkflowHarness();
        using var original = await source.CreateAsync(InitialAppLockChoice.MasterPassword);
        var accounts = SyntheticAccounts();
        Assert.True((await original.Accounts.CommitImportAsync(accounts)).IsSuccess);
        Assert.Equal(AuthorizationResult.Success, await original.ChangeAsync(choice));
        var export = new ExportService(NullLogger<ExportService>.Instance);
        using var encrypted = new MemoryStream();
        Assert.True((await export.ExportToEncryptedStreamAsync(
            (await original.Accounts.GetAllOtpEntriesSortedAsync()).Value, AuthenticationWorkflowHarness.Password,
            encrypted, ExportFileFormat.Json, TestContext.Current.CancellationToken)).IsSuccess);
        Assert.DoesNotContain(accounts[0].Secret, Encoding.UTF8.GetString(encrypted.ToArray()));
        encrypted.Position = 0;
        var decoded = await export.ImportFromEncryptedStreamAsync(AuthenticationWorkflowHarness.Password, encrypted, TestContext.Current.CancellationToken);
        Assert.True(decoded.IsSuccess);

        using var destination = new AuthenticationWorkflowHarness();
        using (var restored = await destination.CreateAsync(choice))
        {
            var importer = new AccountImportService(restored.Accounts);
            var result = await importer.ImportAsync(decoded.Value, ImportConflictStrategy.SkipExisting,
                (preview, _) => Task.FromResult(preview.NewCount == accounts.Count), TestContext.Current.CancellationToken);
            Assert.True(result.IsSuccess);
            Assert.Equal(AccountImportStatus.Completed, result.Value.Status);
            Assert.Equal(accounts.Count, result.Value.Added);
            AssertAccounts(accounts, (await restored.Accounts.GetAllOtpEntriesSortedAsync()).Value);
            var repeated = await importer.ImportAsync(decoded.Value, ImportConflictStrategy.SkipExisting,
                (_, _) => Task.FromResult(true), TestContext.Current.CancellationToken);
            Assert.True(repeated.IsSuccess);
            Assert.Equal(0, repeated.Value.Added);
            AssertAccounts(accounts, (await restored.Accounts.GetAllOtpEntriesSortedAsync()).Value);
        }
        using var restarted = await destination.OpenAsync();
        Assert.Equal(AuthorizationResult.Success, await restarted.Authorization.TryUnlockWithPasswordAsync(
            AuthenticationWorkflowHarness.Password, TestContext.Current.CancellationToken));
        AssertAccounts(accounts, (await restarted.Accounts.GetAllOtpEntriesSortedAsync()).Value);
    }

    [Theory]
    [InlineData("password")]
    [InlineData("tamper")]
    [InlineData("cancel")]
    public async Task UnreadableOrCancelledEncryptedBackup_CannotChangeTheExistingVault(string failure)
    {
        using var harness = new AuthenticationWorkflowHarness();
        using var process = await harness.CreateAsync(InitialAppLockChoice.StrongBiometrics);
        var accounts = SyntheticAccounts();
        Assert.True((await process.Accounts.CommitImportAsync(accounts)).IsSuccess);
        var before = await File.ReadAllBytesAsync(harness.VaultPath, TestContext.Current.CancellationToken);
        var export = new ExportService(NullLogger<ExportService>.Instance);
        using var encrypted = new MemoryStream();
        Assert.True((await export.ExportToEncryptedStreamAsync(accounts, AuthenticationWorkflowHarness.Password,
            encrypted, ExportFileFormat.Json, TestContext.Current.CancellationToken)).IsSuccess);
        var bytes = encrypted.ToArray();
        if (failure == "tamper") bytes[^1] ^= 1;
        using var input = new MemoryStream(bytes);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        if (failure == "cancel") cancellation.Cancel();
        if (failure == "cancel")
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => export.ImportFromEncryptedStreamAsync(
                AuthenticationWorkflowHarness.Password, input, cancellation.Token));
        else
            Assert.True((await export.ImportFromEncryptedStreamAsync(failure == "password" ? "Incorrect synthetic password" :
                AuthenticationWorkflowHarness.Password, input, cancellation.Token)).IsFailed);
        Assert.Equal(before, await File.ReadAllBytesAsync(harness.VaultPath, TestContext.Current.CancellationToken));
        AssertAccounts(accounts, (await process.Accounts.GetAllOtpEntriesSortedAsync()).Value);
    }

    [Theory]
    [InlineData(AccountImportConflictAction.Skip)]
    [InlineData(AccountImportConflictAction.Replace)]
    [InlineData(AccountImportConflictAction.KeepBoth)]
    [InlineData(null)]
    public async Task ImportConflictReview_DoesNotWriteUntilConfirmedAndAppliesOnlyChosenResolution(AccountImportConflictAction? action)
    {
        using var harness = new AuthenticationWorkflowHarness();
        using var process = await harness.CreateAsync(InitialAppLockChoice.MasterPassword);
        var account = SyntheticAccounts()[0];
        var added = await process.Accounts.AddNewAsync(account);
        Assert.True(added.IsSuccess, string.Join("; ", added.Errors.Select(error => error.Message)));
        var changed = new Account(account.ID, account.Issuer, "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ", account.AccountName, 60);
        var before = await File.ReadAllBytesAsync(harness.VaultPath, TestContext.Current.CancellationToken);
        var result = await new AccountImportService(process.Accounts).ImportWithConflictResolutionAsync([changed], async (preview, token) =>
        {
            Assert.Equal(before, await File.ReadAllBytesAsync(harness.VaultPath, token));
            Assert.Equal(account.Secret, Assert.Single((await process.Accounts.GetAllOtpEntriesSortedAsync()).Value).Secret);
            var conflict = Assert.Single(preview.ChangedConflicts);
            Assert.True(conflict.SecretChanged);
            return action is null ? null : new AccountImportResolution([new AccountImportConflictResolution(conflict.ImportIndex, action.Value)]);
        }, TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        var stored = (await process.Accounts.GetAllOtpEntriesSortedAsync()).Value;
        if (action is null or AccountImportConflictAction.Skip)
        {
            AssertAccounts([account], stored);
            Assert.Equal(before, await File.ReadAllBytesAsync(harness.VaultPath, TestContext.Current.CancellationToken));
        }
        else
        {
            Assert.Contains(stored, value => value.Secret == changed.Secret);
            Assert.Equal(action == AccountImportConflictAction.KeepBoth ? 2 : 1, stored.Count);
        }
    }

    [Fact]
    public async Task AccountCrudAndOtpGeneration_RespectLockAndPersistValidatedChanges()
    {
        using var harness = new AuthenticationWorkflowHarness();
        using var process = await harness.CreateAsync(InitialAppLockChoice.MasterPassword);
        var account = new Account(Guid.NewGuid(), "RFC 6238", "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ", digits: 8);
        Assert.True((await process.Accounts.AddNewAsync(account)).IsSuccess);
        var otp = new AccountTotpService(process.Accounts, new OtpNetTotpGenerator(new RfcVectorTimeProvider()));
        Assert.Equal("94287082", (await otp.GenerateAsync(account.ID)).Value.Code);
        process.Authorization.Lock();
        Assert.True((await otp.GenerateAsync(account.ID)).IsFailed);
        Assert.True((await process.Accounts.DeleteAsync(account)).IsFailed);
        Assert.Equal(AuthorizationResult.Success, await process.Authorization.TryUnlockWithPasswordAsync(
            AuthenticationWorkflowHarness.Password, TestContext.Current.CancellationToken));
        var edited = new Account(account.ID, "Edited synthetic issuer", account.Secret, "edited@example.invalid", 60, isFavorite: true, algorithm: "SHA256");
        Assert.True((await process.Accounts.UpdateAsync(account, edited)).IsSuccess);
        AssertAccounts([edited], (await process.Accounts.GetAllOtpEntriesSortedAsync()).Value);
        Assert.True((await process.Accounts.DeleteAsync(edited)).IsSuccess);
        Assert.Empty((await process.Accounts.GetAllOtpEntriesSortedAsync()).Value);
        Assert.True((await otp.GenerateAsync(account.ID)).IsFailed);
    }

    private sealed class RfcVectorTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.FromUnixTimeSeconds(59);
    }
}
