using System.Security.Cryptography;
using FluentResults;
using Microsoft.Extensions.Logging;
using TOTP.Core.Security.Interfaces;
using TOTP.Core.Security.Models;

namespace TOTP.Platform.MacOS.Security;

public sealed class MacOSKeychainSecretStore(
    IMacOSKeychainNative native,
    ILogger<MacOSKeychainSecretStore> logger) : IPlatformSecretStore
{
    private const int MaximumReferenceLength = 256;
    private const int MaximumSecretLength = 4096;

    public string ProviderId => PlatformQuickUnlockContract.MacOSKeychainProvider;

    public Task<PlatformSecretStoreAvailability> GetAvailabilityAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsMacOS())
            return Task.FromResult(PlatformSecretStoreAvailability.NotSupported);
        try
        {
            return Task.FromResult(native.GetAvailability());
        }
        catch (Exception exception)
        {
            LogFailure("availability", exception);
            return Task.FromResult(PlatformSecretStoreAvailability.TemporarilyUnavailable);
        }
    }

    public async Task<Result> StoreAsync(
        string secretReference,
        ReadOnlyMemory<byte> secret,
        CancellationToken cancellationToken = default)
    {
        if (!IsValidReference(secretReference))
            return Fail(PlatformSecretStoreErrorCode.InvalidReference, "The Keychain reference is invalid.");
        if (secret.IsEmpty || secret.Length > MaximumSecretLength)
            return Fail(PlatformSecretStoreErrorCode.InvalidSecret, "The Keychain secret is invalid.");

        cancellationToken.ThrowIfCancellationRequested();
        var ownedSecret = secret.ToArray();
        try
        {
            var status = await Task.Run(
                () => native.Store(secretReference, ownedSecret),
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return status == MacOSKeychainNativeStatus.Success
                ? Result.Ok()
                : Fail(MapError(status, PlatformSecretStoreErrorCode.StoreFailed),
                    "The Keychain secret could not be stored.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogFailure("store", exception);
            return Fail(PlatformSecretStoreErrorCode.StoreFailed, "The Keychain secret could not be stored.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(ownedSecret);
        }
    }

    public async Task<Result<SensitiveBuffer?>> RetrieveAsync(
        string secretReference,
        CancellationToken cancellationToken = default)
    {
        if (!IsValidReference(secretReference))
            return Fail<SensitiveBuffer?>(
                PlatformSecretStoreErrorCode.InvalidReference,
                "The Keychain reference is invalid.");

        cancellationToken.ThrowIfCancellationRequested();
        byte[]? secret = null;
        try
        {
            var result = await Task.Run(() => native.Retrieve(secretReference), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            secret = result.Secret;
            if (result.Status == MacOSKeychainNativeStatus.NotFound)
                return Result.Ok<SensitiveBuffer?>(null);
            if (result.Status != MacOSKeychainNativeStatus.Success || secret is null)
                return Fail<SensitiveBuffer?>(
                    MapError(result.Status, PlatformSecretStoreErrorCode.RetrieveFailed),
                    "The Keychain secret could not be retrieved.");
            if (secret.Length is <= 0 or > MaximumSecretLength)
                return Fail<SensitiveBuffer?>(
                    PlatformSecretStoreErrorCode.InvalidSecret,
                    "The Keychain returned invalid secret material.");

            return Result.Ok<SensitiveBuffer?>(SensitiveBuffer.CopyFrom(secret));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogFailure("retrieve", exception);
            return Fail<SensitiveBuffer?>(
                PlatformSecretStoreErrorCode.RetrieveFailed,
                "The Keychain secret could not be retrieved.");
        }
        finally
        {
            if (secret is not null) CryptographicOperations.ZeroMemory(secret);
        }
    }

    public async Task<Result> DeleteAsync(
        string secretReference,
        CancellationToken cancellationToken = default)
    {
        if (!IsValidReference(secretReference))
            return Fail(PlatformSecretStoreErrorCode.InvalidReference, "The Keychain reference is invalid.");

        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var status = await Task.Run(() => native.Delete(secretReference), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return status is MacOSKeychainNativeStatus.Success or MacOSKeychainNativeStatus.NotFound
                ? Result.Ok()
                : Fail(MapError(status, PlatformSecretStoreErrorCode.DeleteFailed),
                    "The Keychain secret could not be removed.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogFailure("delete", exception);
            return Fail(PlatformSecretStoreErrorCode.DeleteFailed, "The Keychain secret could not be removed.");
        }
    }

    private static bool IsValidReference(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length <= MaximumReferenceLength
        && !value.Any(char.IsControl);

    private static PlatformSecretStoreErrorCode MapError(
        MacOSKeychainNativeStatus status,
        PlatformSecretStoreErrorCode fallback) => status switch
        {
            MacOSKeychainNativeStatus.Cancelled or MacOSKeychainNativeStatus.AccessDenied =>
                PlatformSecretStoreErrorCode.AccessDenied,
            MacOSKeychainNativeStatus.NotConfigured or
                MacOSKeychainNativeStatus.NotSupported or
                MacOSKeychainNativeStatus.TemporarilyUnavailable => PlatformSecretStoreErrorCode.Unavailable,
            _ => fallback
        };

    private void LogFailure(string operation, Exception exception) =>
        logger.LogWarning(
            "macOS Keychain operation failed safely. operation={Operation} failure_type={FailureType}",
            operation,
            exception.GetType().Name);

    private static Result Fail(PlatformSecretStoreErrorCode code, string message) =>
        Result.Fail(new PlatformSecretStoreError(code, message));

    private static Result<T> Fail<T>(PlatformSecretStoreErrorCode code, string message) =>
        Result.Fail<T>(new PlatformSecretStoreError(code, message));
}
