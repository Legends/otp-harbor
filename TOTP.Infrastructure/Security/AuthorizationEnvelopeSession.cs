using System.Security.Cryptography;
using FluentResults;
using Microsoft.Extensions.Logging;
using TOTP.Core.Security;
using TOTP.Core.Security.Interfaces;
using TOTP.Core.Security.Models;

namespace TOTP.Infrastructure.Security;

public sealed class AuthorizationEnvelopeSession : IAuthorizationEnvelopeSession
{
    private const int VaultKeySize = 32;

    private readonly IAuthorizationEnvelopeStore _store;
    private readonly IMasterPasswordService _passwordService;
    private readonly IStoredVaultKeyVerifier _vaultVerifier;
    private readonly ISecurityContext _securityContext;
    private readonly IReadOnlyList<IPlatformQuickUnlock> _platformQuickUnlockAdapters;
    private readonly ILogger<AuthorizationEnvelopeSession> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private AuthorizationEnvelopeV2? _envelope;

    public AuthorizationEnvelopeSession(
        IAuthorizationEnvelopeStore store,
        IMasterPasswordService passwordService,
        IStoredVaultKeyVerifier vaultVerifier,
        ISecurityContext securityContext,
        IEnumerable<IPlatformQuickUnlock> platformQuickUnlockAdapters,
        ILogger<AuthorizationEnvelopeSession> logger)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _passwordService = passwordService ?? throw new ArgumentNullException(nameof(passwordService));
        _vaultVerifier = vaultVerifier ?? throw new ArgumentNullException(nameof(vaultVerifier));
        _securityContext = securityContext ?? throw new ArgumentNullException(nameof(securityContext));
        _platformQuickUnlockAdapters = platformQuickUnlockAdapters?.ToArray()
            ?? throw new ArgumentNullException(nameof(platformQuickUnlockAdapters));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public AuthorizationEnvelopeSessionState State { get; private set; } =
        AuthorizationEnvelopeSessionState.NotInitialized;

