using System.Security.Cryptography;
using System.Text;
using FluentResults;
using Microsoft.Extensions.Logging;
using TOTP.Core.Security.Interfaces;
using TOTP.Core.Security.Models;

namespace TOTP.Infrastructure.Security;

public sealed class SecretStoreUnattendedUnlock : IPlatformUnattendedUnlock
{
    private readonly IPlatformSecretStore _store;
    private readonly ILogger<SecretStoreUnattendedUnlock> _logger;

    public SecretStoreUnattendedUnlock(IPlatformSecretStore store, string providerId,
        ILogger<SecretStoreUnattendedUnlock> logger)
    {
        if (providerId is not (PlatformQuickUnlockContract.MacOSUnattendedProvider
            or PlatformQuickUnlockContract.LinuxUnattendedProvider))
            throw new ArgumentOutOfRangeException(nameof(providerId));
        _store = store;
        ProviderId = providerId;
        _logger = logger;
    }

    public string ProviderId { get; }

    public async Task<PlatformQuickUnlockAvailability> GetAvailabilityAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _store.GetAvailabilityAsync(cancellationToken) switch
            {
                PlatformSecretStoreAvailability.Available => PlatformQuickUnlockAvailability.Available,
                PlatformSecretStoreAvailability.NotSupported => PlatformQuickUnlockAvailability.NotSupported,
                PlatformSecretStoreAvailability.NotConfigured => PlatformQuickUnlockAvailability.NotConfigured,
                PlatformSecretStoreAvailability.DisabledByPolicy => PlatformQuickUnlockAvailability.DisabledByPolicy,
                _ => PlatformQuickUnlockAvailability.TemporarilyUnavailable
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { return PlatformQuickUnlockAvailability.TemporarilyUnavailable; }
    }

    public async Task<Result<PlatformQuickUnlockWrapperV2>> RegisterAsync(ReadOnlyMemory<byte> vaultKey,
        CancellationToken cancellationToken = default)
    {
        if (vaultKey.Length != 32)
            return Fail<PlatformQuickUnlockWrapperV2>(PlatformQuickUnlockErrorCode.InvalidKeyMaterial);
        var reference = PlatformQuickUnlockContract.DesktopUnattendedReferencePrefix + Guid.NewGuid().ToString("N");
        var wrappingKey = RandomNumberGenerator.GetBytes(32);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var ciphertext = new byte[48];
        var committed = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            using (var aes = new AesGcm(wrappingKey, 16))
                aes.Encrypt(nonce, vaultKey.Span, ciphertext.AsSpan(0, 32), ciphertext.AsSpan(32, 16), Binding(reference));
            // Unreferenced OS items must contain only random wrapping material,
            // not a usable vault key if cleanup or envelope persistence fails.
            var stored = await _store.StoreAsync(reference, wrappingKey, cancellationToken);
            if (stored.IsFailed) return Fail<PlatformQuickUnlockWrapperV2>(PlatformQuickUnlockErrorCode.RegistrationFailed);
            cancellationToken.ThrowIfCancellationRequested();
            var wrapper = new PlatformQuickUnlockWrapperV2
            {
                Provider = ProviderId,
                ProviderVersion = 1,
                AuthenticationPolicy = PlatformQuickUnlockContract.UserVerificationNotRequired,
                KeyReference = reference,
                WrappedKey = new PlatformWrappedKeyV2
                {
                    Algorithm = AesGcmWrappedKeyV2.AlgorithmIdentifier,
                    Nonce = nonce,
                    Ciphertext = ciphertext
                }
            };
            committed = true;
            return Result.Ok(wrapper);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { return Fail<PlatformQuickUnlockWrapperV2>(PlatformQuickUnlockErrorCode.RegistrationFailed); }
        finally
        {
            CryptographicOperations.ZeroMemory(wrappingKey);
            if (!committed)
            {
                CryptographicOperations.ZeroMemory(ciphertext);
                CryptographicOperations.ZeroMemory(nonce);
                try
                {
                    if ((await _store.DeleteAsync(reference, CancellationToken.None)).IsFailed)
                        _logger.LogWarning("Unattended secret-store registration cleanup failed.");
                }
                catch (Exception) { _logger.LogWarning("Unattended secret-store registration cleanup failed."); }
            }
        }
    }

    public async Task<Result<PlatformQuickUnlockAttempt>> TryUnlockAsync(PlatformQuickUnlockWrapperV2 wrapper,
        CancellationToken cancellationToken = default)
    {
        if (!IsOwned(wrapper)) return Fail<PlatformQuickUnlockAttempt>(PlatformQuickUnlockErrorCode.InvalidMetadata);
        SensitiveBuffer? secret = null;
        byte[]? vaultKey = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var retrieved = await _store.RetrieveAsync(wrapper.KeyReference, cancellationToken);
            if (retrieved.IsFailed) return Fail<PlatformQuickUnlockAttempt>(PlatformQuickUnlockErrorCode.UnlockFailed);
            secret = retrieved.Value;
            cancellationToken.ThrowIfCancellationRequested();
            if (secret is null)
                return Result.Ok(PlatformQuickUnlockAttempt.WithoutKey(PlatformQuickUnlockStatus.KeyNotFound));
            if (secret.Length != 32) return Fail<PlatformQuickUnlockAttempt>(PlatformQuickUnlockErrorCode.InvalidKeyMaterial);
            vaultKey = new byte[32];
            using (var aes = new AesGcm(secret.Memory.Span, 16))
                aes.Decrypt(wrapper.WrappedKey.Nonce!, wrapper.WrappedKey.Ciphertext.AsSpan(0, 32),
                    wrapper.WrappedKey.Ciphertext.AsSpan(32, 16), vaultKey, Binding(wrapper.KeyReference));
            var attempt = PlatformQuickUnlockAttempt.Successful(SensitiveBuffer.CopyFrom(vaultKey));
            return Result.Ok(attempt);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { return Fail<PlatformQuickUnlockAttempt>(PlatformQuickUnlockErrorCode.UnlockFailed); }
        finally
        {
            secret?.Dispose();
            if (vaultKey is not null) CryptographicOperations.ZeroMemory(vaultKey);
        }
    }

    public async Task<Result> RemoveAsync(PlatformQuickUnlockWrapperV2 wrapper, CancellationToken cancellationToken = default)
    {
        if (!IsOwned(wrapper)) return Result.Fail(new PlatformQuickUnlockError(
            PlatformQuickUnlockErrorCode.InvalidMetadata, "Unattended-unlock metadata is invalid."));
        try { return await _store.DeleteAsync(wrapper.KeyReference, cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { return Result.Fail(new PlatformQuickUnlockError(
            PlatformQuickUnlockErrorCode.RemoveFailed, "Unattended-unlock removal failed.")); }
    }

    private bool IsOwned(PlatformQuickUnlockWrapperV2? wrapper)
    {
        return wrapper?.Provider == ProviderId && PlatformQuickUnlockContract.IsSupportedDesktopUnattendedWrapper(wrapper);
    }

    private byte[] Binding(string reference) => Encoding.UTF8.GetBytes(
        PlatformQuickUnlockContract.DesktopUnattendedAssociatedDataContext + "/" + ProviderId + "/" + reference);
    private static Result<T> Fail<T>(PlatformQuickUnlockErrorCode code) =>
        Result.Fail<T>(new PlatformQuickUnlockError(code, "The unattended secret-store operation failed safely."));
}
