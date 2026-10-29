using System.Security.Cryptography;
using System.Text;
using Android.Security.Keystore;
using FluentResults;
using Java.Security;
using Javax.Crypto;
using Javax.Crypto.Spec;
using Microsoft.Extensions.Logging;
using TOTP.Core.Security.Interfaces;
using TOTP.Core.Security.Models;

namespace TOTP.Platform.Android;

using Result = FluentResults.Result;

public sealed class AndroidPlatformUnattendedUnlock : IPlatformUnattendedUnlock
{
    private const int VaultKeySize = 32;
    private const int GcmTagSizeBits = 128;
    private const int GcmNonceSize = 12;
    private const int WrappedKeySize = 48;
    private const string KeyAliasPrefix = "TOTP_ANDROID_UNATTENDED_";
    private const string AndroidKeyStore = "AndroidKeyStore";
    private const string AesGcmTransformation = "AES/GCM/NoPadding";

    private readonly ILogger<AndroidPlatformUnattendedUnlock> _logger;

    public AndroidPlatformUnattendedUnlock(
        ILogger<AndroidPlatformUnattendedUnlock> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string ProviderId => PlatformQuickUnlockContract.AndroidKeystoreUnattendedProvider;

    public Task<PlatformQuickUnlockAvailability> GetAvailabilityAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(PlatformQuickUnlockAvailability.Available);
    }

    public Task<Result<PlatformQuickUnlockWrapperV2>> RegisterAsync(
        ReadOnlyMemory<byte> vaultKey,
        CancellationToken cancellationToken = default)
    {
        if (vaultKey.Length != VaultKeySize)
            return Task.FromResult(Fail<PlatformQuickUnlockWrapperV2>(
                PlatformQuickUnlockErrorCode.InvalidKeyMaterial,
                "The vault key is invalid."));

        cancellationToken.ThrowIfCancellationRequested();
        var keyReference = $"{KeyAliasPrefix}{Guid.NewGuid():N}";
        var ownedVaultKey = vaultKey.ToArray();
        byte[]? nonce = null;
        byte[]? ciphertext = null;
        try
        {
            GenerateKey(keyReference);
            using var key = GetKey(keyReference)
                ?? throw new UnrecoverableKeyException("The generated Android key was not found.");
            using var cipher = CreateCipher();
            cipher.Init(Javax.Crypto.CipherMode.EncryptMode, key);
            ApplyAssociatedData(cipher, keyReference);
            nonce = cipher.GetIV();
            ciphertext = cipher.DoFinal(ownedVaultKey)
                ?? throw new CryptographicException("Android AES-GCM encryption returned no output.");

            if (nonce is not { Length: GcmNonceSize }
                || ciphertext is not { Length: WrappedKeySize })
            {
                RemoveAliasBestEffort(keyReference);
                return Task.FromResult(Fail<PlatformQuickUnlockWrapperV2>(
                    PlatformQuickUnlockErrorCode.InvalidKeyMaterial,
                    "The Android unattended wrapper is invalid."));
            }

            var wrapper = new PlatformQuickUnlockWrapperV2
            {
                Provider = ProviderId,
                ProviderVersion =
                    PlatformQuickUnlockContract.AndroidKeystoreUnattendedProviderVersion,
                AuthenticationPolicy =
                    PlatformQuickUnlockContract.UserVerificationNotRequired,
                KeyReference = keyReference,
                WrappedKey = new PlatformWrappedKeyV2
                {
                    Algorithm = PlatformQuickUnlockContract.AndroidAes256GcmAlgorithm,
                    Nonce = nonce,
                    Ciphertext = ciphertext
                }
            };
            nonce = null;
            ciphertext = null;
            return Task.FromResult(Result.Ok(wrapper));
        }
        catch (Exception exception)
        {
            RemoveAliasBestEffort(keyReference);
            LogFailure("registration", exception);
            return Task.FromResult(Fail<PlatformQuickUnlockWrapperV2>(
                PlatformQuickUnlockErrorCode.RegistrationFailed,
                "Android unattended unlock could not be registered.",
                exception));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(ownedVaultKey);
            if (nonce is not null) CryptographicOperations.ZeroMemory(nonce);
            if (ciphertext is not null) CryptographicOperations.ZeroMemory(ciphertext);
        }
    }

