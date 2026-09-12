using FluentResults;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TOTP.Core.Security.Interfaces;
using TOTP.Core.Security.Models;
using TOTP.Core.Enums;
using TOTP.Infrastructure.Security;

namespace TOTP.Tests.Security;

public sealed class PlatformQuickUnlockEnrollmentTests
{
    [Fact]
    public async Task EnableAsync_WithoutRecoveryPassword_FailsBeforeLoading()
    {
        var dependencies = new Dependencies();
        using var sut = dependencies.CreateSut();

        var result = await sut.EnableAsync(" ", TestContext.Current.CancellationToken);

        AssertEnrollmentError(result, PlatformQuickUnlockEnrollmentErrorCode.RecoveryPasswordRequired);
        dependencies.Store.VerifyNoOtherCalls();
        dependencies.Password.VerifyNoOtherCalls();
        dependencies.Vault.VerifyNoOtherCalls();
        dependencies.Platform.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task EnableAsync_WhenEnvelopeIsMissing_ReportsNotConfigured()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var dependencies = new Dependencies();
        dependencies.Store.Setup(value => value.LoadAsync(cancellationToken))
            .ReturnsAsync(Result.Ok<AuthorizationEnvelopeV2?>(null));
        using var sut = dependencies.CreateSut();

        var result = await sut.EnableAsync("recovery-password", cancellationToken);

        AssertEnrollmentError(result, PlatformQuickUnlockEnrollmentErrorCode.NotConfigured);
        dependencies.Password.VerifyNoOtherCalls();
        dependencies.Platform.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task EnableAsync_WhenMatchingMetadataExists_VerifiesRecoveryAndDoesNotReplaceIt()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var envelope = CreateEnvelope() with { QuickUnlockWrapper = CreateWrapper() };
        var dependencies = ReadyThroughVault(envelope, cancellationToken);
        using var sut = dependencies.CreateSut();

        var result = await sut.EnableAsync("recovery-password", cancellationToken);

        Assert.True(result.IsSuccess);
        dependencies.Platform.VerifyGet(value => value.ProviderId, Times.Once);
        dependencies.Platform.Verify(value => value.RegisterAsync(
            It.IsAny<ReadOnlyMemory<byte>>(),
            It.IsAny<CancellationToken>()), Times.Never);
        AssertEnvelopeCleared(envelope);
    }

    [Fact]
    public async Task EnableAsync_WithWrongRecoveryPassword_DoesNotAccessPlatform()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var envelope = CreateEnvelope();
        var dependencies = Dependencies.Loading(envelope, cancellationToken);
        dependencies.Password.Setup(value => value.UnwrapKeyV2Async(
                envelope.PasswordWrapper,
                "wrong-password",
                cancellationToken))
            .ReturnsAsync((byte[]?)null);
        using var sut = dependencies.CreateSut();

        var result = await sut.EnableAsync("wrong-password", cancellationToken);

