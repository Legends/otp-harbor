using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using TOTP.Core.Enums;
using TOTP.Core.Security.Models;

namespace TOTP.Infrastructure.Security;

public sealed partial class PortableAuthorizationService
{
    public async Task<AuthorizationResult> ReactivateAppLockWithPlatformAsync(
        PreferredUnlockMethod authorizationMethod, CancellationToken cancellationToken = default)
    {
        if (!State.IsConfigured) return AuthorizationResult.NotConfigured;
        if (!State.IsUnlocked || !_securityContext.IsUnlocked) return AuthorizationResult.PasswordRequired;
        if (_settingsService.Current.AppLockEnabled) return AuthorizationResult.Cancelled;
        if (authorizationMethod != _settingsService.Current.PreferredUnlockMethod
            || authorizationMethod is not (PreferredUnlockMethod.PlatformQuickUnlock or PreferredUnlockMethod.PlatformDeviceCredential))
            return AuthorizationResult.PasswordRequired;
        var generation = Volatile.Read(ref _sensitiveAuthorizationGeneration);
        var previousMethod = State.PreferredUnlockMethod;
        using var pending = BeginSensitiveAuthorization(cancellationToken);
        cancellationToken = pending.Token;
        byte[]? key = null;
        var enrolled = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            key = _securityContext.GetDekCopy();
            if (key.Length != 32) return AuthorizationResult.Failed;
            bool CanCommit()
            {
                if (cancellationToken.IsCancellationRequested || !State.IsUnlocked || !_securityContext.IsUnlocked
                    || generation != Volatile.Read(ref _sensitiveAuthorizationGeneration)
                    || _settingsService.Current.AppLockEnabled || State.PreferredUnlockMethod != previousMethod
                    || _settingsService.Current.PreferredUnlockMethod != authorizationMethod) return false;
                var current = _securityContext.GetDekCopy();
                try { return CryptographicOperations.FixedTimeEquals(key, current); }
                finally { CryptographicOperations.ZeroMemory(current); }
            }

            // Enrollment itself verifies presence. Replacing the unattended wrapper
            // first is fail closed if the preference save subsequently fails.
            var registration = await _quickUnlockEnrollment.EnableWithPlatformVerificationAsync(
                key, authorizationMethod, CanCommit, cancellationToken);
            if (registration.IsFailed) return MapEnrollmentFailure(registration);
            enrolled = true;
            _settingsService.Current.AppLockEnabled = true;
            cancellationToken.ThrowIfCancellationRequested();
            var saved = await _settingsService.SaveAsync();
            if (saved.IsFailed) { Lock(); return AuthorizationResult.Failed; }
            if (!await RefreshSessionAsync()) { Lock(); return AuthorizationResult.Failed; }
            ApplySessionState();
            return AuthorizationResult.Success;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (enrolled || generation != Volatile.Read(ref _sensitiveAuthorizationGeneration)) Lock();
            throw;
        }
        catch (Exception exception)
        {
            if (enrolled) Lock();
            _logger.LogError("App-lock reactivation failed. Exception type: {ExceptionType}.", exception.GetType().Name);
            return AuthorizationResult.Failed;
        }
        finally
        {
            if (key is not null) CryptographicOperations.ZeroMemory(key);
            EndSensitiveAuthorization(pending);
        }
    }
}
