using System.Security.Cryptography;
using FluentResults;
using Microsoft.Extensions.Logging;
using TOTP.Core.Security.Interfaces;
using TOTP.Core.Security.Models;

namespace TOTP.Platform.Windows.Security;

public sealed class WindowsPlatformQuickUnlock : IPlatformQuickUnlock
{
    private const int VaultKeySize = 32;
    private const int RsaCiphertextSize = 256;

    private readonly IHelloGate _helloGate;
    private readonly ILogger<WindowsPlatformQuickUnlock> _logger;

    public WindowsPlatformQuickUnlock(
        IHelloGate helloGate,
        ILogger<WindowsPlatformQuickUnlock> logger)
    {
        _helloGate = helloGate ?? throw new ArgumentNullException(nameof(helloGate));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string ProviderId => PlatformQuickUnlockContract.WindowsHelloTpmProvider;

    public async Task<PlatformQuickUnlockAvailability> GetAvailabilityAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await _helloGate.GetAvailabilityAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Windows quick-unlock availability check failed.");
            return PlatformQuickUnlockAvailability.TemporarilyUnavailable;
        }
    }

    public async Task<Result<PlatformQuickUnlockWrapperV2>> RegisterAsync(
        ReadOnlyMemory<byte> vaultKey,
        CancellationToken cancellationToken = default)
    {
        if (vaultKey.Length != VaultKeySize)
            return Fail<PlatformQuickUnlockWrapperV2>(
                PlatformQuickUnlockErrorCode.InvalidKeyMaterial,
                "The vault key is invalid.");

        cancellationToken.ThrowIfCancellationRequested();
        IDisposable? prompt = null;
        try
        {
            prompt = _helloGate.BeginPromptSession();
            var verification = await _helloGate.RequestVerificationAsync(cancellationToken);
            if (verification != AuthorizationResult.Success)
            {
                prompt?.Dispose();
                return Fail<PlatformQuickUnlockWrapperV2>(MapRegistrationError(verification), "User verification did not succeed.");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            prompt?.Dispose();
            throw;
        }
        catch (Exception ex)
        {
            prompt?.Dispose();
            _logger.LogError(ex, "Windows quick-unlock verification failed during registration.");
            return Fail<PlatformQuickUnlockWrapperV2>(
                PlatformQuickUnlockErrorCode.RegistrationFailed,
                "Windows quick-unlock verification failed during registration.",
                ex);
        }

        var keyReference = $"TOTP_TPM_{Guid.NewGuid():N}";
        var ownedVaultKey = vaultKey.ToArray();
        byte[]? wrappedKey = null;
        try
        {
            wrappedKey = await _helloGate.ProtectKeyAsync(
                ownedVaultKey,
                keyReference,
                cancellationToken);
            if (wrappedKey.Length != RsaCiphertextSize)
            {
                await TryRemoveFailedRegistrationAsync(keyReference);
                return Fail<PlatformQuickUnlockWrapperV2>(
                    PlatformQuickUnlockErrorCode.InvalidKeyMaterial,
                    "The platform wrapper is invalid.");
            }

            var wrapper = new PlatformQuickUnlockWrapperV2
            {
                Provider = ProviderId,
                ProviderVersion = PlatformQuickUnlockContract.WindowsHelloTpmProviderVersion,
                AuthenticationPolicy = PlatformQuickUnlockContract.UserVerificationRequired,
                KeyReference = keyReference,
                WrappedKey = new PlatformWrappedKeyV2
                {
                    Algorithm = PlatformQuickUnlockContract.RsaOaepSha256Algorithm,
                    Ciphertext = wrappedKey
                }
            };
            wrappedKey = null;
            return Result.Ok(wrapper);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await TryRemoveFailedRegistrationAsync(keyReference);
            throw;
        }
        catch (Exception ex)
        {
            await TryRemoveFailedRegistrationAsync(keyReference);
            _logger.LogError(ex, "Windows quick-unlock registration failed.");
            return Fail<PlatformQuickUnlockWrapperV2>(
                PlatformQuickUnlockErrorCode.RegistrationFailed,
                "Windows quick-unlock registration failed.",
                ex);
        }
        finally
        {
            prompt?.Dispose();
            CryptographicOperations.ZeroMemory(ownedVaultKey);
            if (wrappedKey is not null) CryptographicOperations.ZeroMemory(wrappedKey);
        }
    }

    public async Task<Result<PlatformQuickUnlockAttempt>> TryUnlockAsync(
        PlatformQuickUnlockWrapperV2 wrapper,
        CancellationToken cancellationToken = default)
    {
        if (!PlatformQuickUnlockContract.IsSupported(wrapper))
            return Fail<PlatformQuickUnlockAttempt>(
                PlatformQuickUnlockErrorCode.InvalidMetadata,
                "Quick-unlock metadata is invalid.");

        try
        {
            using var prompt = _helloGate.BeginPromptSession();
            var verification = await _helloGate.RequestVerificationAsync(cancellationToken);
            if (verification != AuthorizationResult.Success)
                return Result.Ok(PlatformQuickUnlockAttempt.WithoutKey(MapAttemptStatus(verification)));

            var recoveredKey = await _helloGate.UnprotectKeyAsync(
                wrapper.WrappedKey.Ciphertext,
                wrapper.KeyReference,
                cancellationToken);
            if (recoveredKey is null)
                return Result.Ok(PlatformQuickUnlockAttempt.WithoutKey(PlatformQuickUnlockStatus.KeyNotFound));

            try
            {
                if (recoveredKey.Length != VaultKeySize)
                {
                    return Fail<PlatformQuickUnlockAttempt>(
                        PlatformQuickUnlockErrorCode.InvalidKeyMaterial,
                        "The recovered vault key is invalid.");
                }

                return Result.Ok(PlatformQuickUnlockAttempt.Successful(SensitiveBuffer.CopyFrom(recoveredKey)));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(recoveredKey);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Windows quick unlock failed.");
            return Fail<PlatformQuickUnlockAttempt>(
                PlatformQuickUnlockErrorCode.UnlockFailed,
                "Windows quick unlock failed.",
                ex);
        }
    }

    public async Task<Result> RemoveAsync(
        PlatformQuickUnlockWrapperV2 wrapper,
        CancellationToken cancellationToken = default)
    {
        if (!PlatformQuickUnlockContract.IsSupported(wrapper))
            return Fail(PlatformQuickUnlockErrorCode.InvalidMetadata, "Quick-unlock metadata is invalid.");

        try
        {
            await _helloGate.RemoveKeyAsync(wrapper.KeyReference, cancellationToken);
            return Result.Ok();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Windows quick-unlock removal failed.");
            return Fail(PlatformQuickUnlockErrorCode.RemoveFailed, "Windows quick-unlock removal failed.", ex);
        }
    }

    private async Task TryRemoveFailedRegistrationAsync(string keyReference)
    {
        try
        {
            await _helloGate.RemoveKeyAsync(keyReference, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to remove incomplete Windows quick-unlock registration.");
        }
    }

    private static PlatformQuickUnlockErrorCode MapRegistrationError(AuthorizationResult result) => result switch
    {
        AuthorizationResult.Cancelled => PlatformQuickUnlockErrorCode.Cancelled,
        AuthorizationResult.DisabledByPolicy => PlatformQuickUnlockErrorCode.DisabledByPolicy,
        AuthorizationResult.TooManyAttempts => PlatformQuickUnlockErrorCode.RetriesExhausted,
        AuthorizationResult.NotAvailable => PlatformQuickUnlockErrorCode.Unavailable,
        _ => PlatformQuickUnlockErrorCode.RegistrationFailed
    };

    private static PlatformQuickUnlockStatus MapAttemptStatus(AuthorizationResult result) => result switch
    {
        AuthorizationResult.Cancelled => PlatformQuickUnlockStatus.Cancelled,
        AuthorizationResult.TooManyAttempts => PlatformQuickUnlockStatus.RetriesExhausted,
        AuthorizationResult.DisabledByPolicy => PlatformQuickUnlockStatus.DisabledByPolicy,
        AuthorizationResult.NotAvailable => PlatformQuickUnlockStatus.NotAvailable,
        AuthorizationResult.NotConfigured => PlatformQuickUnlockStatus.NotConfigured,
        _ => PlatformQuickUnlockStatus.VerificationFailed
    };

    private static Result Fail(
        PlatformQuickUnlockErrorCode code,
        string message,
        Exception? exception = null) =>
        Result.Fail(new PlatformQuickUnlockError(code, message, exception));

    private static Result<T> Fail<T>(
        PlatformQuickUnlockErrorCode code,
        string message,
        Exception? exception = null) =>
        Result.Fail<T>(new PlatformQuickUnlockError(code, message, exception));
}
