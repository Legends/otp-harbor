using System.Security.Cryptography;
using System.Text;
using Android.Security.Keystore;
using FluentResults;
using Java.Security;
using Javax.Crypto;
using Javax.Crypto.Spec;
using Microsoft.Extensions.Logging;
using TOTP.Core.Enums;
using TOTP.Core.Security.Interfaces;
using TOTP.Core.Security.Models;

namespace TOTP.Platform.Android;

using Result = FluentResults.Result;

public sealed class AndroidPlatformQuickUnlock : IPlatformQuickUnlock
{
    private const int VaultKeySize = 32;
    private const int GcmTagSizeBits = 128;
    private const int GcmNonceSize = 12;
    private const int WrappedKeySize = 48;
    private const int StrongBiometricValiditySeconds = 1;
    private const string BiometricKeyAliasPrefix = "TOTP_ANDROID_BIO_";
    private const string DeviceCredentialKeyAliasPrefix = "TOTP_ANDROID_PIN_";
    private const string LegacyBiometricKeyAliasPrefix = "TOTP_ANDROID_";
    private const string AndroidKeyStore = "AndroidKeyStore";
    private const string AesGcmTransformation = "AES/GCM/NoPadding";

    private readonly IAndroidBiometricPrompt _biometricPrompt;
    private readonly ILogger<AndroidPlatformQuickUnlock> _logger;
    private readonly AndroidQuickUnlockMode _mode;

    public AndroidPlatformQuickUnlock(
        IAndroidBiometricPrompt biometricPrompt,
        ILogger<AndroidPlatformQuickUnlock> logger)
        : this(biometricPrompt, logger, AndroidQuickUnlockMode.StrongBiometric)
    {
    }

    public AndroidPlatformQuickUnlock(
        IAndroidBiometricPrompt biometricPrompt,
        ILogger<AndroidPlatformQuickUnlock> logger,
        AndroidQuickUnlockMode mode)
    {
        _biometricPrompt = biometricPrompt
            ?? throw new ArgumentNullException(nameof(biometricPrompt));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _mode = mode;
    }

    public string ProviderId => _mode == AndroidQuickUnlockMode.DeviceCredential
        ? PlatformQuickUnlockContract.AndroidKeystoreDeviceCredentialProvider
        : PlatformQuickUnlockContract.AndroidKeystoreBiometricProvider;

    public PreferredUnlockMethod UnlockMethod => _mode == AndroidQuickUnlockMode.DeviceCredential
        ? PreferredUnlockMethod.PlatformDeviceCredential
        : PreferredUnlockMethod.PlatformQuickUnlock;

