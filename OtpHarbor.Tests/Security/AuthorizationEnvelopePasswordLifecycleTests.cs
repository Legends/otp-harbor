using FluentResults;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TOTP.Core.Security.Interfaces;
using TOTP.Core.Security.Models;
using TOTP.Infrastructure.Security;

namespace TOTP.Tests.Security;

public sealed class AuthorizationEnvelopePasswordLifecycleTests
{
    [Fact]
    public async Task ConfigureAsync_WithInvalidNewPassword_FailsBeforeStorageAccess()
    {
        var dependencies = new Dependencies(validNewPassword: false);
        using var sut = dependencies.CreateSut();

        var result = await sut.ConfigureAsync("weak", TestContext.Current.CancellationToken);

        AssertLifecycleError(result, AuthorizationEnvelopePasswordLifecycleErrorCode.InvalidNewPassword);
        dependencies.Store.VerifyNoOtherCalls();
        dependencies.Password.VerifyNoOtherCalls();
        dependencies.Activator.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ConfigureAsync_WhenAlreadyConfigured_FailsWithoutReplacingAndClearsLoadedEnvelope()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var envelope = CreateEnvelope();
        var dependencies = Dependencies.Loading(envelope, cancellationToken);
        using var sut = dependencies.CreateSut();

        var result = await sut.ConfigureAsync("recovery-password", cancellationToken);

        AssertLifecycleError(result, AuthorizationEnvelopePasswordLifecycleErrorCode.AlreadyConfigured);
        dependencies.Password.VerifyNoOtherCalls();
        dependencies.Activator.VerifyNoOtherCalls();
        AssertEnvelopeCleared(envelope);
    }

    [Fact]
    public async Task ConfigureAsync_WhenLoadFails_PreservesTypedFailure()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var dependencies = new Dependencies();
        dependencies.Store.Setup(value => value.LoadAsync(cancellationToken))
            .ReturnsAsync(Result.Fail<AuthorizationEnvelopeV2?>(new AuthorizationEnvelopeError(
                AuthorizationEnvelopeErrorCode.ReadFailed,
                "synthetic read failure")));
        using var sut = dependencies.CreateSut();

        var result = await sut.ConfigureAsync("recovery-password", cancellationToken);

        AssertLifecycleError(result, AuthorizationEnvelopePasswordLifecycleErrorCode.EnvelopeLoadFailed);
        Assert.Equal(
            AuthorizationEnvelopeErrorCode.ReadFailed,
            Assert.Single(result.Errors.OfType<AuthorizationEnvelopeError>()).Code);
        dependencies.Password.VerifyNoOtherCalls();
        dependencies.Activator.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ConfigureAsync_AfterVerifiedActivation_ReturnsOwnedKeyAndClearsWorkingBuffers()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var wrapper = CreateEnvelope().PasswordWrapper;
        byte[]? candidateReference = null;
        byte[]? expectedKey = null;
        var dependencies = Dependencies.Loading(null, cancellationToken);
        dependencies.Password.Setup(value => value.WrapKeyV2Async(
                It.IsAny<byte[]>(),
                "recovery-password",
                cancellationToken))
            .Callback<byte[], string, CancellationToken>((key, _, _) =>
            {
                candidateReference = key;
                expectedKey = (byte[])key.Clone();
            })
            .ReturnsAsync(wrapper);
        dependencies.Activator.Setup(value => value.ActivateAsync(
                It.IsAny<AuthorizationEnvelopeV2>(),
                It.IsAny<ReadOnlyMemory<byte>>(),
                "recovery-password",
                cancellationToken))
            .Callback<AuthorizationEnvelopeV2, ReadOnlyMemory<byte>, string, CancellationToken>(
                (envelope, key, _, _) =>
                {
                    Assert.Same(wrapper, envelope.PasswordWrapper);
                    Assert.Equal(expectedKey, key.ToArray());
                    Assert.Null(envelope.QuickUnlockWrapper);
                })
            .ReturnsAsync(Result.Ok());
        using var sut = dependencies.CreateSut();

        var result = await sut.ConfigureAsync("recovery-password", cancellationToken);

