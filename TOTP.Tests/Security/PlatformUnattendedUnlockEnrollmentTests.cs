using FluentResults;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TOTP.Core.Security.Interfaces;
using TOTP.Core.Security.Models;
using TOTP.Infrastructure.Security;

namespace TOTP.Tests.Security;

public sealed class PlatformUnattendedUnlockEnrollmentTests
{
    [Fact]
    public async Task EnableAsync_WithWrongRecoveryPassword_DoesNotAccessPlatform()
    {
        var token = TestContext.Current.CancellationToken;
        var envelope = AuthorizationEnvelopeV2CodecTests.CreateEnvelope();
        var store = Loading(envelope, token);
        var password = new Mock<IMasterPasswordService>();
        password.Setup(value => value.UnwrapKeyV2Async(
                envelope.PasswordWrapper,
                "wrong",
                token))
            .ReturnsAsync((byte[]?)null);
        var platform = CreatePlatform();
        using var sut = CreateSut(store, password, new(), platform);

        var result = await sut.EnableAsync("wrong", token);

        Assert.Equal(
            PlatformQuickUnlockEnrollmentErrorCode.InvalidRecoveryPassword,
            Assert.Single(result.Errors.OfType<PlatformQuickUnlockEnrollmentError>()).Code);
        platform.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task EnableAsync_ReplacesBiometricWrapperOnlyAfterVaultVerificationAndPersistence()
    {
        var token = TestContext.Current.CancellationToken;
        var oldWrapper = CreateBiometricWrapper();
        var envelope = AuthorizationEnvelopeV2CodecTests.CreateEnvelope() with
        {
            QuickUnlockWrapper = oldWrapper
        };
        var store = Loading(envelope, token);
        string? savedProvider = null;
        store.Setup(value => value.SaveAsync(
                It.IsAny<AuthorizationEnvelopeV2>(),
                token))
            .Callback<AuthorizationEnvelopeV2, CancellationToken>((saved, _) =>
                savedProvider = saved.QuickUnlockWrapper?.Provider)
            .ReturnsAsync(Result.Ok());
        var recoveredKey = Enumerable.Range(1, 32).Select(value => (byte)value).ToArray();
        var password = new Mock<IMasterPasswordService>();
        password.Setup(value => value.UnwrapKeyV2Async(
                envelope.PasswordWrapper,
                "recovery-password",
                token))
            .ReturnsAsync(recoveredKey);
        var vault = new Mock<IStoredVaultKeyVerifier>();
        vault.Setup(value => value.VerifyAsync(
                It.IsAny<ReadOnlyMemory<byte>>(),
                token))
            .ReturnsAsync(Result.Ok(VaultKeyVerificationStatus.Verified));
        var platform = CreatePlatform();
        var newWrapper = CreateUnattendedWrapper();
        platform.Setup(value => value.GetAvailabilityAsync(token))
            .ReturnsAsync(PlatformQuickUnlockAvailability.Available);
        platform.Setup(value => value.RegisterAsync(
                It.IsAny<ReadOnlyMemory<byte>>(),
                token))
            .ReturnsAsync(Result.Ok(newWrapper));
        var oldPlatform = new Mock<IPlatformQuickUnlock>();
        oldPlatform.SetupGet(value => value.ProviderId)
            .Returns(PlatformQuickUnlockContract.AndroidKeystoreBiometricProvider);
        oldPlatform.Setup(value => value.RemoveAsync(oldWrapper, CancellationToken.None))
            .ReturnsAsync(Result.Ok());
        using var sut = CreateSut(store, password, vault, platform, oldPlatform.Object);

        var result = await sut.EnableAsync("recovery-password", token);

        Assert.True(result.IsSuccess);
        Assert.Equal(PlatformQuickUnlockContract.AndroidKeystoreUnattendedProvider, savedProvider);
        oldPlatform.Verify(value => value.RemoveAsync(oldWrapper, CancellationToken.None), Times.Once);
        Assert.All(recoveredKey, value => Assert.Equal(0, value));
        Assert.All(newWrapper.WrappedKey.Ciphertext, value => Assert.Equal(0, value));
        Assert.All(oldWrapper.WrappedKey.Ciphertext, value => Assert.Equal(0, value));
    }

    [Fact]
    public async Task DisableAsync_PersistsRemovalBeforeDeletingPlatformKey()
    {
        var token = TestContext.Current.CancellationToken;
        var wrapper = CreateUnattendedWrapper();
        var envelope = AuthorizationEnvelopeV2CodecTests.CreateEnvelope() with
        {
            QuickUnlockWrapper = wrapper
        };
        var store = Loading(envelope, token);
        var persistedWithoutWrapper = false;
        store.Setup(value => value.SaveAsync(
                It.IsAny<AuthorizationEnvelopeV2>(),
                token))
            .Callback<AuthorizationEnvelopeV2, CancellationToken>((saved, _) =>
                persistedWithoutWrapper = saved.QuickUnlockWrapper is null)
            .ReturnsAsync(Result.Ok());
        var platform = CreatePlatform();
        platform.Setup(value => value.RemoveAsync(wrapper, token))
            .Callback(() => Assert.True(persistedWithoutWrapper))
            .ReturnsAsync(Result.Ok());
        using var sut = CreateSut(store, new(), new(), platform);

        var result = await sut.DisableAsync(token);

        Assert.True(result.IsSuccess);
        platform.Verify(value => value.RemoveAsync(wrapper, token), Times.Once);
        Assert.All(wrapper.WrappedKey.Ciphertext, value => Assert.Equal(0, value));
    }

    private static Mock<IAuthorizationEnvelopeStore> Loading(
        AuthorizationEnvelopeV2 envelope,
        CancellationToken token)
    {
        var store = new Mock<IAuthorizationEnvelopeStore>();
        store.Setup(value => value.LoadAsync(token))
            .ReturnsAsync(Result.Ok<AuthorizationEnvelopeV2?>(envelope));
        return store;
    }

    private static Mock<IPlatformUnattendedUnlock> CreatePlatform()
    {
        var platform = new Mock<IPlatformUnattendedUnlock>();
        platform.SetupGet(value => value.ProviderId)
            .Returns(PlatformQuickUnlockContract.AndroidKeystoreUnattendedProvider);
        return platform;
    }

    private static PlatformUnattendedUnlockEnrollment CreateSut(
        Mock<IAuthorizationEnvelopeStore> store,
        Mock<IMasterPasswordService> password,
        Mock<IStoredVaultKeyVerifier> vault,
        Mock<IPlatformUnattendedUnlock> platform,
        params IPlatformQuickUnlock[] additionalAdapters) =>
        new(
            store.Object,
            password.Object,
            vault.Object,
            platform.Object,
            [platform.Object, .. additionalAdapters],
            NullLogger<PlatformUnattendedUnlockEnrollment>.Instance);

    private static PlatformQuickUnlockWrapperV2 CreateBiometricWrapper() => new()
    {
        Provider = PlatformQuickUnlockContract.AndroidKeystoreBiometricProvider,
        ProviderVersion = PlatformQuickUnlockContract.AndroidKeystoreBiometricProviderVersion,
        AuthenticationPolicy = PlatformQuickUnlockContract.UserVerificationRequired,
        KeyReference = "TOTP_ANDROID_SYNTHETIC",
        WrappedKey = new PlatformWrappedKeyV2
        {
            Algorithm = PlatformQuickUnlockContract.AndroidAes256GcmAlgorithm,
            Nonce = new byte[12],
            Ciphertext = Enumerable.Repeat((byte)3, 48).ToArray()
        }
    };

    private static PlatformQuickUnlockWrapperV2 CreateUnattendedWrapper() => new()
    {
        Provider = PlatformQuickUnlockContract.AndroidKeystoreUnattendedProvider,
        ProviderVersion = PlatformQuickUnlockContract.AndroidKeystoreUnattendedProviderVersion,
        AuthenticationPolicy = PlatformQuickUnlockContract.UserVerificationNotRequired,
        KeyReference = "TOTP_ANDROID_UNATTENDED_SYNTHETIC",
        WrappedKey = new PlatformWrappedKeyV2
        {
            Algorithm = PlatformQuickUnlockContract.AndroidAes256GcmAlgorithm,
            Nonce = new byte[12],
            Ciphertext = Enumerable.Repeat((byte)7, 48).ToArray()
        }
    };
}
