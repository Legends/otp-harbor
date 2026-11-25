using System.Text;
using TOTP.Core.Enums;
using TOTP.Core.Models;
using TOTP.Core.Security.Models;
using TOTP.Tests.Common;

namespace TOTP.Tests.Security;

[Collection(NonParallelCollectionDefinition.NonParallel)]
public sealed partial class AuthenticationWorkflowTests
{
    public static IEnumerable<object[]> AuthenticationTransitions =>
        from source in Enum.GetValues<InitialAppLockChoice>()
        from destination in Enum.GetValues<InitialAppLockChoice>()
        select new object[] { source, destination };

    [Theory]
    [MemberData(nameof(AuthenticationTransitions))]
    public async Task EveryAuthenticationTransition_PreservesEncryptedAccountsRestartAndPasswordRecovery(
        InitialAppLockChoice source, InitialAppLockChoice destination)
    {
        using var harness = new AuthenticationWorkflowHarness();
        var accounts = SyntheticAccounts();
        using (var process = await harness.CreateAsync(source))
        {
            Assert.True((await process.Accounts.CommitImportAsync(accounts)).IsSuccess);
            Assert.Equal(AuthorizationResult.Success, await process.ChangeAsync(destination));
            Assert.Equal(destination != InitialAppLockChoice.AppLockDisabled, process.Settings.Current.AppLockEnabled);
            AssertAccounts(accounts, (await process.Accounts.GetAllOtpEntriesSortedAsync()).Value);
            Assert.DoesNotContain(accounts[0].Secret, Encoding.UTF8.GetString(await File.ReadAllBytesAsync(harness.VaultPath, TestContext.Current.CancellationToken)));
        }

        using var restarted = await harness.OpenAsync();
        var automatic = await restarted.Authorization.TryUnlockOnStartupAsync(TestContext.Current.CancellationToken);
        if (destination == InitialAppLockChoice.MasterPassword)
        {
            Assert.Equal(AuthorizationResult.PasswordRequired, automatic);
            Assert.True((await restarted.Accounts.GetAllOtpEntriesSortedAsync()).IsFailed);
            Assert.Equal(AuthorizationResult.Success, await restarted.Authorization.TryUnlockWithPasswordAsync(AuthenticationWorkflowHarness.Password, TestContext.Current.CancellationToken));
        }
        else Assert.Equal(AuthorizationResult.Success, automatic);
        AssertAccounts(accounts, (await restarted.Accounts.GetAllOtpEntriesSortedAsync()).Value);
        restarted.Authorization.Lock();
        Assert.Equal(AuthorizationResult.InvalidCredentials, await restarted.Authorization.TryUnlockWithPasswordAsync("Synthetic wrong password", TestContext.Current.CancellationToken));
        Assert.False(restarted.Authorization.State.IsUnlocked);
        Assert.True((await restarted.Accounts.GetAllOtpEntriesSortedAsync()).IsFailed);
        Assert.Equal(AuthorizationResult.Success, await restarted.Authorization.TryUnlockWithPasswordAsync(AuthenticationWorkflowHarness.Password, TestContext.Current.CancellationToken));
        AssertAccounts(accounts, (await restarted.Accounts.GetAllOtpEntriesSortedAsync()).Value);
    }

    [Theory]
    [InlineData(PreferredUnlockMethod.PlatformQuickUnlock, PlatformQuickUnlockStatus.Cancelled)]
    [InlineData(PreferredUnlockMethod.PlatformQuickUnlock, PlatformQuickUnlockStatus.NotAvailable)]
    [InlineData(PreferredUnlockMethod.PlatformQuickUnlock, PlatformQuickUnlockStatus.VerificationFailed)]
    [InlineData(PreferredUnlockMethod.PlatformDeviceCredential, PlatformQuickUnlockStatus.Cancelled)]
    [InlineData(PreferredUnlockMethod.PlatformDeviceCredential, PlatformQuickUnlockStatus.NotAvailable)]
    [InlineData(PreferredUnlockMethod.PlatformDeviceCredential, PlatformQuickUnlockStatus.VerificationFailed)]
    public async Task FailedCurrentDeviceAuthentication_CannotChangePreferencesOrStoredKeyWrappers(
        PreferredUnlockMethod method, PlatformQuickUnlockStatus outcome)
    {
        using var harness = new AuthenticationWorkflowHarness();
        var choice = method == PreferredUnlockMethod.PlatformQuickUnlock ? InitialAppLockChoice.StrongBiometrics : InitialAppLockChoice.DeviceScreenLock;
        using var process = await harness.CreateAsync(choice);
        var accounts = SyntheticAccounts();
        Assert.True((await process.Accounts.CommitImportAsync(accounts)).IsSuccess);
        var originalEnvelope = await File.ReadAllBytesAsync(harness.EnvelopePath, TestContext.Current.CancellationToken);
        harness.Adapter(method).Outcome = outcome;

        Assert.NotEqual(AuthorizationResult.Success, await process.Authorization.SetAppLockEnabledWithPlatformAsync(false, method, TestContext.Current.CancellationToken));
        Assert.NotEqual(AuthorizationResult.Success, await process.Authorization.ConfigureUnlockMethodWithPlatformAsync(PreferredUnlockMethod.Password, method, TestContext.Current.CancellationToken));
        Assert.True(process.Settings.Current.AppLockEnabled);
        Assert.Equal(method, process.Authorization.State.PreferredUnlockMethod);
        Assert.Equal(originalEnvelope, await File.ReadAllBytesAsync(harness.EnvelopePath, TestContext.Current.CancellationToken));
        AssertAccounts(accounts, (await process.Accounts.GetAllOtpEntriesSortedAsync()).Value);
    }

    private static List<Account> SyntheticAccounts()
    {
        var group = new AccountGroup(Guid.NewGuid(), "Synthetic group", "#4C956C");
        return Enumerable.Range(0, 40).Select(index => new Account(Guid.NewGuid(), $"Synthetic {index:D2}",
            "JBSWY3DPEHPK3PXP", $"user{index}@example.invalid", index % 2 == 0 ? 30 : 60,
            groups: [group], isFavorite: index % 3 == 0, algorithm: index % 2 == 0 ? "SHA1" : "SHA256", digits: index % 2 == 0 ? 6 : 8)).ToList();
    }

    private static void AssertAccounts(IReadOnlyList<Account> expected, IReadOnlyList<Account> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        foreach (var account in expected)
        {
            var restored = Assert.Single(actual, value => value.Issuer == account.Issuer);
            Assert.Equal(account.Secret, restored.Secret);
            Assert.Equal(account.AccountName, restored.AccountName);
            Assert.Equal(account.PeriodSeconds, restored.PeriodSeconds);
            Assert.Equal(account.Algorithm, restored.Algorithm);
            Assert.Equal(account.Digits, restored.Digits);
            Assert.Equal(account.IsFavorite, restored.IsFavorite);
            Assert.Equal(account.Groups.Select(group => group.Name), restored.Groups.Select(group => group.Name));
        }
    }
}