    public async Task<PlatformQuickUnlockAvailability> GetAvailabilityAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await _biometricPrompt.GetAvailabilityAsync(_mode, cancellationToken);
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
        {
            return Fail<PlatformQuickUnlockWrapperV2>(
                PlatformQuickUnlockErrorCode.InvalidKeyMaterial,
                "The vault key is invalid.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        var keyReference = $"{KeyAliasPrefix}{Guid.NewGuid():N}";
        var ownedVaultKey = vaultKey.ToArray();
        byte[]? nonce = null;
        byte[]? ciphertext = null;
        try
        {
            GenerateKey(keyReference, _mode);
            using var authenticated = await _biometricPrompt.AuthenticateAsync(
                _mode,
                () =>
                {
                    using var key = GetKey(keyReference)
                        ?? throw new UnrecoverableKeyException(
                            "The generated Android key was not found.");
                    using var cipher = CreateCipher();
                    cipher.Init(Javax.Crypto.CipherMode.EncryptMode, key);
                    ApplyAssociatedData(cipher, keyReference);
                    nonce = cipher.GetIV();
                    return cipher.DoFinal(ownedVaultKey)
                        ?? throw new CryptographicException(
                            "Android AES-GCM encryption returned no output.");
                },
                cancellationToken);
            if (authenticated.Status != PlatformQuickUnlockStatus.Succeeded
                || authenticated.Output is null)
            {
                _logger.LogWarning(
                    "Android quick-unlock registration stopped after biometric prompt. status={Status}",
                    authenticated.Status);
                RemoveAliasBestEffort(keyReference);
                return Fail<PlatformQuickUnlockWrapperV2>(
                    MapRegistrationError(authenticated.Status),
                    "Android biometric verification did not succeed.");
            }

            ciphertext = authenticated.Output.ToArray();
            if (nonce is not { Length: GcmNonceSize }
                || ciphertext is not { Length: WrappedKeySize })
            {
                _logger.LogWarning(
                    "Android quick-unlock registration produced invalid cryptographic lengths. nonce_length={NonceLength} ciphertext_length={CiphertextLength}",
                    nonce?.Length ?? -1,
                    ciphertext?.Length ?? -1);
                RemoveAliasBestEffort(keyReference);
                return Fail<PlatformQuickUnlockWrapperV2>(
                    PlatformQuickUnlockErrorCode.InvalidKeyMaterial,
                    "The Android platform wrapper is invalid.");
            }

            var wrapper = new PlatformQuickUnlockWrapperV2
            {
                Provider = ProviderId,
                ProviderVersion = ProviderVersion,
                AuthenticationPolicy = PlatformQuickUnlockContract.UserVerificationRequired,
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
            return Result.Ok(wrapper);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            RemoveAliasBestEffort(keyReference);
            throw;
        }
        catch (Exception exception)
        {
            RemoveAliasBestEffort(keyReference);
            LogFailure("registration", exception);
            return Fail<PlatformQuickUnlockWrapperV2>(
                PlatformQuickUnlockErrorCode.RegistrationFailed,
                "Android biometric quick unlock could not be registered.",
                exception);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(ownedVaultKey);
            if (nonce is not null) CryptographicOperations.ZeroMemory(nonce);
            if (ciphertext is not null) CryptographicOperations.ZeroMemory(ciphertext);
        }
    }

    public async Task<Result<PlatformQuickUnlockAttempt>> TryUnlockAsync(
        PlatformQuickUnlockWrapperV2 wrapper,
        CancellationToken cancellationToken = default)
    {
        if (!IsOwnedSupportedWrapper(wrapper))
        {
            return Fail<PlatformQuickUnlockAttempt>(
                PlatformQuickUnlockErrorCode.InvalidMetadata,
                "Quick-unlock metadata is invalid.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        byte[]? recoveredKey = null;
        try
        {
            using var existingKey = GetKey(wrapper.KeyReference);
            if (existingKey is null)
            {
                return Result.Ok(PlatformQuickUnlockAttempt.WithoutKey(
                    PlatformQuickUnlockStatus.KeyNotFound));
            }

            using var authenticated = await _biometricPrompt.AuthenticateAsync(
                _mode,
                () =>
                {
                    using var key = GetKey(wrapper.KeyReference)
                        ?? throw new UnrecoverableKeyException(
                            "The Android key was not found.");
                    using var cipher = CreateCipher();
                    using var parameters = new GCMParameterSpec(
                        GcmTagSizeBits,
                        wrapper.WrappedKey.Nonce!);
                    cipher.Init(Javax.Crypto.CipherMode.DecryptMode, key, parameters);
                    ApplyAssociatedData(cipher, wrapper.KeyReference);
                    return cipher.DoFinal(wrapper.WrappedKey.Ciphertext)
                        ?? throw new CryptographicException(
                            "Android AES-GCM decryption returned no output.");
                },
                cancellationToken);
            if (authenticated.Status != PlatformQuickUnlockStatus.Succeeded
                || authenticated.Output is null)
            {
                return Result.Ok(PlatformQuickUnlockAttempt.WithoutKey(authenticated.Status));
            }

            recoveredKey = authenticated.Output.ToArray();
            if (recoveredKey is not { Length: VaultKeySize })
            {
                return Fail<PlatformQuickUnlockAttempt>(
                    PlatformQuickUnlockErrorCode.InvalidKeyMaterial,
                    "The recovered vault key is invalid.");
            }

            return Result.Ok(PlatformQuickUnlockAttempt.Successful(
                SensitiveBuffer.CopyFrom(recoveredKey)));
        }
        catch (KeyPermanentlyInvalidatedException)
        {
            RemoveAliasBestEffort(wrapper.KeyReference);
            return Result.Ok(PlatformQuickUnlockAttempt.WithoutKey(
                PlatformQuickUnlockStatus.KeyNotFound));
        }
        catch (UnrecoverableKeyException)
        {
            return Result.Ok(PlatformQuickUnlockAttempt.WithoutKey(
                PlatformQuickUnlockStatus.KeyNotFound));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (AEADBadTagException exception)
        {
            LogFailure("authentication", exception);
            return Fail<PlatformQuickUnlockAttempt>(
                PlatformQuickUnlockErrorCode.InvalidKeyMaterial,
                "Android quick-unlock ciphertext authentication failed.");
        }
        catch (Exception exception)
        {
            LogFailure("unlock", exception);
            return Fail<PlatformQuickUnlockAttempt>(
                PlatformQuickUnlockErrorCode.UnlockFailed,
                "Android biometric quick unlock failed.",
                exception);
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
        {
            return Task.FromResult(Fail(
                PlatformQuickUnlockErrorCode.InvalidMetadata,
                "Quick-unlock metadata is invalid."));
        }

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
                "Android biometric quick unlock could not be removed.",
                exception));
        }
    }

    private static void GenerateKey(string keyReference, AndroidQuickUnlockMode mode)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(30))
        {
            throw new PlatformNotSupportedException(
                "Android biometric quick unlock requires Android 11 or newer.");
        }

        using var generator = KeyGenerator.GetInstance(KeyProperties.KeyAlgorithmAes, AndroidKeyStore)
            ?? throw new CryptographicException("Android AES key generation is unavailable.");
        using var builder = new KeyGenParameterSpec.Builder(
                keyReference,
                KeyStorePurpose.Encrypt | KeyStorePurpose.Decrypt)
            .SetBlockModes(KeyProperties.BlockModeGcm)
            .SetEncryptionPaddings(KeyProperties.EncryptionPaddingNone)
            .SetKeySize(256)
            .SetRandomizedEncryptionRequired(true)
            .SetUserAuthenticationRequired(true);
        if (mode == AndroidQuickUnlockMode.StrongBiometric)
            builder.SetInvalidatedByBiometricEnrollment(true);
        builder.SetUserAuthenticationParameters(
            StrongBiometricValiditySeconds,
            mode == AndroidQuickUnlockMode.DeviceCredential
                ? (int)KeyPropertiesAuthType.DeviceCredential
                : (int)KeyPropertiesAuthType.BiometricStrong);

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

    private void ApplyAssociatedData(Cipher cipher, string keyReference)
    {
        var associatedData = Encoding.UTF8.GetBytes(
            $"{AssociatedDataContext}|{keyReference}");
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

    private bool IsOwnedSupportedWrapper(PlatformQuickUnlockWrapperV2? wrapper) =>
        wrapper is not null
        && string.Equals(
            wrapper.Provider,
            ProviderId,
            StringComparison.Ordinal)
        && (wrapper.KeyReference.StartsWith(KeyAliasPrefix, StringComparison.Ordinal)
            || _mode == AndroidQuickUnlockMode.StrongBiometric
            && wrapper.KeyReference.StartsWith(
                LegacyBiometricKeyAliasPrefix,
                StringComparison.Ordinal))
        && PlatformQuickUnlockContract.IsSupported(wrapper);

    private string KeyAliasPrefix => _mode == AndroidQuickUnlockMode.DeviceCredential
        ? DeviceCredentialKeyAliasPrefix
        : BiometricKeyAliasPrefix;

    private int ProviderVersion => _mode == AndroidQuickUnlockMode.DeviceCredential
        ? PlatformQuickUnlockContract.AndroidKeystoreDeviceCredentialProviderVersion
        : PlatformQuickUnlockContract.AndroidKeystoreBiometricProviderVersion;

    private string AssociatedDataContext => _mode == AndroidQuickUnlockMode.DeviceCredential
        ? PlatformQuickUnlockContract.AndroidDeviceCredentialAssociatedDataContext
        : PlatformQuickUnlockContract.AndroidAssociatedDataContext;

    private static PlatformQuickUnlockErrorCode MapRegistrationError(
        PlatformQuickUnlockStatus status) => status switch
        {
            PlatformQuickUnlockStatus.Cancelled => PlatformQuickUnlockErrorCode.Cancelled,
            PlatformQuickUnlockStatus.DisabledByPolicy =>
                PlatformQuickUnlockErrorCode.DisabledByPolicy,
            PlatformQuickUnlockStatus.RetriesExhausted =>
                PlatformQuickUnlockErrorCode.RetriesExhausted,
            PlatformQuickUnlockStatus.NotAvailable or PlatformQuickUnlockStatus.NotConfigured =>
                PlatformQuickUnlockErrorCode.Unavailable,
            _ => PlatformQuickUnlockErrorCode.RegistrationFailed
        };

    private void LogFailure(string operation, Exception exception) =>
        _logger.LogWarning(
            "Android quick-unlock operation failed safely. operation={Operation} failure_type={FailureType}",
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
