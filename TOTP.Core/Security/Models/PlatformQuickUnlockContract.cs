using TOTP.Core.Enums;

namespace TOTP.Core.Security.Models;

/// <summary>
/// Stable wire identifiers understood by reviewed platform quick-unlock adapters.
/// New providers and algorithms require explicit validation and security review.
/// </summary>
public static class PlatformQuickUnlockContract
{
    private const int WindowsRsa2048CiphertextSize = 256;
    private const int MacOSKeychainBindingSize = 32;
    private const int AndroidAesGcmNonceSize = 12;
    private const int AndroidAesGcmCiphertextSize = 48;
    private const int MaxKeyReferenceLength = 256;

    public const string WindowsHelloTpmProvider = "windows-hello-tpm";
    public const int WindowsHelloTpmProviderVersion = 1;
    public const string UserVerificationRequired = "user-verification-required";
    public const string RsaOaepSha256Algorithm = "rsa-oaep-sha256";
    public const string MacOSKeychainProvider = "macos-keychain-user-presence";
    public const int MacOSKeychainProviderVersion = 1;
    public const string KeychainItemReferenceAlgorithm = "keychain-item-reference-sha256";
    public const string AndroidKeystoreBiometricProvider = "android-keystore-biometric";
    public const int AndroidKeystoreBiometricProviderVersion = 1;
    public const string AndroidAes256GcmAlgorithm = "aes-256-gcm";
    public const string AndroidAssociatedDataContext =
        "totp-manager/authorization-envelope/v2/android-keystore-biometric";
    public const string AndroidKeystoreDeviceCredentialProvider =
        "android-keystore-device-credential";
    public const int AndroidKeystoreDeviceCredentialProviderVersion = 1;
    public const string AndroidDeviceCredentialAssociatedDataContext =
        "totp-manager/authorization-envelope/v2/android-keystore-device-credential";
    public const string AndroidKeystoreUnattendedProvider = "android-keystore-unattended";
    public const int AndroidKeystoreUnattendedProviderVersion = 1;
    public const string UserVerificationNotRequired = "user-verification-not-required";
    public const string AndroidUnattendedAssociatedDataContext =
        "totp-manager/authorization-envelope/v2/android-keystore-unattended";

    public static bool IsSupported(PlatformQuickUnlockWrapperV2? wrapper)
    {
        if (wrapper?.WrappedKey is null)
        {
            return false;
        }

        return IsSupportedWindowsWrapper(wrapper)
            || IsSupportedMacOSWrapper(wrapper)
            || IsSupportedAndroidWrapper(wrapper)
            || IsSupportedAndroidDeviceCredentialWrapper(wrapper)
            || IsSupportedAndroidUnattendedWrapper(wrapper);
    }

    public static PreferredUnlockMethod? GetUnlockMethod(
        PlatformQuickUnlockWrapperV2? wrapper)
    {
        if (!IsSupported(wrapper) || IsSupportedAndroidUnattendedWrapper(wrapper))
            return null;

        return string.Equals(
            wrapper!.Provider,
            AndroidKeystoreDeviceCredentialProvider,
            StringComparison.Ordinal)
                ? PreferredUnlockMethod.PlatformDeviceCredential
                : PreferredUnlockMethod.PlatformQuickUnlock;
    }

