using TOTP.Core.Security.Interfaces;
using TOTP.Core.Security.Models;
using TOTP.Core.Enums;

namespace TOTP.Tests.Security;

public sealed class PlatformQuickUnlockContractTests
{
    [Fact]
    public void Availability_DefaultValue_IsNotAvailable()
    {
        Assert.Equal(PlatformQuickUnlockAvailability.Unknown, default);
        Assert.NotEqual(
            PlatformQuickUnlockAvailability.Available,
            default(PlatformQuickUnlockAvailability));
    }

    [Fact]
    public void SuccessfulAttempt_OwnsAndClearsRecoveredVaultKey()
    {
        var keyBytes = Enumerable.Range(1, 32).Select(value => (byte)value).ToArray();
        var vaultKey = SensitiveBuffer.CopyFrom(keyBytes);
        keyBytes.AsSpan().Clear();
        var retainedView = vaultKey.Memory;
        var attempt = PlatformQuickUnlockAttempt.Successful(vaultKey);

        Assert.True(attempt.IsSuccess);
        Assert.Equal(PlatformQuickUnlockStatus.Succeeded, attempt.Status);
        Assert.Same(vaultKey, attempt.VaultKey);

        attempt.Dispose();

        Assert.All(retainedView.ToArray(), value => Assert.Equal(0, value));
        Assert.Throws<ObjectDisposedException>(() => _ = attempt.VaultKey);
        attempt.Dispose();
    }

    [Fact]
    public void SuccessfulAttempt_WhenVaultKeyLengthIsInvalid_Throws()
    {
        using var vaultKey = SensitiveBuffer.CopyFrom(new byte[16]);

        Assert.Throws<ArgumentException>(() =>
            PlatformQuickUnlockAttempt.Successful(vaultKey));
    }

    [Theory]
    [InlineData(PlatformQuickUnlockStatus.Cancelled)]
    [InlineData(PlatformQuickUnlockStatus.NotAvailable)]
    [InlineData(PlatformQuickUnlockStatus.NotConfigured)]
    [InlineData(PlatformQuickUnlockStatus.DisabledByPolicy)]
    [InlineData(PlatformQuickUnlockStatus.RetriesExhausted)]
    [InlineData(PlatformQuickUnlockStatus.VerificationFailed)]
    [InlineData(PlatformQuickUnlockStatus.KeyNotFound)]
    public void WithoutKey_ForExpectedOutcome_HasNoVaultKey(PlatformQuickUnlockStatus status)
    {
        using var attempt = PlatformQuickUnlockAttempt.WithoutKey(status);

        Assert.False(attempt.IsSuccess);
        Assert.Equal(status, attempt.Status);
        Assert.Null(attempt.VaultKey);
    }