    public Task<Result<PlatformQuickUnlockAttempt>> TryUnlockAsync(
        PlatformQuickUnlockWrapperV2 wrapper,
        CancellationToken cancellationToken = default)
    {
        if (!IsOwnedSupportedWrapper(wrapper))
            return Task.FromResult(Fail<PlatformQuickUnlockAttempt>(
                PlatformQuickUnlockErrorCode.InvalidMetadata,
                "Unattended-unlock metadata is invalid."));

        cancellationToken.ThrowIfCancellationRequested();
        byte[]? recoveredKey = null;
        try
        {
            using var key = GetKey(wrapper.KeyReference);
            if (key is null)
                return Task.FromResult(Result.Ok(
                    PlatformQuickUnlockAttempt.WithoutKey(
                        PlatformQuickUnlockStatus.KeyNotFound)));

            using var cipher = CreateCipher();
            using var parameters = new GCMParameterSpec(
                GcmTagSizeBits,
                wrapper.WrappedKey.Nonce!);
            cipher.Init(Javax.Crypto.CipherMode.DecryptMode, key, parameters);
            ApplyAssociatedData(cipher, wrapper.KeyReference);
            recoveredKey = cipher.DoFinal(wrapper.WrappedKey.Ciphertext)
                ?? throw new CryptographicException("Android AES-GCM decryption returned no output.");
            if (recoveredKey is not { Length: VaultKeySize })
                return Task.FromResult(Fail<PlatformQuickUnlockAttempt>(
                    PlatformQuickUnlockErrorCode.InvalidKeyMaterial,
                    "The recovered vault key is invalid."));

            return Task.FromResult(Result.Ok(
                PlatformQuickUnlockAttempt.Successful(
                    SensitiveBuffer.CopyFrom(recoveredKey))));
        }
        catch (UnrecoverableKeyException)
        {
            return Task.FromResult(Result.Ok(
                PlatformQuickUnlockAttempt.WithoutKey(
                    PlatformQuickUnlockStatus.KeyNotFound)));
        }
        catch (AEADBadTagException exception)
        {
            LogFailure("authentication", exception);
            return Task.FromResult(Fail<PlatformQuickUnlockAttempt>(
                PlatformQuickUnlockErrorCode.InvalidKeyMaterial,
                "Android unattended ciphertext authentication failed."));
        }
        catch (Exception exception)
        {
            LogFailure("unlock", exception);
            return Task.FromResult(Fail<PlatformQuickUnlockAttempt>(
                PlatformQuickUnlockErrorCode.UnlockFailed,
                "Android unattended unlock failed.",
                exception));
        }
        finally
        {
            if (recoveredKey is not null) CryptographicOperations.ZeroMemory(recoveredKey);
        }
    }

    public Task<Result> RemoveAsync(
        PlatformQuickUnlockWrapperV2 wrapper,
        CancellationToken cancellationToken = default)
    {
        if (!IsOwnedSupportedWrapper(wrapper))
            return Task.FromResult(Fail(
                PlatformQuickUnlockErrorCode.InvalidMetadata,
                "Unattended-unlock metadata is invalid."));

        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            DeleteKey(wrapper.KeyReference);
            return Task.FromResult(Result.Ok());
        }
        catch (Exception exception)
        {
            LogFailure("removal", exception);
            return Task.FromResult(Fail(
                PlatformQuickUnlockErrorCode.RemoveFailed,
                "Android unattended unlock could not be removed.",
                exception));
        }
    }

    private static void GenerateKey(string keyReference)
    {
        using var generator = KeyGenerator.GetInstance(
            KeyProperties.KeyAlgorithmAes,
            AndroidKeyStore)
            ?? throw new CryptographicException("Android AES key generation is unavailable.");
        using var builder = new KeyGenParameterSpec.Builder(
                keyReference,
                KeyStorePurpose.Encrypt | KeyStorePurpose.Decrypt)
            .SetBlockModes(KeyProperties.BlockModeGcm)
            .SetEncryptionPaddings(KeyProperties.EncryptionPaddingNone)
            .SetKeySize(256)
            .SetRandomizedEncryptionRequired(true)
            .SetUserAuthenticationRequired(false);
        using var specification = builder.Build();
        generator.Init(specification);
        using var generatedKey = generator.GenerateKey();
    }

    private static Java.Security.IKey? GetKey(string keyReference)
    {
        using var keyStore = LoadKeyStore();
        return keyStore.GetKey(keyReference, null);
    }

    private static void DeleteKey(string keyReference)
    {
        using var keyStore = LoadKeyStore();
        if (keyStore.ContainsAlias(keyReference)) keyStore.DeleteEntry(keyReference);
    }

    private static KeyStore LoadKeyStore()
    {
        var keyStore = KeyStore.GetInstance(AndroidKeyStore)
            ?? throw new CryptographicException("Android Keystore is unavailable.");
        keyStore.Load(null);
        return keyStore;
    }

    private static Cipher CreateCipher() =>
        Cipher.GetInstance(AesGcmTransformation)
        ?? throw new CryptographicException("Android AES-GCM is unavailable.");

    private static void ApplyAssociatedData(Cipher cipher, string keyReference)
    {
        var associatedData = Encoding.UTF8.GetBytes(
            $"{PlatformQuickUnlockContract.AndroidUnattendedAssociatedDataContext}|{keyReference}");
        try
        {
            cipher.UpdateAAD(associatedData);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(associatedData);
        }
    }

    private void RemoveAliasBestEffort(string keyReference)
    {
        try
        {
            DeleteKey(keyReference);
        }
        catch (Exception exception)
        {
            LogFailure("registration-cleanup", exception);
        }
    }

    private static bool IsOwnedSupportedWrapper(PlatformQuickUnlockWrapperV2? wrapper) =>
        wrapper is not null
        && wrapper.KeyReference.StartsWith(KeyAliasPrefix, StringComparison.Ordinal)
        && PlatformQuickUnlockContract.IsSupportedAndroidUnattendedWrapper(wrapper);

    private void LogFailure(string operation, Exception exception) =>
        _logger.LogWarning(
            "Android unattended-unlock operation failed safely. operation={Operation} failure_type={FailureType}",
            operation,
            exception.GetType().Name);

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