    private static bool IsSupportedWindowsWrapper(PlatformQuickUnlockWrapperV2 wrapper) =>
        string.Equals(wrapper.Provider, WindowsHelloTpmProvider, StringComparison.Ordinal)
            && wrapper.ProviderVersion == WindowsHelloTpmProviderVersion
            && string.Equals(wrapper.AuthenticationPolicy, UserVerificationRequired, StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(wrapper.KeyReference)
            && wrapper.KeyReference.Length <= MaxKeyReferenceLength
            && !wrapper.KeyReference.Any(char.IsControl)
            && string.Equals(wrapper.WrappedKey.Algorithm, RsaOaepSha256Algorithm, StringComparison.Ordinal)
            && wrapper.WrappedKey.Nonce is null
            && wrapper.WrappedKey.Ciphertext is { Length: WindowsRsa2048CiphertextSize };

    private static bool IsSupportedMacOSWrapper(PlatformQuickUnlockWrapperV2 wrapper) =>
        string.Equals(wrapper.Provider, MacOSKeychainProvider, StringComparison.Ordinal)
        && wrapper.ProviderVersion == MacOSKeychainProviderVersion
        && string.Equals(wrapper.AuthenticationPolicy, UserVerificationRequired, StringComparison.Ordinal)
        && !string.IsNullOrWhiteSpace(wrapper.KeyReference)
        && wrapper.KeyReference.Length <= MaxKeyReferenceLength
        && !wrapper.KeyReference.Any(char.IsControl)
        && string.Equals(
            wrapper.WrappedKey.Algorithm,
            KeychainItemReferenceAlgorithm,
            StringComparison.Ordinal)
        && wrapper.WrappedKey.Nonce is null
        && wrapper.WrappedKey.Ciphertext is { Length: MacOSKeychainBindingSize };

    private static bool IsSupportedAndroidWrapper(PlatformQuickUnlockWrapperV2 wrapper) =>
        string.Equals(wrapper.Provider, AndroidKeystoreBiometricProvider, StringComparison.Ordinal)
        && wrapper.ProviderVersion == AndroidKeystoreBiometricProviderVersion
        && string.Equals(wrapper.AuthenticationPolicy, UserVerificationRequired, StringComparison.Ordinal)
        && !string.IsNullOrWhiteSpace(wrapper.KeyReference)
        && wrapper.KeyReference.Length <= MaxKeyReferenceLength
        && !wrapper.KeyReference.Any(char.IsControl)
        && string.Equals(
            wrapper.WrappedKey.Algorithm,
            AndroidAes256GcmAlgorithm,
            StringComparison.Ordinal)
        && wrapper.WrappedKey.Nonce is { Length: AndroidAesGcmNonceSize }
        && wrapper.WrappedKey.Ciphertext is { Length: AndroidAesGcmCiphertextSize };

    private static bool IsSupportedAndroidDeviceCredentialWrapper(
        PlatformQuickUnlockWrapperV2 wrapper) =>
        string.Equals(
            wrapper.Provider,
            AndroidKeystoreDeviceCredentialProvider,
            StringComparison.Ordinal)
        && wrapper.ProviderVersion == AndroidKeystoreDeviceCredentialProviderVersion
        && string.Equals(
            wrapper.AuthenticationPolicy,
            UserVerificationRequired,
            StringComparison.Ordinal)
        && !string.IsNullOrWhiteSpace(wrapper.KeyReference)
        && wrapper.KeyReference.Length <= MaxKeyReferenceLength
        && !wrapper.KeyReference.Any(char.IsControl)
        && string.Equals(
            wrapper.WrappedKey.Algorithm,
            AndroidAes256GcmAlgorithm,
            StringComparison.Ordinal)
        && wrapper.WrappedKey.Nonce is { Length: AndroidAesGcmNonceSize }
        && wrapper.WrappedKey.Ciphertext is { Length: AndroidAesGcmCiphertextSize };

    public static bool IsSupportedAndroidUnattendedWrapper(PlatformQuickUnlockWrapperV2? wrapper) =>
        wrapper is not null
        && string.Equals(wrapper.Provider, AndroidKeystoreUnattendedProvider, StringComparison.Ordinal)
        && wrapper.ProviderVersion == AndroidKeystoreUnattendedProviderVersion
        && string.Equals(wrapper.AuthenticationPolicy, UserVerificationNotRequired, StringComparison.Ordinal)
        && !string.IsNullOrWhiteSpace(wrapper.KeyReference)
        && wrapper.KeyReference.Length <= MaxKeyReferenceLength
        && !wrapper.KeyReference.Any(char.IsControl)
        && string.Equals(
            wrapper.WrappedKey.Algorithm,
            AndroidAes256GcmAlgorithm,
            StringComparison.Ordinal)
        && wrapper.WrappedKey.Nonce is { Length: AndroidAesGcmNonceSize }
        && wrapper.WrappedKey.Ciphertext is { Length: AndroidAesGcmCiphertextSize };
}
