using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using TOTP.Core.Enums;
using TOTP.Core.Security.Interfaces;
using TOTP.Core.Security.Models;

namespace TOTP.Infrastructure.Security;

public sealed partial class PortableAuthorizationService
{
    private readonly IReadOnlyList<IPlatformQuickUnlock> _sensitiveAuthorizationAdapters;
    private long _sensitiveAuthorizationGeneration;
    private int _initialQuickUnlockSetupPending;
    private readonly object _sensitiveOperationsSync = new();
    private readonly HashSet<CancellationTokenSource> _sensitiveOperations = [];

    public Task<AuthorizationResult> AuthorizeSensitiveOperationAsync(
        PreferredUnlockMethod unlockMethod, CancellationToken cancellationToken = default) =>
        WithFreshPlatformAuthorizationAsync(unlockMethod,
            (_, _) => Task.FromResult(AuthorizationResult.Success), cancellationToken);

    public Task<AuthorizationResult> SetAppLockEnabledWithPlatformAsync(
        bool enabled, PreferredUnlockMethod authorizationMethod, CancellationToken cancellationToken = default) =>
        WithFreshPlatformAuthorizationAsync(authorizationMethod,
            (key, token) => SetAppLockEnabledCoreAsync(enabled, string.Empty, key, token), cancellationToken);

    public Task<AuthorizationResult> ConfigureUnlockMethodWithPlatformAsync(
        PreferredUnlockMethod unlockMethod, PreferredUnlockMethod authorizationMethod,
        CancellationToken cancellationToken = default) =>
        WithFreshPlatformAuthorizationAsync(authorizationMethod,
            (key, token) => ConfigureUnlockMethodFromAuthenticatedKeyAsync(unlockMethod, key, token), cancellationToken);