    [Theory]
    [InlineData(PlatformQuickUnlockStatus.Unknown)]
    [InlineData(PlatformQuickUnlockStatus.Succeeded)]
    public void WithoutKey_ForInvalidOutcome_Throws(PlatformQuickUnlockStatus status)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PlatformQuickUnlockAttempt.WithoutKey(status));
    }

    [Fact]
    public void PlatformQuickUnlockError_PreservesTypedCode()
    {
        var error = new PlatformQuickUnlockError(
            PlatformQuickUnlockErrorCode.InvalidMetadata,
            "Quick-unlock metadata is invalid.");

        Assert.Equal(PlatformQuickUnlockErrorCode.InvalidMetadata, error.Code);
        Assert.Equal("Quick-unlock metadata is invalid.", error.Message);
    }

    [Fact]
    public void Contract_UsesOwnedAttemptForRecoveredKeyMaterial()
    {
        var unlockMethod = typeof(IPlatformQuickUnlock).GetMethod(
            nameof(IPlatformQuickUnlock.TryUnlockAsync));

        Assert.NotNull(unlockMethod);
        Assert.Equal(
            typeof(Task<FluentResults.Result<PlatformQuickUnlockAttempt>>),
            unlockMethod.ReturnType);
    }

    [Fact]
    public void IsSupported_WithReviewedAndroidWrapper_ReturnsTrue()
    {
        var wrapper = CreateAndroidWrapper();

        Assert.True(PlatformQuickUnlockContract.IsSupported(wrapper));
    }

    [Fact]
    public void IsSupported_WithReviewedAndroidDeviceCredentialWrapper_ReturnsTrue()
    {
        var wrapper = CreateAndroidWrapper() with
        {
            Provider = PlatformQuickUnlockContract.AndroidKeystoreDeviceCredentialProvider,
            ProviderVersion =
                PlatformQuickUnlockContract.AndroidKeystoreDeviceCredentialProviderVersion,
            KeyReference = "TOTP_ANDROID_PIN_0123456789abcdef0123456789abcdef"
        };

        Assert.True(PlatformQuickUnlockContract.IsSupported(wrapper));
        Assert.Equal(
            PreferredUnlockMethod.PlatformDeviceCredential,
            PlatformQuickUnlockContract.GetUnlockMethod(wrapper));
    }

    [Fact]
    public void IsSupported_WithReviewedAndroidUnattendedWrapper_ReturnsTrue()
    {
        var wrapper = CreateAndroidUnattendedWrapper();

        Assert.True(PlatformQuickUnlockContract.IsSupported(wrapper));
        Assert.True(
            PlatformQuickUnlockContract.IsSupportedAndroidUnattendedWrapper(wrapper));
        Assert.Null(PlatformQuickUnlockContract.GetUnlockMethod(wrapper));
    }

    [Fact]
    public void IsSupportedAndroidUnattendedWrapper_WithBiometricPolicy_ReturnsFalse()
    {
        var wrapper = CreateAndroidUnattendedWrapper() with
        {
            AuthenticationPolicy = PlatformQuickUnlockContract.UserVerificationRequired
        };

        Assert.False(
            PlatformQuickUnlockContract.IsSupportedAndroidUnattendedWrapper(wrapper));
    }

    [Theory]
    [InlineData(11, 48)]
    [InlineData(12, 47)]
    [InlineData(13, 48)]
    [InlineData(12, 49)]
    public void IsSupported_WithInvalidAndroidCryptographicShape_ReturnsFalse(
        int nonceLength,
        int ciphertextLength)
    {
        var wrapper = CreateAndroidWrapper(nonceLength, ciphertextLength);

        Assert.False(PlatformQuickUnlockContract.IsSupported(wrapper));
    }

    private static PlatformQuickUnlockWrapperV2 CreateAndroidWrapper(
        int nonceLength = 12,
        int ciphertextLength = 48) => new()
    {
        Provider = PlatformQuickUnlockContract.AndroidKeystoreBiometricProvider,
        ProviderVersion = PlatformQuickUnlockContract.AndroidKeystoreBiometricProviderVersion,
        AuthenticationPolicy = PlatformQuickUnlockContract.UserVerificationRequired,
        KeyReference = "TOTP_ANDROID_0123456789abcdef0123456789abcdef",
        WrappedKey = new PlatformWrappedKeyV2
        {
            Algorithm = PlatformQuickUnlockContract.AndroidAes256GcmAlgorithm,
            Nonce = new byte[nonceLength],
            Ciphertext = new byte[ciphertextLength]
        }
    };

    private static PlatformQuickUnlockWrapperV2 CreateAndroidUnattendedWrapper() => new()
    {
        Provider = PlatformQuickUnlockContract.AndroidKeystoreUnattendedProvider,
        ProviderVersion = PlatformQuickUnlockContract.AndroidKeystoreUnattendedProviderVersion,
        AuthenticationPolicy = PlatformQuickUnlockContract.UserVerificationNotRequired,
        KeyReference = "TOTP_ANDROID_UNATTENDED_0123456789abcdef0123456789abcdef",
        WrappedKey = new PlatformWrappedKeyV2
        {
            Algorithm = PlatformQuickUnlockContract.AndroidAes256GcmAlgorithm,
            Nonce = new byte[12],
            Ciphertext = new byte[48]
        }
    };
}