        Assert.True(result.IsSuccess);
        using var returnedKey = result.Value;
        Assert.Equal(expectedKey, returnedKey.Memory.ToArray());
        Assert.NotNull(candidateReference);
        Assert.All(candidateReference, value => Assert.Equal(0, value));
        AssertPasswordWrapperCleared(wrapper);
    }

    [Fact]
    public async Task ConfigureAsync_WhenActivationFails_PreservesFailureAndClearsWorkingBuffers()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var wrapper = CreateEnvelope().PasswordWrapper;
        byte[]? candidateReference = null;
        var dependencies = Dependencies.Loading(null, cancellationToken);
        dependencies.Password.Setup(value => value.WrapKeyV2Async(
                It.IsAny<byte[]>(),
                It.IsAny<string>(),
                cancellationToken))
            .Callback<byte[], string, CancellationToken>((key, _, _) => candidateReference = key)
            .ReturnsAsync(wrapper);
        dependencies.Activator.Setup(value => value.ActivateAsync(
                It.IsAny<AuthorizationEnvelopeV2>(),
                It.IsAny<ReadOnlyMemory<byte>>(),
                It.IsAny<string>(),
                cancellationToken))
            .ReturnsAsync(Result.Fail(new AuthorizationEnvelopeActivationError(
                AuthorizationEnvelopeActivationErrorCode.PersistenceFailed,
                "synthetic activation failure")));
        using var sut = dependencies.CreateSut();

        var result = await sut.ConfigureAsync("recovery-password", cancellationToken);

        AssertLifecycleError(result, AuthorizationEnvelopePasswordLifecycleErrorCode.ActivationFailed);
        Assert.Equal(
            AuthorizationEnvelopeActivationErrorCode.PersistenceFailed,
            Assert.Single(result.Errors.OfType<AuthorizationEnvelopeActivationError>()).Code);
        Assert.NotNull(candidateReference);
        Assert.All(candidateReference, value => Assert.Equal(0, value));
        AssertPasswordWrapperCleared(wrapper);
    }

    [Fact]
    public async Task ConfigureAsync_WhenWrappingIsCancelled_PropagatesAndClearsGeneratedKey()
    {
        using var cancellation = new CancellationTokenSource();
        byte[]? candidateReference = null;
        var dependencies = Dependencies.Loading(null, cancellation.Token);
        dependencies.Password.Setup(value => value.WrapKeyV2Async(
                It.IsAny<byte[]>(),
                It.IsAny<string>(),
                cancellation.Token))
            .Returns<byte[], string, CancellationToken>(async (key, _, _) =>
            {
                candidateReference = key;
                await cancellation.CancelAsync();
                throw new OperationCanceledException(cancellation.Token);
            });
        using var sut = dependencies.CreateSut();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => sut.ConfigureAsync("recovery-password", cancellation.Token));

        Assert.NotNull(candidateReference);
        Assert.All(candidateReference, value => Assert.Equal(0, value));
        dependencies.Activator.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ChangePasswordAsync_WithoutCurrentPassword_FailsBeforeStorageAccess()
    {
        var dependencies = new Dependencies();
        using var sut = dependencies.CreateSut();

        var result = await sut.ChangePasswordAsync(
            " ",
            "new-recovery-password",
            TestContext.Current.CancellationToken);

        AssertLifecycleError(result, AuthorizationEnvelopePasswordLifecycleErrorCode.CurrentPasswordRequired);
        dependencies.Store.VerifyNoOtherCalls();
        dependencies.Password.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ChangePasswordAsync_WhenNotConfigured_DoesNotCreateNewEnvelope()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var dependencies = Dependencies.Loading(null, cancellationToken);
        using var sut = dependencies.CreateSut();

        var result = await sut.ChangePasswordAsync(
            "current-password",
            "new-recovery-password",
            cancellationToken);

        AssertLifecycleError(result, AuthorizationEnvelopePasswordLifecycleErrorCode.NotConfigured);
        dependencies.Password.VerifyNoOtherCalls();
        dependencies.Activator.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ChangePasswordAsync_WithWrongCurrentPassword_DoesNotCreateReplacementAndClearsEnvelope()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var envelope = CreateEnvelopeWithQuickUnlock();
        var dependencies = Dependencies.Loading(envelope, cancellationToken);
        dependencies.Password.Setup(value => value.UnwrapKeyV2Async(
                envelope.PasswordWrapper,
                "wrong-password",
                cancellationToken))
            .ReturnsAsync((byte[]?)null);
        using var sut = dependencies.CreateSut();

        var result = await sut.ChangePasswordAsync(
            "wrong-password",
            "new-recovery-password",
            cancellationToken);

        AssertLifecycleError(result, AuthorizationEnvelopePasswordLifecycleErrorCode.InvalidCurrentPassword);
        dependencies.Password.Verify(value => value.WrapKeyV2Async(
            It.IsAny<byte[]>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
        dependencies.Activator.VerifyNoOtherCalls();
        AssertEnvelopeCleared(envelope);
    }

    [Fact]
    public async Task ChangePasswordAsync_WithInvalidRecoveredKey_ClearsItAndStopsBeforeReplacement()
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

        var result = await sut.ChangePasswordAsync(
            "current-password",
            "new-recovery-password",
            cancellationToken);

        AssertLifecycleError(result, AuthorizationEnvelopePasswordLifecycleErrorCode.InvalidRecoveredKey);
        Assert.All(recoveredKey, value => Assert.Equal(0, value));
        dependencies.Activator.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ChangePasswordAsync_AfterVerification_PreservesQuickUnlockAndReturnsOwnedKey()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var envelope = CreateEnvelopeWithQuickUnlock();
        var originalQuickCiphertext = (byte[])envelope.QuickUnlockWrapper!.WrappedKey.Ciphertext.Clone();
        var recoveredKey = Enumerable.Range(1, 32).Select(value => (byte)value).ToArray();
        var expectedKey = (byte[])recoveredKey.Clone();
        var replacementWrapper = CreateEnvelope().PasswordWrapper;
        var dependencies = Dependencies.Loading(envelope, cancellationToken);
        dependencies.Password.Setup(value => value.UnwrapKeyV2Async(
                envelope.PasswordWrapper,
                "current-password",
                cancellationToken))
            .ReturnsAsync(recoveredKey);
        dependencies.Password.Setup(value => value.WrapKeyV2Async(
                recoveredKey,
                "new-recovery-password",
                cancellationToken))
            .ReturnsAsync(replacementWrapper);
        dependencies.Activator.Setup(value => value.ActivateAsync(
                It.IsAny<AuthorizationEnvelopeV2>(),
                It.IsAny<ReadOnlyMemory<byte>>(),
                "new-recovery-password",
                cancellationToken))
            .Callback<AuthorizationEnvelopeV2, ReadOnlyMemory<byte>, string, CancellationToken>(
                (updated, key, _, _) =>
                {
                    Assert.Same(replacementWrapper, updated.PasswordWrapper);
                    Assert.Same(envelope.QuickUnlockWrapper, updated.QuickUnlockWrapper);
                    Assert.Equal(originalQuickCiphertext, updated.QuickUnlockWrapper!.WrappedKey.Ciphertext);
                    Assert.Equal(expectedKey, key.ToArray());
                })
            .ReturnsAsync(Result.Ok());
        using var sut = dependencies.CreateSut();

        var result = await sut.ChangePasswordAsync(
            "current-password",
            "new-recovery-password",
            cancellationToken);

        Assert.True(result.IsSuccess);
        using var returnedKey = result.Value;
        Assert.Equal(expectedKey, returnedKey.Memory.ToArray());
        Assert.All(recoveredKey, value => Assert.Equal(0, value));
        AssertEnvelopeCleared(envelope);
        AssertPasswordWrapperCleared(replacementWrapper);
    }

    [Fact]
    public async Task ChangePasswordAsync_WhenActivationFails_DoesNotReturnKeyAndPreservesFailure()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var envelope = CreateEnvelope();
        var recoveredKey = new byte[32];
        var replacementWrapper = CreateEnvelope().PasswordWrapper;
        var dependencies = Dependencies.Loading(envelope, cancellationToken);
        dependencies.Password.Setup(value => value.UnwrapKeyV2Async(
                envelope.PasswordWrapper,
                It.IsAny<string>(),
                cancellationToken))
            .ReturnsAsync(recoveredKey);
        dependencies.Password.Setup(value => value.WrapKeyV2Async(
                recoveredKey,
                It.IsAny<string>(),
                cancellationToken))
            .ReturnsAsync(replacementWrapper);
        dependencies.Activator.Setup(value => value.ActivateAsync(
                It.IsAny<AuthorizationEnvelopeV2>(),
                It.IsAny<ReadOnlyMemory<byte>>(),
                It.IsAny<string>(),
                cancellationToken))
            .ReturnsAsync(Result.Fail(new AuthorizationEnvelopeActivationError(
                AuthorizationEnvelopeActivationErrorCode.VaultVerificationFailed,
                "synthetic verification failure")));
        using var sut = dependencies.CreateSut();

        var result = await sut.ChangePasswordAsync(
            "current-password",
            "new-recovery-password",
            cancellationToken);

        AssertLifecycleError(result, AuthorizationEnvelopePasswordLifecycleErrorCode.ActivationFailed);
        Assert.Equal(
            AuthorizationEnvelopeActivationErrorCode.VaultVerificationFailed,
            Assert.Single(result.Errors.OfType<AuthorizationEnvelopeActivationError>()).Code);
        Assert.All(recoveredKey, value => Assert.Equal(0, value));
        AssertEnvelopeCleared(envelope);
        AssertPasswordWrapperCleared(replacementWrapper);
    }

    private static AuthorizationEnvelopeV2 CreateEnvelope() =>
        AuthorizationEnvelopeV2CodecTests.CreateEnvelope();

    private static AuthorizationEnvelopeV2 CreateEnvelopeWithQuickUnlock() =>
        CreateEnvelope() with
        {
            QuickUnlockWrapper = new PlatformQuickUnlockWrapperV2
            {
                Provider = PlatformQuickUnlockContract.WindowsHelloTpmProvider,
                ProviderVersion = PlatformQuickUnlockContract.WindowsHelloTpmProviderVersion,
                AuthenticationPolicy = PlatformQuickUnlockContract.UserVerificationRequired,
                KeyReference = "TOTP_TPM_SYNTHETIC_PASSWORD_LIFECYCLE",
                WrappedKey = new PlatformWrappedKeyV2
                {
                    Algorithm = PlatformQuickUnlockContract.RsaOaepSha256Algorithm,
                    Ciphertext = Enumerable.Repeat((byte)9, 256).ToArray()
                }
            }
        };

    private static void AssertEnvelopeCleared(AuthorizationEnvelopeV2 envelope)
    {
        AssertPasswordWrapperCleared(envelope.PasswordWrapper);
        if (envelope.QuickUnlockWrapper is not null)
            Assert.All(envelope.QuickUnlockWrapper.WrappedKey.Ciphertext, value => Assert.Equal(0, value));
    }

    private static void AssertPasswordWrapperCleared(PasswordKeyWrapperV2 wrapper)
    {
        Assert.All(wrapper.Kdf.Salt, value => Assert.Equal(0, value));
        Assert.All(wrapper.WrappedKey.Nonce, value => Assert.Equal(0, value));
        Assert.All(wrapper.WrappedKey.Ciphertext, value => Assert.Equal(0, value));
    }

    private static void AssertLifecycleError(
        Result<SensitiveBuffer> result,
        AuthorizationEnvelopePasswordLifecycleErrorCode expectedCode)
    {
        Assert.False(result.IsSuccess);
        Assert.Equal(
            expectedCode,
            Assert.Single(result.Errors.OfType<AuthorizationEnvelopePasswordLifecycleError>()).Code);
    }

    private sealed class Dependencies
    {
        public Mock<IAuthorizationEnvelopeStore> Store { get; } = new();
        public Mock<IMasterPasswordService> Password { get; } = new();
        public Mock<IPasswordValidationService> Validation { get; } = new();
        public Mock<IAuthorizationEnvelopeActivator> Activator { get; } = new();

        public Dependencies(bool validNewPassword = true)
        {
            Validation.Setup(value => value.IsValidNew(It.IsAny<string>())).Returns(validNewPassword);
        }

        public static Dependencies Loading(
            AuthorizationEnvelopeV2? envelope,
            CancellationToken cancellationToken)
        {
            var dependencies = new Dependencies();
            dependencies.Store.Setup(value => value.LoadAsync(cancellationToken))
                .ReturnsAsync(Result.Ok<AuthorizationEnvelopeV2?>(envelope));
            return dependencies;
        }

        public AuthorizationEnvelopePasswordLifecycle CreateSut() => new(
            Store.Object,
            Password.Object,
            Validation.Object,
            Activator.Object,
            NullLogger<AuthorizationEnvelopePasswordLifecycle>.Instance);
    }
}
