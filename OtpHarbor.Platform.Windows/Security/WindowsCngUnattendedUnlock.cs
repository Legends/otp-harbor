using System.Security.Cryptography;
using FluentResults;
using Microsoft.Extensions.Logging;
using TOTP.Core.Security.Interfaces;
using TOTP.Core.Security.Models;

namespace TOTP.Platform.Windows.Security;

public sealed class WindowsCngUnattendedUnlock(ILogger<WindowsCngUnattendedUnlock> logger) : IPlatformUnattendedUnlock
{
    public string ProviderId => PlatformQuickUnlockContract.WindowsCngUnattendedProvider;

    public Task<PlatformQuickUnlockAvailability> GetAvailabilityAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(OperatingSystem.IsWindows() ? PlatformQuickUnlockAvailability.Available
            : PlatformQuickUnlockAvailability.NotSupported);
    }

    public async Task<Result<PlatformQuickUnlockWrapperV2>> RegisterAsync(ReadOnlyMemory<byte> vaultKey,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows()) return Fail<PlatformQuickUnlockWrapperV2>(PlatformQuickUnlockErrorCode.Unavailable);
        if (vaultKey.Length != 32) return Fail<PlatformQuickUnlockWrapperV2>(PlatformQuickUnlockErrorCode.InvalidKeyMaterial);
        var ownedKey = vaultKey.ToArray();
        try
        {
            // RSA generation can be slow; keep it off the presentation thread.
            return await Task.Run(() => RegisterCore(ownedKey, cancellationToken), cancellationToken);
        }
        finally { CryptographicOperations.ZeroMemory(ownedKey); }
    }

    private Result<PlatformQuickUnlockWrapperV2> RegisterCore(byte[] vaultKey, CancellationToken cancellationToken)
    {
        var reference = PlatformQuickUnlockContract.DesktopUnattendedReferencePrefix + Guid.NewGuid().ToString("N");
        CngKey? key = null;
        var committed = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var parameters = new CngKeyCreationParameters
            {
                Provider = CngProvider.MicrosoftSoftwareKeyStorageProvider,
                ExportPolicy = CngExportPolicies.None,
                KeyUsage = CngKeyUsages.Decryption,
                KeyCreationOptions = CngKeyCreationOptions.None,
                UIPolicy = new CngUIPolicy(CngUIProtectionLevels.None)
            };
            parameters.Parameters.Add(new CngProperty("Length", BitConverter.GetBytes(2048), CngPropertyOptions.None));
            key = CngKey.Create(CngAlgorithm.Rsa, reference, parameters);
            using var rsa = new RSACng(key);
            var ciphertext = rsa.Encrypt(vaultKey, RSAEncryptionPadding.OaepSHA256);
            cancellationToken.ThrowIfCancellationRequested();
            var wrapper = new PlatformQuickUnlockWrapperV2
            {
                Provider = ProviderId, ProviderVersion = 1,
                AuthenticationPolicy = PlatformQuickUnlockContract.UserVerificationNotRequired,
                KeyReference = reference,
                WrappedKey = new PlatformWrappedKeyV2
                {
                    Algorithm = PlatformQuickUnlockContract.RsaOaepSha256Algorithm,
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
            if (!committed && key is not null)
            {
                try { key.Delete(); }
                catch (Exception) { logger.LogWarning("Unattended Windows key registration cleanup failed."); }
            }
            key?.Dispose();
        }
    }

    public Task<Result<PlatformQuickUnlockAttempt>> TryUnlockAsync(PlatformQuickUnlockWrapperV2 wrapper,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsOwned(wrapper)) return Task.FromResult(Fail<PlatformQuickUnlockAttempt>(PlatformQuickUnlockErrorCode.InvalidMetadata));
        if (!OperatingSystem.IsWindows()) return Task.FromResult(Fail<PlatformQuickUnlockAttempt>(PlatformQuickUnlockErrorCode.Unavailable));
        byte[]? plaintext = null;
        try
        {
            if (!CngKey.Exists(wrapper.KeyReference, CngProvider.MicrosoftSoftwareKeyStorageProvider))
                return Task.FromResult(Result.Ok(PlatformQuickUnlockAttempt.WithoutKey(PlatformQuickUnlockStatus.KeyNotFound)));
            using var key = CngKey.Open(wrapper.KeyReference, CngProvider.MicrosoftSoftwareKeyStorageProvider, CngKeyOpenOptions.Silent);
            using var rsa = new RSACng(key);
            plaintext = rsa.Decrypt(wrapper.WrappedKey.Ciphertext, RSAEncryptionPadding.OaepSHA256);
            cancellationToken.ThrowIfCancellationRequested();
            if (plaintext.Length != 32) return Task.FromResult(Fail<PlatformQuickUnlockAttempt>(PlatformQuickUnlockErrorCode.InvalidKeyMaterial));
            return Task.FromResult(Result.Ok(PlatformQuickUnlockAttempt.Successful(SensitiveBuffer.CopyFrom(plaintext))));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { return Task.FromResult(Fail<PlatformQuickUnlockAttempt>(PlatformQuickUnlockErrorCode.UnlockFailed)); }
        finally { if (plaintext is not null) CryptographicOperations.ZeroMemory(plaintext); }
    }

    public Task<Result> RemoveAsync(PlatformQuickUnlockWrapperV2 wrapper, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsOwned(wrapper)) return Task.FromResult(Result.Fail(new PlatformQuickUnlockError(
            PlatformQuickUnlockErrorCode.InvalidMetadata, "Unattended-unlock metadata is invalid.")));
        try
        {
            if (OperatingSystem.IsWindows() && CngKey.Exists(wrapper.KeyReference, CngProvider.MicrosoftSoftwareKeyStorageProvider))
            {
                using var key = CngKey.Open(wrapper.KeyReference, CngProvider.MicrosoftSoftwareKeyStorageProvider, CngKeyOpenOptions.Silent);
                key.Delete();
            }
            return Task.FromResult(Result.Ok());
        }
        catch (Exception) { return Task.FromResult(Result.Fail(new PlatformQuickUnlockError(
            PlatformQuickUnlockErrorCode.RemoveFailed, "Unattended-unlock removal failed."))); }
    }

    private bool IsOwned(PlatformQuickUnlockWrapperV2? wrapper) => wrapper?.Provider == ProviderId
        && PlatformQuickUnlockContract.IsSupportedDesktopUnattendedWrapper(wrapper);
    private static Result<T> Fail<T>(PlatformQuickUnlockErrorCode code) =>
        Result.Fail<T>(new PlatformQuickUnlockError(code, "The unattended Windows key operation failed safely."));
}
