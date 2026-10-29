using System.Security.Cryptography;
using System.Text;
using FluentResults;
using Microsoft.Extensions.Logging;
using TOTP.Core.Security.Interfaces;
using TOTP.Core.Security.Models;

namespace TOTP.Platform.MacOS.Security;

public sealed class MacOSPlatformQuickUnlock(
    IPlatformSecretStore secretStore,
    ILogger<MacOSPlatformQuickUnlock> logger) : IPlatformQuickUnlock
{
    private const int VaultKeySize = 32;
    private const string ReferencePrefix = "TOTP_KEYCHAIN_";

    public string ProviderId => PlatformQuickUnlockContract.MacOSKeychainProvider;

    public async Task<PlatformQuickUnlockAvailability> GetAvailabilityAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            return (await secretStore.GetAvailabilityAsync(cancellationToken)) switch
            {
                PlatformSecretStoreAvailability.Available => PlatformQuickUnlockAvailability.Available,
                PlatformSecretStoreAvailability.NotConfigured => PlatformQuickUnlockAvailability.NotConfigured,
                PlatformSecretStoreAvailability.DisabledByPolicy => PlatformQuickUnlockAvailability.DisabledByPolicy,
                PlatformSecretStoreAvailability.NotSupported => PlatformQuickUnlockAvailability.NotSupported,
                _ => PlatformQuickUnlockAvailability.TemporarilyUnavailable
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogFailure("availability", exception);
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

        var keyReference = $"{ReferencePrefix}{Guid.NewGuid():N}";
        var stored = await secretStore.StoreAsync(keyReference, vaultKey, cancellationToken);
        if (stored.IsFailed)
            return Fail<PlatformQuickUnlockWrapperV2>(
                MapStoreError(stored.Errors, PlatformQuickUnlockErrorCode.RegistrationFailed),
                "macOS quick unlock could not be registered.");

        return Result.Ok(new PlatformQuickUnlockWrapperV2
        {
            Provider = ProviderId,
            ProviderVersion = PlatformQuickUnlockContract.MacOSKeychainProviderVersion,
            AuthenticationPolicy = PlatformQuickUnlockContract.UserVerificationRequired,
            KeyReference = keyReference,
            WrappedKey = new PlatformWrappedKeyV2
            {
                Algorithm = PlatformQuickUnlockContract.KeychainItemReferenceAlgorithm,
                Ciphertext = CreateBinding(keyReference)
            }
        });
    }

    public async Task<Result<PlatformQuickUnlockAttempt>> TryUnlockAsync(
        PlatformQuickUnlockWrapperV2 wrapper,
        CancellationToken cancellationToken = default)
    {
        if (!IsOwnedSupportedWrapper(wrapper) || !HasValidBinding(wrapper))
            return Fail<PlatformQuickUnlockAttempt>(
                PlatformQuickUnlockErrorCode.InvalidMetadata,
                "Quick-unlock metadata is invalid.");

        var retrieved = await secretStore.RetrieveAsync(wrapper.KeyReference, cancellationToken);
        if (retrieved.IsFailed)
        {
            var code = MapStoreError(retrieved.Errors, PlatformQuickUnlockErrorCode.UnlockFailed);
            if (code == PlatformQuickUnlockErrorCode.Cancelled)
                return Result.Ok(PlatformQuickUnlockAttempt.WithoutKey(PlatformQuickUnlockStatus.Cancelled));
            return Fail<PlatformQuickUnlockAttempt>(code, "macOS quick unlock failed.");
        }

        var secret = retrieved.Value;
        if (secret is null)
            return Result.Ok(PlatformQuickUnlockAttempt.WithoutKey(PlatformQuickUnlockStatus.KeyNotFound));
        if (secret.Memory.Length != VaultKeySize)
        {
            secret.Dispose();
            return Fail<PlatformQuickUnlockAttempt>(
                PlatformQuickUnlockErrorCode.InvalidKeyMaterial,
                "The recovered vault key is invalid.");
        }

        return Result.Ok(PlatformQuickUnlockAttempt.Successful(secret));
    }

    public async Task<Result> RemoveAsync(
        PlatformQuickUnlockWrapperV2 wrapper,
        CancellationToken cancellationToken = default)
    {
        if (!IsOwnedSupportedWrapper(wrapper) || !HasValidBinding(wrapper))
            return Fail(PlatformQuickUnlockErrorCode.InvalidMetadata, "Quick-unlock metadata is invalid.");

        var deleted = await secretStore.DeleteAsync(wrapper.KeyReference, cancellationToken);
        return deleted.IsSuccess
            ? Result.Ok()
            : Fail(
                MapStoreError(deleted.Errors, PlatformQuickUnlockErrorCode.RemoveFailed),
                "macOS quick unlock could not be removed.");
    }

    private static bool IsOwnedSupportedWrapper(PlatformQuickUnlockWrapperV2? wrapper) =>
        wrapper is not null
        && string.Equals(wrapper.Provider, PlatformQuickUnlockContract.MacOSKeychainProvider, StringComparison.Ordinal)
        && PlatformQuickUnlockContract.IsSupported(wrapper);

    private static bool HasValidBinding(PlatformQuickUnlockWrapperV2 wrapper)
    {
        var expected = CreateBinding(wrapper.KeyReference);
        try
        {
            return CryptographicOperations.FixedTimeEquals(expected, wrapper.WrappedKey.Ciphertext);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(expected);
        }
    }

    private static byte[] CreateBinding(string keyReference) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(keyReference));

    private static PlatformQuickUnlockErrorCode MapStoreError(
        IReadOnlyList<IError> errors,
        PlatformQuickUnlockErrorCode fallback)
    {
        var code = errors.OfType<PlatformSecretStoreError>().FirstOrDefault()?.Code;
        return code switch
        {
            PlatformSecretStoreErrorCode.AccessDenied => PlatformQuickUnlockErrorCode.Cancelled,
            PlatformSecretStoreErrorCode.Unavailable => PlatformQuickUnlockErrorCode.Unavailable,
            PlatformSecretStoreErrorCode.InvalidSecret => PlatformQuickUnlockErrorCode.InvalidKeyMaterial,
            _ => fallback
        };
    }

    private void LogFailure(string operation, Exception exception) =>
        logger.LogWarning(
            "macOS quick-unlock operation failed safely. operation={Operation} failure_type={FailureType}",
            operation,
            exception.GetType().Name);

    private static Result Fail(PlatformQuickUnlockErrorCode code, string message) =>
        Result.Fail(new PlatformQuickUnlockError(code, message));

    private static Result<T> Fail<T>(PlatformQuickUnlockErrorCode code, string message) =>
        Result.Fail<T>(new PlatformQuickUnlockError(code, message));
}