    public async Task<AuthorizationResult> CompleteInitialQuickUnlockSetupAsync(
        PreferredUnlockMethod? unlockMethod, CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _initialQuickUnlockSetupPending, 0) == 0)
            return unlockMethod is null ? AuthorizationResult.Success : AuthorizationResult.PasswordRequired;
        if (unlockMethod is null) return AuthorizationResult.Success;
        if (!State.IsUnlocked || !_securityContext.IsUnlocked) return AuthorizationResult.PasswordRequired;
        using var pending = BeginSensitiveAuthorization(cancellationToken);
        cancellationToken = pending.Token;
        byte[]? key = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            key = _securityContext.GetDekCopy();
            return await ConfigureUnlockMethodFromAuthenticatedKeyAsync(unlockMethod.Value, key, cancellationToken);
        }
        finally
        {
            if (key is not null) CryptographicOperations.ZeroMemory(key);
            EndSensitiveAuthorization(pending);
        }
    }

    private async Task<AuthorizationResult> WithFreshPlatformAuthorizationAsync(
        PreferredUnlockMethod method,
        Func<ReadOnlyMemory<byte>, CancellationToken, Task<AuthorizationResult>> operation,
        CancellationToken cancellationToken)
    {
        if (!State.IsConfigured) return AuthorizationResult.NotConfigured;
        if (!State.IsUnlocked || !_securityContext.IsUnlocked) return AuthorizationResult.PasswordRequired;
        if (method is not PreferredUnlockMethod.PlatformQuickUnlock and not PreferredUnlockMethod.PlatformDeviceCredential)
            return AuthorizationResult.PasswordRequired;
        var lockedPolicy = _settingsService.Current.AppLockEnabled;
        var configuredMethod = State.PreferredUnlockMethod;
        if (lockedPolicy && (method != configuredMethod || !_session.State.HasQuickUnlock))
            return AuthorizationResult.PasswordRequired;
        var generation = Volatile.Read(ref _sensitiveAuthorizationGeneration);
        using var pending = BeginSensitiveAuthorization(cancellationToken);
        cancellationToken = pending.Token;
        byte[]? key = null;
        byte[]? currentKey = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            key = _securityContext.GetDekCopy();
            if (key.Length != 32) return AuthorizationResult.Failed;
            AuthorizationResult authorized;
            if (lockedPolicy)
            {
                var attempt = await _session.TryUnlockWithPlatformAsync(cancellationToken);
                authorized = attempt.IsSuccess ? attempt.Value : AuthorizationResult.Failed;
            }
            else
                authorized = await AuthenticateOneTimePlatformAsync(method, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (authorized != AuthorizationResult.Success) return authorized;
            if (!State.IsUnlocked || !_securityContext.IsUnlocked
                || generation != Volatile.Read(ref _sensitiveAuthorizationGeneration)
                || lockedPolicy != _settingsService.Current.AppLockEnabled
                || configuredMethod != State.PreferredUnlockMethod)
                return AuthorizationResult.Cancelled;
            currentKey = _securityContext.GetDekCopy();
            if (!CryptographicOperations.FixedTimeEquals(key, currentKey))
            {
                Lock();
                return AuthorizationResult.Cancelled;
            }
            return await operation(key, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // A late platform completion must not restore a session after Lock().
            if (generation != Volatile.Read(ref _sensitiveAuthorizationGeneration)) Lock();
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError("Sensitive authorization failed. Exception type: {ExceptionType}.", exception.GetType().Name);
            return AuthorizationResult.Failed;
        }
        finally
        {
            if (key is not null) CryptographicOperations.ZeroMemory(key);
            if (currentKey is not null) CryptographicOperations.ZeroMemory(currentKey);
            EndSensitiveAuthorization(pending);
        }
    }

    private async Task<AuthorizationResult> AuthenticateOneTimePlatformAsync(
        PreferredUnlockMethod method, CancellationToken cancellationToken)
    {
        var adapter = _sensitiveAuthorizationAdapters.FirstOrDefault(value => value.UnlockMethod == method
            && value is not IPlatformUnattendedUnlock
            && value.ProviderId is PlatformQuickUnlockContract.AndroidKeystoreBiometricProvider
                or PlatformQuickUnlockContract.AndroidKeystoreDeviceCredentialProvider
                or PlatformQuickUnlockContract.WindowsHelloTpmProvider
                or PlatformQuickUnlockContract.MacOSKeychainProvider);
        if (adapter is null || await adapter.GetAvailabilityAsync(cancellationToken) != PlatformQuickUnlockAvailability.Available)
            return AuthorizationResult.NotAvailable;
        // Use a random challenge, never the vault key. Android/Windows verify
        // during registration; Keychain user presence is checked on retrieval.
        var challenge = RandomNumberGenerator.GetBytes(32);
        PlatformQuickUnlockWrapperV2? wrapper = null;
        var removedSuccessfully = false;
        try
        {
            var registered = await adapter.RegisterAsync(challenge, cancellationToken);
            if (registered.IsFailed)
            {
                var error = registered.Errors.OfType<PlatformQuickUnlockError>().FirstOrDefault();
                return error?.Code switch
                {
                    PlatformQuickUnlockErrorCode.Cancelled => AuthorizationResult.Cancelled,
                    PlatformQuickUnlockErrorCode.DisabledByPolicy => AuthorizationResult.DisabledByPolicy,
                    PlatformQuickUnlockErrorCode.RetriesExhausted => AuthorizationResult.TooManyAttempts,
                    PlatformQuickUnlockErrorCode.Unavailable => AuthorizationResult.NotAvailable,
                    _ => AuthorizationResult.Failed
                };
            }
            wrapper = registered.Value;
            if (wrapper.Provider != adapter.ProviderId || PlatformQuickUnlockContract.GetUnlockMethod(wrapper) != method)
                return AuthorizationResult.Failed;
            if (wrapper.Provider == PlatformQuickUnlockContract.MacOSKeychainProvider)
            {
                var retrieved = await adapter.TryUnlockAsync(wrapper, cancellationToken);
                if (retrieved.IsFailed) return AuthorizationResult.Failed;
                using var attempt = retrieved.Value;
                if (!attempt.IsSuccess)
                    return attempt.Status switch
                    {
                        PlatformQuickUnlockStatus.Cancelled => AuthorizationResult.Cancelled,
                        PlatformQuickUnlockStatus.NotAvailable or PlatformQuickUnlockStatus.KeyNotFound => AuthorizationResult.NotAvailable,
                        _ => AuthorizationResult.Failed
                    };
                if (attempt.VaultKey is null || !CryptographicOperations.FixedTimeEquals(
                        challenge, attempt.VaultKey.Memory.Span)) return AuthorizationResult.Failed;
            }
            var removed = await adapter.RemoveAsync(wrapper, CancellationToken.None);
            removedSuccessfully = removed.IsSuccess;
            return removed.IsSuccess ? AuthorizationResult.Success : AuthorizationResult.Failed;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(challenge);
            if (wrapper is not null)
            {
                if (!removedSuccessfully)
                {
                    try { await adapter.RemoveAsync(wrapper, CancellationToken.None); }
                    catch (Exception exception)
                    {
                        _logger.LogWarning("Temporary authentication cleanup failed. Exception type: {ExceptionType}.", exception.GetType().Name);
                    }
                }
                AuthorizationEnvelopeBufferCleaner.Clear(wrapper);
            }
        }
    }

    private async Task<AuthorizationResult> ConfigureUnlockMethodFromAuthenticatedKeyAsync(
        PreferredUnlockMethod method, ReadOnlyMemory<byte> key, CancellationToken cancellationToken)
    {
        if (method == PreferredUnlockMethod.Password) return await SetGateAsync(AuthorizationGateKind.Password);
        if (method is not PreferredUnlockMethod.PlatformQuickUnlock and not PreferredUnlockMethod.PlatformDeviceCredential)
            return AuthorizationResult.Failed;
        var enrolled = await _quickUnlockEnrollment.EnableWithAuthenticatedKeyAsync(key, method, cancellationToken);
        if (enrolled.IsFailed) return MapEnrollmentFailure(enrolled);
        if (!await RefreshSessionAsync()) return AuthorizationResult.Failed;
        var previous = _settingsService.Current.PreferredUnlockMethod;
        _settingsService.Current.PreferredUnlockMethod = method;
        var saved = await _settingsService.SaveAsync();
        if (saved.IsFailed) _settingsService.Current.PreferredUnlockMethod = previous;
        ApplySessionState();
        return saved.IsSuccess ? AuthorizationResult.Success : AuthorizationResult.Failed;
    }

    private CancellationTokenSource BeginSensitiveAuthorization(CancellationToken cancellationToken)
    {
        var pending = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lock (_sensitiveOperationsSync)
        {
            _sensitiveOperations.Add(pending);
            if (!State.IsUnlocked || !_securityContext.IsUnlocked) pending.Cancel();
        }
        return pending;
    }

    private void EndSensitiveAuthorization(CancellationTokenSource pending)
    {
        lock (_sensitiveOperationsSync) _sensitiveOperations.Remove(pending);
    }

    private void CancelSensitiveAuthorizations()
    {
        CancellationTokenSource[] pendingOperations;
        lock (_sensitiveOperationsSync) pendingOperations = _sensitiveOperations.ToArray();
        foreach (var pending in pendingOperations)
        {
            try { pending.Cancel(); }
            catch (ObjectDisposedException) { }
            catch (Exception exception)
            {
                _logger.LogWarning("Authentication cancellation callback failed. Exception type: {ExceptionType}.", exception.GetType().Name);
            }
        }
    }
}