    public async Task<Result<AuthorizationEnvelopeSessionState>> InitializeAsync(
        CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            ClearCachedEnvelope();
            State = AuthorizationEnvelopeSessionState.NotInitialized;

            var loaded = await _store.LoadAsync(cancellationToken);
            if (loaded.IsFailed)
            {
                return Fail<AuthorizationEnvelopeSessionState>(
                    AuthorizationEnvelopeSessionErrorCode.LoadFailed,
                    "The authorization envelope could not be loaded.",
                    loaded.Errors);
            }

            _envelope = loaded.Value;
            State = new AuthorizationEnvelopeSessionState(
                IsInitialized: true,
                IsConfigured: _envelope is not null,
                HasQuickUnlock: _envelope?.QuickUnlockWrapper is not null
                    && PlatformQuickUnlockContract.IsSupported(_envelope.QuickUnlockWrapper),
                HasUnattendedUnlock:
                    PlatformQuickUnlockContract.IsSupportedAndroidUnattendedWrapper(
                        _envelope?.QuickUnlockWrapper));
            return Result.Ok(State);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            ClearCachedEnvelope();
            State = AuthorizationEnvelopeSessionState.NotInitialized;
            _logger.LogError(ex, "Authorization-envelope session initialization failed unexpectedly.");
            return Fail<AuthorizationEnvelopeSessionState>(
                AuthorizationEnvelopeSessionErrorCode.UnexpectedFailure,
                "Authorization-envelope session initialization failed unexpectedly.",
                exception: ex);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<Result<AuthorizationResult>> TryUnlockWithPasswordAsync(
        string password,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(password))
            return Result.Ok(AuthorizationResult.InvalidCredentials);

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (!State.IsInitialized)
            {
                return Fail<AuthorizationResult>(
                    AuthorizationEnvelopeSessionErrorCode.NotInitialized,
                    "The authorization-envelope session is not initialized.");
            }

            if (_envelope is null)
                return Result.Ok(AuthorizationResult.NotConfigured);

            byte[]? recoveredKey = null;
            try
            {
                recoveredKey = await _passwordService.UnwrapKeyV2Async(
                    _envelope.PasswordWrapper,
                    password,
                    cancellationToken);
                if (recoveredKey is null)
                    return Result.Ok(AuthorizationResult.InvalidCredentials);
                if (recoveredKey.Length != VaultKeySize)
                {
                    return Fail<AuthorizationResult>(
                        AuthorizationEnvelopeSessionErrorCode.VaultVerificationFailed,
                        "The recovered vault key has an invalid length.");
                }

                var verified = await _vaultVerifier.VerifyAsync(recoveredKey, cancellationToken);
                if (verified.IsFailed)
                {
                    return Fail<AuthorizationResult>(
                        AuthorizationEnvelopeSessionErrorCode.VaultVerificationFailed,
                        "The vault could not be verified during password unlock.",
                        verified.Errors);
                }

                if (verified.Value is not VaultKeyVerificationStatus.Verified
                    and not VaultKeyVerificationStatus.VaultNotFound)
                {
                    return Fail<AuthorizationResult>(
                        AuthorizationEnvelopeSessionErrorCode.VaultVerificationFailed,
                        "The recovered key did not verify the vault.");
                }

                cancellationToken.ThrowIfCancellationRequested();
                _securityContext.SetDek(recoveredKey);
                return Result.Ok(AuthorizationResult.Success);
            }
            finally
            {
                if (recoveredKey is not null) CryptographicOperations.ZeroMemory(recoveredKey);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Password unlock through the authorization envelope failed unexpectedly.");
            return Fail<AuthorizationResult>(
                AuthorizationEnvelopeSessionErrorCode.UnexpectedFailure,
                "Password unlock through the authorization envelope failed unexpectedly.",
                exception: ex);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<Result<AuthorizationResult>> TryUnlockWithPlatformAsync(
        CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (!State.IsInitialized)
            {
                return Fail<AuthorizationResult>(
                    AuthorizationEnvelopeSessionErrorCode.NotInitialized,
                    "The authorization-envelope session is not initialized.");
            }

            if (_envelope is null)
                return Result.Ok(AuthorizationResult.NotConfigured);

            var wrapper = _envelope.QuickUnlockWrapper;
            if (!PlatformQuickUnlockContract.IsSupported(wrapper))
                return Result.Ok(AuthorizationResult.PasswordRequired);

            var adapter = _platformQuickUnlockAdapters.FirstOrDefault(value =>
                string.Equals(value.ProviderId, wrapper!.Provider, StringComparison.Ordinal));
            if (adapter is null)
                return Result.Ok(AuthorizationResult.PasswordRequired);

            var platformResult = await adapter.TryUnlockAsync(wrapper!, cancellationToken);
            if (platformResult.IsFailed)
            {
                return Fail<AuthorizationResult>(
                    AuthorizationEnvelopeSessionErrorCode.PlatformUnlockFailed,
                    "Platform quick unlock failed.",
                    platformResult.Errors);
            }

            using var attempt = platformResult.Value;
            if (!attempt.IsSuccess)
                return Result.Ok(MapPlatformOutcome(attempt.Status));

            var platformKey = attempt.VaultKey;
            if (platformKey is null || platformKey.Length != VaultKeySize)
            {
                return Fail<AuthorizationResult>(
                    AuthorizationEnvelopeSessionErrorCode.PlatformUnlockFailed,
                    "Platform quick unlock returned invalid key material.");
            }

            var verified = await _vaultVerifier.VerifyAsync(platformKey.Memory, cancellationToken);
            if (verified.IsFailed)
            {
                return Fail<AuthorizationResult>(
                    AuthorizationEnvelopeSessionErrorCode.VaultVerificationFailed,
                    "The vault could not be verified during platform unlock.",
                    verified.Errors);
            }

            if (verified.Value is not VaultKeyVerificationStatus.Verified
                and not VaultKeyVerificationStatus.VaultNotFound)
            {
                return Fail<AuthorizationResult>(
                    AuthorizationEnvelopeSessionErrorCode.VaultVerificationFailed,
                    "The platform-recovered key did not verify the vault.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            var contextKey = platformKey.Memory.ToArray();
            try
            {
                _securityContext.SetDek(contextKey);
                return Result.Ok(AuthorizationResult.Success);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(contextKey);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Platform unlock through the authorization envelope failed unexpectedly.");
            return Fail<AuthorizationResult>(
                AuthorizationEnvelopeSessionErrorCode.UnexpectedFailure,
                "Platform unlock through the authorization envelope failed unexpectedly.",
                exception: ex);
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Dispose()
    {
        ClearCachedEnvelope();
        _lock.Dispose();
    }

    private void ClearCachedEnvelope()
    {
        if (_envelope is null) return;
        AuthorizationEnvelopeBufferCleaner.Clear(_envelope);
        _envelope = null;
    }

    private static AuthorizationResult MapPlatformOutcome(PlatformQuickUnlockStatus status) => status switch
    {
        PlatformQuickUnlockStatus.Cancelled => AuthorizationResult.Cancelled,
        PlatformQuickUnlockStatus.DisabledByPolicy => AuthorizationResult.DisabledByPolicy,
        PlatformQuickUnlockStatus.RetriesExhausted => AuthorizationResult.TooManyAttempts,
        PlatformQuickUnlockStatus.VerificationFailed => AuthorizationResult.Failed,
        PlatformQuickUnlockStatus.NotAvailable or
        PlatformQuickUnlockStatus.NotConfigured or
        PlatformQuickUnlockStatus.KeyNotFound => AuthorizationResult.PasswordRequired,
        _ => AuthorizationResult.Failed
    };

    private static Result<T> Fail<T>(
        AuthorizationEnvelopeSessionErrorCode code,
        string message,
        IEnumerable<IError>? causes = null,
        Exception? exception = null)
    {
        var errors = new List<IError>
        {
            new AuthorizationEnvelopeSessionError(code, message, exception)
        };
        if (causes is not null) errors.AddRange(causes);
        return Result.Fail<T>(errors);
    }
}