        AssertEnrollmentError(result, PlatformQuickUnlockEnrollmentErrorCode.InvalidRecoveryPassword);
        dependencies.Vault.VerifyNoOtherCalls();
        dependencies.Platform.VerifyNoOtherCalls();
        AssertEnvelopeCleared(envelope);
    }

    [Fact]
    public async Task EnableAsync_AfterPasswordAndVaultVerification_PersistsRegistrationAndClearsBuffers()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var envelope = CreateEnvelope();
        var recoveredKey = Enumerable.Range(1, 32).Select(value => (byte)value).ToArray();
        var expectedKey = (byte[])recoveredKey.Clone();
        var wrapper = CreateWrapper();
        var expectedCiphertext = (byte[])wrapper.WrappedKey.Ciphertext.Clone();
        byte[]? registrationInput = null;
        byte[]? savedCiphertext = null;
        var dependencies = Dependencies.Loading(envelope, cancellationToken);
        dependencies.Password.Setup(value => value.UnwrapKeyV2Async(
                envelope.PasswordWrapper,
                "recovery-password",
                cancellationToken))
            .ReturnsAsync(recoveredKey);
        dependencies.Vault.Setup(value => value.VerifyAsync(
                It.IsAny<ReadOnlyMemory<byte>>(),
                cancellationToken))
            .Callback<ReadOnlyMemory<byte>, CancellationToken>((key, _) =>
                Assert.Equal(expectedKey, key.ToArray()))
            .ReturnsAsync(Result.Ok(VaultKeyVerificationStatus.Verified));
        dependencies.Platform.Setup(value => value.GetAvailabilityAsync(cancellationToken))
            .ReturnsAsync(PlatformQuickUnlockAvailability.Available);
        dependencies.Platform.Setup(value => value.RegisterAsync(
                It.IsAny<ReadOnlyMemory<byte>>(),
                cancellationToken))
            .Callback<ReadOnlyMemory<byte>, CancellationToken>((key, _) =>
                registrationInput = key.ToArray())
            .ReturnsAsync(Result.Ok(wrapper));
        dependencies.Store.Setup(value => value.SaveAsync(
                It.IsAny<AuthorizationEnvelopeV2>(),
                cancellationToken))
            .Callback<AuthorizationEnvelopeV2, CancellationToken>((saved, _) =>
                savedCiphertext = (byte[])saved.QuickUnlockWrapper!.WrappedKey.Ciphertext.Clone())
            .ReturnsAsync(Result.Ok());
        using var sut = dependencies.CreateSut();

        var result = await sut.EnableAsync("recovery-password", cancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(expectedKey, registrationInput);
        Assert.Equal(expectedCiphertext, savedCiphertext);
        Assert.All(recoveredKey, value => Assert.Equal(0, value));
        Assert.All(wrapper.WrappedKey.Ciphertext, value => Assert.Equal(0, value));
        AssertEnvelopeCleared(envelope);
        dependencies.Platform.Verify(value => value.RemoveAsync(
            It.IsAny<PlatformQuickUnlockWrapperV2>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task EnableAsync_ForDeviceCredential_UsesMatchingAdapterAndReplacesBiometricWrapper()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var oldWrapper = CreateAndroidBiometricWrapper();
        var envelope = CreateEnvelope() with { QuickUnlockWrapper = oldWrapper };
        var deviceWrapper = CreateAndroidDeviceCredentialWrapper();
        var dependencies = ReadyThroughVault(envelope, cancellationToken);
        dependencies.Platform.SetupGet(value => value.ProviderId)
            .Returns(PlatformQuickUnlockContract.AndroidKeystoreBiometricProvider);
        var device = new Mock<IPlatformQuickUnlock>();
        device.SetupGet(value => value.UnlockMethod)
            .Returns(PreferredUnlockMethod.PlatformDeviceCredential);
        device.SetupGet(value => value.ProviderId)
            .Returns(PlatformQuickUnlockContract.AndroidKeystoreDeviceCredentialProvider);
        device.Setup(value => value.GetAvailabilityAsync(cancellationToken))
            .ReturnsAsync(PlatformQuickUnlockAvailability.Available);
        device.Setup(value => value.RegisterAsync(
                It.IsAny<ReadOnlyMemory<byte>>(),
                cancellationToken))
            .ReturnsAsync(Result.Ok(deviceWrapper));
        dependencies.Platform.Setup(value => value.RemoveAsync(
                oldWrapper,
                CancellationToken.None))
            .ReturnsAsync(Result.Ok());
        dependencies.Store.Setup(value => value.SaveAsync(
                It.IsAny<AuthorizationEnvelopeV2>(),
                cancellationToken))
            .Callback<AuthorizationEnvelopeV2, CancellationToken>((saved, _) =>
                Assert.Equal(
                    PlatformQuickUnlockContract.AndroidKeystoreDeviceCredentialProvider,
                    saved.QuickUnlockWrapper!.Provider))
            .ReturnsAsync(Result.Ok());
        using var sut = new PlatformQuickUnlockEnrollment(
            dependencies.Store.Object,
            dependencies.Password.Object,
            dependencies.Vault.Object,
            [dependencies.Platform.Object, device.Object],
            NullLogger<PlatformQuickUnlockEnrollment>.Instance);

        var result = await sut.EnableAsync(
            "recovery-password",
            PreferredUnlockMethod.PlatformDeviceCredential,
            cancellationToken);

        Assert.True(result.IsSuccess);
        device.Verify(value => value.RegisterAsync(
            It.IsAny<ReadOnlyMemory<byte>>(),
            cancellationToken), Times.Once);
        dependencies.Platform.Verify(value => value.RemoveAsync(
            oldWrapper,
            CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task EnableAsync_WithInvalidRecoveredKey_ClearsItAndStopsBeforeVaultVerification()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var envelope = CreateEnvelope();
        var recoveredKey = Enumerable.Repeat((byte)7, 31).ToArray();
        var dependencies = Dependencies.Loading(envelope, cancellationToken);
        dependencies.Password.Setup(value => value.UnwrapKeyV2Async(
                envelope.PasswordWrapper,
                It.IsAny<string>(),
                cancellationToken))
            .ReturnsAsync(recoveredKey);
        using var sut = dependencies.CreateSut();

        var result = await sut.EnableAsync("recovery-password", cancellationToken);

        AssertEnrollmentError(result, PlatformQuickUnlockEnrollmentErrorCode.InvalidRecoveredKey);
        Assert.All(recoveredKey, value => Assert.Equal(0, value));
        dependencies.Vault.VerifyNoOtherCalls();
        dependencies.Platform.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(VaultKeyVerificationStatus.AuthenticationFailed)]
    [InlineData(VaultKeyVerificationStatus.InvalidVaultFormat)]
    [InlineData(VaultKeyVerificationStatus.InvalidCandidateKey)]
    public async Task EnableAsync_WhenRecoveryKeyDoesNotVerifyVault_DoesNotAccessPlatform(
        VaultKeyVerificationStatus verificationStatus)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var envelope = CreateEnvelope();
        var dependencies = ReadyThroughPassword(envelope, cancellationToken);
        dependencies.Vault.Setup(value => value.VerifyAsync(
                It.IsAny<ReadOnlyMemory<byte>>(),
                cancellationToken))
            .ReturnsAsync(Result.Ok(verificationStatus));
        using var sut = dependencies.CreateSut();

        var result = await sut.EnableAsync("recovery-password", cancellationToken);

        AssertEnrollmentError(result, PlatformQuickUnlockEnrollmentErrorCode.VaultVerificationFailed);
        dependencies.Platform.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task EnableAsync_WhenPlatformIsUnavailable_DoesNotRegister()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var envelope = CreateEnvelope();
        var dependencies = ReadyThroughVault(envelope, cancellationToken);
        dependencies.Platform.Setup(value => value.GetAvailabilityAsync(cancellationToken))
            .ReturnsAsync(PlatformQuickUnlockAvailability.NotConfigured);
        using var sut = dependencies.CreateSut();

        var result = await sut.EnableAsync("recovery-password", cancellationToken);

        AssertEnrollmentError(result, PlatformQuickUnlockEnrollmentErrorCode.PlatformUnavailable);
        dependencies.Platform.Verify(value => value.RegisterAsync(
            It.IsAny<ReadOnlyMemory<byte>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task EnableAsync_WhenRegistrationFails_DoesNotPersistEnvelope()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var envelope = CreateEnvelope();
        var dependencies = ReadyThroughVault(envelope, cancellationToken);
        dependencies.Platform.Setup(value => value.GetAvailabilityAsync(cancellationToken))
            .ReturnsAsync(PlatformQuickUnlockAvailability.Available);
        dependencies.Platform.Setup(value => value.RegisterAsync(
                It.IsAny<ReadOnlyMemory<byte>>(),
                cancellationToken))
            .ReturnsAsync(Result.Fail<PlatformQuickUnlockWrapperV2>(new PlatformQuickUnlockError(
                PlatformQuickUnlockErrorCode.Cancelled,
                "synthetic cancellation")));
        using var sut = dependencies.CreateSut();

        var result = await sut.EnableAsync("recovery-password", cancellationToken);

        AssertEnrollmentError(result, PlatformQuickUnlockEnrollmentErrorCode.RegistrationFailed);
        Assert.Equal(
            PlatformQuickUnlockErrorCode.Cancelled,
            Assert.Single(result.Errors.OfType<PlatformQuickUnlockError>()).Code);
        dependencies.Store.Verify(value => value.SaveAsync(
            It.IsAny<AuthorizationEnvelopeV2>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task EnableAsync_WhenPlatformReturnsInvalidMetadata_RemovesRegistrationAndDoesNotPersist()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var envelope = CreateEnvelope();
        var wrapper = CreateWrapper() with { AuthenticationPolicy = "silent" };
        var dependencies = ReadyThroughVault(envelope, cancellationToken);
        dependencies.Platform.SetupGet(value => value.ProviderId)
            .Returns(PlatformQuickUnlockContract.WindowsHelloTpmProvider);
        dependencies.Platform.Setup(value => value.GetAvailabilityAsync(cancellationToken))
            .ReturnsAsync(PlatformQuickUnlockAvailability.Available);
        dependencies.Platform.Setup(value => value.RegisterAsync(
                It.IsAny<ReadOnlyMemory<byte>>(),
                cancellationToken))
            .ReturnsAsync(Result.Ok(wrapper));
        dependencies.Platform.Setup(value => value.RemoveAsync(wrapper, CancellationToken.None))
            .ReturnsAsync(Result.Ok());
        using var sut = dependencies.CreateSut();

        var result = await sut.EnableAsync("recovery-password", cancellationToken);

        AssertEnrollmentError(result, PlatformQuickUnlockEnrollmentErrorCode.RegistrationFailed);
        dependencies.Platform.Verify(value => value.RemoveAsync(wrapper, CancellationToken.None), Times.Once);
        dependencies.Store.Verify(value => value.SaveAsync(
            It.IsAny<AuthorizationEnvelopeV2>(),
            It.IsAny<CancellationToken>()), Times.Never);
        Assert.All(wrapper.WrappedKey.Ciphertext, value => Assert.Equal(0, value));
    }

    [Fact]
    public async Task EnableAsync_WhenPersistenceFails_RemovesRegistrationAndPreservesFailure()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var envelope = CreateEnvelope();
        var wrapper = CreateWrapper();
        var dependencies = ReadyThroughRegistration(envelope, wrapper, cancellationToken);
        dependencies.Store.Setup(value => value.SaveAsync(
                It.IsAny<AuthorizationEnvelopeV2>(),
                cancellationToken))
            .ReturnsAsync(Result.Fail(new AuthorizationEnvelopeError(
                AuthorizationEnvelopeErrorCode.WriteFailed,
                "synthetic write failure")));
        dependencies.Platform.Setup(value => value.RemoveAsync(wrapper, CancellationToken.None))
            .ReturnsAsync(Result.Ok());
        using var sut = dependencies.CreateSut();

        var result = await sut.EnableAsync("recovery-password", cancellationToken);

        AssertEnrollmentError(result, PlatformQuickUnlockEnrollmentErrorCode.PersistenceFailed);
        Assert.Equal(
            AuthorizationEnvelopeErrorCode.WriteFailed,
            Assert.Single(result.Errors.OfType<AuthorizationEnvelopeError>()).Code);
        dependencies.Platform.Verify(value => value.RemoveAsync(wrapper, CancellationToken.None), Times.Once);
        Assert.All(wrapper.WrappedKey.Ciphertext, value => Assert.Equal(0, value));
    }

    [Fact]
    public async Task EnableAsync_WhenPersistenceIsCancelled_RemovesRegistrationAndPropagatesCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        var envelope = CreateEnvelope();
        var wrapper = CreateWrapper();
        var dependencies = ReadyThroughRegistration(envelope, wrapper, cancellation.Token);
        dependencies.Store.Setup(value => value.SaveAsync(
                It.IsAny<AuthorizationEnvelopeV2>(),
                cancellation.Token))
            .Returns(async () =>
            {
                await cancellation.CancelAsync();
                throw new OperationCanceledException(cancellation.Token);
            });
        dependencies.Platform.Setup(value => value.RemoveAsync(wrapper, CancellationToken.None))
            .ReturnsAsync(Result.Ok());
        using var sut = dependencies.CreateSut();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => sut.EnableAsync("recovery-password", cancellation.Token));

        dependencies.Platform.Verify(value => value.RemoveAsync(wrapper, CancellationToken.None), Times.Once);
        Assert.All(wrapper.WrappedKey.Ciphertext, value => Assert.Equal(0, value));
    }

    private static Dependencies ReadyThroughPassword(
        AuthorizationEnvelopeV2 envelope,
        CancellationToken cancellationToken)
    {
        var dependencies = Dependencies.Loading(envelope, cancellationToken);
        dependencies.Password.Setup(value => value.UnwrapKeyV2Async(
                envelope.PasswordWrapper,
                "recovery-password",
                cancellationToken))
            .ReturnsAsync(Enumerable.Range(1, 32).Select(value => (byte)value).ToArray());
        return dependencies;
    }

    private static Dependencies ReadyThroughVault(
        AuthorizationEnvelopeV2 envelope,
        CancellationToken cancellationToken)
    {
        var dependencies = ReadyThroughPassword(envelope, cancellationToken);
        dependencies.Vault.Setup(value => value.VerifyAsync(
                It.IsAny<ReadOnlyMemory<byte>>(),
                cancellationToken))
            .ReturnsAsync(Result.Ok(VaultKeyVerificationStatus.Verified));
        return dependencies;
    }

    private static Dependencies ReadyThroughRegistration(
        AuthorizationEnvelopeV2 envelope,
        PlatformQuickUnlockWrapperV2 wrapper,
        CancellationToken cancellationToken)
    {
        var dependencies = ReadyThroughVault(envelope, cancellationToken);
        dependencies.Platform.Setup(value => value.GetAvailabilityAsync(cancellationToken))
            .ReturnsAsync(PlatformQuickUnlockAvailability.Available);
        dependencies.Platform.Setup(value => value.RegisterAsync(
                It.IsAny<ReadOnlyMemory<byte>>(),
                cancellationToken))
            .ReturnsAsync(Result.Ok(wrapper));
        return dependencies;
    }

    private static AuthorizationEnvelopeV2 CreateEnvelope() =>
        AuthorizationEnvelopeV2CodecTests.CreateEnvelope();

    private static PlatformQuickUnlockWrapperV2 CreateWrapper() => new()
    {
        Provider = PlatformQuickUnlockContract.WindowsHelloTpmProvider,
        ProviderVersion = PlatformQuickUnlockContract.WindowsHelloTpmProviderVersion,
        AuthenticationPolicy = PlatformQuickUnlockContract.UserVerificationRequired,
        KeyReference = "TOTP_TPM_SYNTHETIC_ENROLLMENT",
        WrappedKey = new PlatformWrappedKeyV2
        {
            Algorithm = PlatformQuickUnlockContract.RsaOaepSha256Algorithm,
            Ciphertext = Enumerable.Repeat((byte)5, 256).ToArray()
        }
    };

    private static PlatformQuickUnlockWrapperV2 CreateAndroidBiometricWrapper() => new()
    {
        Provider = PlatformQuickUnlockContract.AndroidKeystoreBiometricProvider,
        ProviderVersion = PlatformQuickUnlockContract.AndroidKeystoreBiometricProviderVersion,
        AuthenticationPolicy = PlatformQuickUnlockContract.UserVerificationRequired,
        KeyReference = "TOTP_ANDROID_BIO_0123456789abcdef0123456789abcdef",
        WrappedKey = new PlatformWrappedKeyV2
        {
            Algorithm = PlatformQuickUnlockContract.AndroidAes256GcmAlgorithm,
            Nonce = new byte[12],
            Ciphertext = new byte[48]
        }
    };

    private static PlatformQuickUnlockWrapperV2 CreateAndroidDeviceCredentialWrapper() => new()
    {
        Provider = PlatformQuickUnlockContract.AndroidKeystoreDeviceCredentialProvider,
        ProviderVersion =
            PlatformQuickUnlockContract.AndroidKeystoreDeviceCredentialProviderVersion,
        AuthenticationPolicy = PlatformQuickUnlockContract.UserVerificationRequired,
        KeyReference = "TOTP_ANDROID_PIN_0123456789abcdef0123456789abcdef",
        WrappedKey = new PlatformWrappedKeyV2
        {
            Algorithm = PlatformQuickUnlockContract.AndroidAes256GcmAlgorithm,
            Nonce = new byte[12],
            Ciphertext = new byte[48]
        }
    };

    private static void AssertEnvelopeCleared(AuthorizationEnvelopeV2 envelope)
    {
        Assert.All(envelope.PasswordWrapper.Kdf.Salt, value => Assert.Equal(0, value));
        Assert.All(envelope.PasswordWrapper.WrappedKey.Nonce, value => Assert.Equal(0, value));
        Assert.All(envelope.PasswordWrapper.WrappedKey.Ciphertext, value => Assert.Equal(0, value));
        if (envelope.QuickUnlockWrapper is not null)
            Assert.All(envelope.QuickUnlockWrapper.WrappedKey.Ciphertext, value => Assert.Equal(0, value));
    }

    private static void AssertEnrollmentError(
        Result result,
        PlatformQuickUnlockEnrollmentErrorCode expectedCode)
    {
        Assert.False(result.IsSuccess);
        Assert.Equal(
            expectedCode,
            Assert.Single(result.Errors.OfType<PlatformQuickUnlockEnrollmentError>()).Code);
    }

    private sealed class Dependencies
    {
        public Mock<IAuthorizationEnvelopeStore> Store { get; } = new();
        public Mock<IMasterPasswordService> Password { get; } = new();
        public Mock<IStoredVaultKeyVerifier> Vault { get; } = new();
        public Mock<IPlatformQuickUnlock> Platform { get; } = new();

        public Dependencies()
        {
            Platform.SetupGet(value => value.ProviderId)
                .Returns(PlatformQuickUnlockContract.WindowsHelloTpmProvider);
            Platform.SetupGet(value => value.UnlockMethod)
                .Returns(PreferredUnlockMethod.PlatformQuickUnlock);
        }

        public static Dependencies Loading(
            AuthorizationEnvelopeV2 envelope,
            CancellationToken cancellationToken)
        {
            var dependencies = new Dependencies();
            dependencies.Store.Setup(value => value.LoadAsync(cancellationToken))
                .ReturnsAsync(Result.Ok<AuthorizationEnvelopeV2?>(envelope));
            return dependencies;
        }

        public PlatformQuickUnlockEnrollment CreateSut() => new(
            Store.Object,
            Password.Object,
            Vault.Object,
            Platform.Object,
            NullLogger<PlatformQuickUnlockEnrollment>.Instance);
    }
}
