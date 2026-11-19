using System.Threading;
using TOTP.Core.Enums;
using TOTP.Core.Security.Models;

namespace TOTP.Core.Security.Interfaces;

public interface IAuthorizationService
{
    AuthorizationState State { get; }

    Task InitializeAsync();
    Task<bool> IsHelloAvailableAsync();
    Task<bool> IsUnlockMethodAvailableAsync(PreferredUnlockMethod unlockMethod);
    Task<AuthorizationResult> TryUnlockOnStartupAsync();
    Task<AuthorizationResult> TryUnlockOnStartupAsync(CancellationToken ct);
    Task<AuthorizationResult> TryUnlockWithPasswordAsync(string password);
    Task<AuthorizationResult> TryUnlockWithPasswordAsync(string password, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return TryUnlockWithPasswordAsync(password);
    }
    Task<AuthorizationResult> TryUnlockWithHelloAsync();
    Task<AuthorizationResult> TryUnlockWithHelloAsync(CancellationToken ct);

    Task<AuthorizationResult> ConfigurePasswordAsync(string password, string confirmPassword);
    Task<AuthorizationResult> ConfigureHelloAsync();
    Task<AuthorizationResult> ConfigureHelloAsync(string recoveryPassword);
    Task<AuthorizationResult> ConfigureUnlockMethodAsync(
        PreferredUnlockMethod unlockMethod,
        string recoveryPassword);
    Task<AuthorizationResult> SetGateAsync(AuthorizationGateKind gate);
    Task<AuthorizationResult> ChangePasswordAsync(string currentPassword, string newPassword);
    Task<AuthorizationResult> SetAppLockEnabledAsync(bool enabled, string recoveryPassword);
    Task<AuthorizationResult> SetAppLockEnabledAsync(
        bool enabled, string recoveryPassword, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return SetAppLockEnabledAsync(enabled, recoveryPassword);
    }

    Task<AuthorizationResult> AuthorizeSensitiveOperationAsync(
        PreferredUnlockMethod unlockMethod, CancellationToken cancellationToken = default) =>
        Task.FromResult(AuthorizationResult.NotAvailable);

    Task<AuthorizationResult> SetAppLockEnabledWithPlatformAsync(
        bool enabled, PreferredUnlockMethod authorizationMethod, CancellationToken cancellationToken = default) =>
        Task.FromResult(AuthorizationResult.NotAvailable);

    Task<AuthorizationResult> ReactivateAppLockWithPlatformAsync(
        PreferredUnlockMethod authorizationMethod, CancellationToken cancellationToken = default) =>
        Task.FromResult(AuthorizationResult.NotAvailable);

    Task<AuthorizationResult> ConfigureUnlockMethodWithPlatformAsync(
        PreferredUnlockMethod unlockMethod, PreferredUnlockMethod authorizationMethod,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(AuthorizationResult.NotAvailable);

    Task<AuthorizationResult> CompleteInitialQuickUnlockSetupAsync(
        PreferredUnlockMethod? unlockMethod, CancellationToken cancellationToken = default) =>
        Task.FromResult(unlockMethod is null ? AuthorizationResult.Success : AuthorizationResult.NotAvailable);

    void Logout();
    void Lock();
}
