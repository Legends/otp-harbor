using System.Threading;
using TOTP.Core.Security.Models;

namespace TOTP.Core.Security.Interfaces;

public interface IAuthorizationService
{
    AuthorizationState State { get; }

    Task InitializeAsync();
    Task<bool> IsHelloAvailableAsync();
    Task<AuthorizationResult> TryUnlockOnStartupAsync();
    Task<AuthorizationResult> TryUnlockOnStartupAsync(CancellationToken ct);
    Task<AuthorizationResult> TryUnlockWithPasswordAsync(string password);
    Task<AuthorizationResult> TryUnlockWithHelloAsync();
    Task<AuthorizationResult> TryUnlockWithHelloAsync(CancellationToken ct);

    Task<AuthorizationResult> ConfigurePasswordAsync(string password, string confirmPassword);
    Task<AuthorizationResult> ConfigureHelloAsync();
    Task<AuthorizationResult> ConfigureHelloAsync(string recoveryPassword);
    Task<AuthorizationResult> SetGateAsync(AuthorizationGateKind gate);
    Task<AuthorizationResult> ChangePasswordAsync(string currentPassword, string newPassword);
    Task<AuthorizationResult> SetAppLockEnabledAsync(bool enabled, string recoveryPassword);

    void Logout();
    void Lock();
}
