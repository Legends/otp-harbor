using FluentResults;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TOTP.Core.Security.Interfaces;
using TOTP.Core.Security.Models;
using TOTP.Infrastructure.Security;

namespace TOTP.Tests.Security;

public sealed class AuthorizationEnvelopeActivatorTests
{
    [Theory]
    [InlineData(VaultKeyVerificationStatus.Verified)]
    [InlineData(VaultKeyVerificationStatus.VaultNotFound)]
    public async Task ActivateAsync_AfterWrapperAndVaultVerification_PersistsEnvelope(
        VaultKeyVerificationStatus vaultStatus)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var candidateKey = Enumerable.Range(1, 32).Select(value => (byte)value).ToArray();
        var candidateSnapshot = (byte[])candidateKey.Clone();
        var recoveredKey = (byte[])candidateKey.Clone();
        var proposedEnvelope = AuthorizationEnvelopeV2CodecTests.CreateEnvelope();
        var proposedCiphertext = (byte[])proposedEnvelope.PasswordWrapper.WrappedKey.Ciphertext.Clone();
        AuthorizationEnvelopeV2? persistedEnvelope = null;
        var events = new List<string>();
        var password = new Mock<IMasterPasswordService>();
        password.Setup(service => service.UnwrapKeyV2Async(
                It.IsAny<PasswordKeyWrapperV2>(),
                "recovery-password",
                cancellationToken))
            .Callback(() => events.Add("unwrap"))
            .ReturnsAsync(recoveredKey);
        var vault = new Mock<IStoredVaultKeyVerifier>();
        vault.Setup(service => service.VerifyAsync(
                It.IsAny<ReadOnlyMemory<byte>>(),
                cancellationToken))
            .Callback(() => events.Add("verify-vault"))
            .ReturnsAsync(Result.Ok(vaultStatus));
        var store = new Mock<IAuthorizationEnvelopeStore>();
        store.Setup(service => service.SaveAsync(
                It.IsAny<AuthorizationEnvelopeV2>(),
                cancellationToken))
            .Callback<AuthorizationEnvelopeV2, CancellationToken>((value, _) =>
            {
                events.Add("save");
                persistedEnvelope = value;
                Assert.Equal(proposedCiphertext, value.PasswordWrapper.WrappedKey.Ciphertext);
            })
            .ReturnsAsync(Result.Ok());
        using var sut = CreateActivator(password, vault, store);

        var result = await sut.ActivateAsync(
            proposedEnvelope,
            candidateKey,
            "recovery-password",
            cancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(["unwrap", "verify-vault", "save"], events);
        Assert.All(recoveredKey, value => Assert.Equal(0, value));
        Assert.Equal(candidateSnapshot, candidateKey);
        Assert.Equal(proposedCiphertext, proposedEnvelope.PasswordWrapper.WrappedKey.Ciphertext);
        Assert.NotNull(persistedEnvelope);
        Assert.All(persistedEnvelope.PasswordWrapper.Kdf.Salt, value => Assert.Equal(0, value));
        Assert.All(persistedEnvelope.PasswordWrapper.WrappedKey.Nonce, value => Assert.Equal(0, value));
        Assert.All(persistedEnvelope.PasswordWrapper.WrappedKey.Ciphertext, value => Assert.Equal(0, value));
        store.Verify(service => service.SaveAsync(
            It.Is<AuthorizationEnvelopeV2>(value => !ReferenceEquals(value, proposedEnvelope)),
            cancellationToken), Times.Once);
    }

    [Fact]
    public async Task ActivateAsync_WhenPasswordWrapperCannotOpen_DoesNotVerifyOrPersist()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var password = new Mock<IMasterPasswordService>();
        password.Setup(service => service.UnwrapKeyV2Async(
                It.IsAny<PasswordKeyWrapperV2>(),
                "wrong-password",
                cancellationToken))
            .ReturnsAsync((byte[]?)null);
        var vault = new Mock<IStoredVaultKeyVerifier>();
        var store = new Mock<IAuthorizationEnvelopeStore>();
        using var sut = CreateActivator(password, vault, store);

        var result = await sut.ActivateAsync(
            AuthorizationEnvelopeV2CodecTests.CreateEnvelope(),
            new byte[32],
            "wrong-password",
            cancellationToken);

        AssertActivationError(result, AuthorizationEnvelopeActivationErrorCode.PasswordWrapperRejected);
        vault.VerifyNoOtherCalls();
        store.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ActivateAsync_WhenRecoveredKeyDiffers_ClearsItAndDoesNotPersist()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var recoveredKey = Enumerable.Repeat((byte)7, 32).ToArray();
        var password = new Mock<IMasterPasswordService>();
        password.Setup(service => service.UnwrapKeyV2Async(
                It.IsAny<PasswordKeyWrapperV2>(),
                "recovery-password",
                cancellationToken))
            .ReturnsAsync(recoveredKey);
        var vault = new Mock<IStoredVaultKeyVerifier>();
        var store = new Mock<IAuthorizationEnvelopeStore>();
        using var sut = CreateActivator(password, vault, store);

        var result = await sut.ActivateAsync(
            AuthorizationEnvelopeV2CodecTests.CreateEnvelope(),
            new byte[32],
            "recovery-password",
            cancellationToken);

        AssertActivationError(result, AuthorizationEnvelopeActivationErrorCode.CandidateKeyMismatch);
        Assert.All(recoveredKey, value => Assert.Equal(0, value));
        vault.VerifyNoOtherCalls();
        store.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ActivateAsync_WhenEnvelopeIsInvalid_RejectsBeforeKdfWork()
    {
        var password = new Mock<IMasterPasswordService>();
        var vault = new Mock<IStoredVaultKeyVerifier>();
        var store = new Mock<IAuthorizationEnvelopeStore>();
        using var sut = CreateActivator(password, vault, store);
        var invalid = AuthorizationEnvelopeV2CodecTests.CreateEnvelope() with { Version = 99 };

        var result = await sut.ActivateAsync(
            invalid,
            new byte[32],
            "recovery-password",
            TestContext.Current.CancellationToken);

        AssertActivationError(result, AuthorizationEnvelopeActivationErrorCode.InvalidEnvelope);
        password.VerifyNoOtherCalls();
        vault.VerifyNoOtherCalls();
        store.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(31)]
    [InlineData(33)]
    public async Task ActivateAsync_WhenCandidateKeyLengthIsInvalid_RejectsBeforeKdfWork(int keyLength)
    {
        var password = new Mock<IMasterPasswordService>();
        var vault = new Mock<IStoredVaultKeyVerifier>();
        var store = new Mock<IAuthorizationEnvelopeStore>();
        using var sut = CreateActivator(password, vault, store);

        var result = await sut.ActivateAsync(
            AuthorizationEnvelopeV2CodecTests.CreateEnvelope(),
            new byte[keyLength],
            "recovery-password",
            TestContext.Current.CancellationToken);

        AssertActivationError(result, AuthorizationEnvelopeActivationErrorCode.InvalidCandidateKey);
        password.VerifyNoOtherCalls();
        vault.VerifyNoOtherCalls();
        store.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ActivateAsync_WhenRecoveryPasswordIsBlank_RejectsBeforeKdfWork()
    {
        var password = new Mock<IMasterPasswordService>();
        var vault = new Mock<IStoredVaultKeyVerifier>();
        var store = new Mock<IAuthorizationEnvelopeStore>();
        using var sut = CreateActivator(password, vault, store);

        var result = await sut.ActivateAsync(
            AuthorizationEnvelopeV2CodecTests.CreateEnvelope(),
            new byte[32],
            " ",
            TestContext.Current.CancellationToken);

        AssertActivationError(result, AuthorizationEnvelopeActivationErrorCode.PasswordWrapperRejected);
        password.VerifyNoOtherCalls();
        vault.VerifyNoOtherCalls();
        store.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(VaultKeyVerificationStatus.AuthenticationFailed)]
    [InlineData(VaultKeyVerificationStatus.InvalidVaultFormat)]
    [InlineData(VaultKeyVerificationStatus.InvalidCandidateKey)]
    public async Task ActivateAsync_WhenExistingVaultDoesNotVerify_DoesNotPersist(
        VaultKeyVerificationStatus status)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var candidateKey = new byte[32];
        var password = PasswordReturning(candidateKey, cancellationToken);
        var vault = new Mock<IStoredVaultKeyVerifier>();
        vault.Setup(service => service.VerifyAsync(
                It.IsAny<ReadOnlyMemory<byte>>(),
                cancellationToken))
            .ReturnsAsync(Result.Ok(status));
        var store = new Mock<IAuthorizationEnvelopeStore>();
        using var sut = CreateActivator(password, vault, store);

        var result = await sut.ActivateAsync(
            AuthorizationEnvelopeV2CodecTests.CreateEnvelope(),
            candidateKey,
            "recovery-password",
            cancellationToken);

        AssertActivationError(result, AuthorizationEnvelopeActivationErrorCode.VaultVerificationFailed);
        store.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ActivateAsync_WhenVaultReadFails_PreservesUnderlyingTypedFailure()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var candidateKey = new byte[32];
        var password = PasswordReturning(candidateKey, cancellationToken);
        var vault = new Mock<IStoredVaultKeyVerifier>();
        vault.Setup(service => service.VerifyAsync(
                It.IsAny<ReadOnlyMemory<byte>>(),
                cancellationToken))
            .ReturnsAsync(Result.Fail<VaultKeyVerificationStatus>(new StoredVaultVerificationError(
                StoredVaultVerificationErrorCode.ReadAccessDenied,
                "synthetic failure")));
        var store = new Mock<IAuthorizationEnvelopeStore>();
        using var sut = CreateActivator(password, vault, store);

        var result = await sut.ActivateAsync(
            AuthorizationEnvelopeV2CodecTests.CreateEnvelope(),
            candidateKey,
            "recovery-password",
            cancellationToken);

        AssertActivationError(result, AuthorizationEnvelopeActivationErrorCode.VaultVerificationFailed);
        Assert.Equal(
            StoredVaultVerificationErrorCode.ReadAccessDenied,
            Assert.Single(result.Errors.OfType<StoredVaultVerificationError>()).Code);
        store.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ActivateAsync_WhenAtomicStoreFails_ReturnsPersistenceFailure()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var candidateKey = new byte[32];
        var password = PasswordReturning(candidateKey, cancellationToken);
        var vault = new Mock<IStoredVaultKeyVerifier>();
        vault.Setup(service => service.VerifyAsync(
                It.IsAny<ReadOnlyMemory<byte>>(),
                cancellationToken))
            .ReturnsAsync(Result.Ok(VaultKeyVerificationStatus.Verified));
        var store = new Mock<IAuthorizationEnvelopeStore>();
        store.Setup(service => service.SaveAsync(
                It.IsAny<AuthorizationEnvelopeV2>(),
                cancellationToken))
            .ReturnsAsync(Result.Fail(new AuthorizationEnvelopeError(
                AuthorizationEnvelopeErrorCode.WriteFailed,
                "synthetic failure")));
        using var sut = CreateActivator(password, vault, store);

        var result = await sut.ActivateAsync(
            AuthorizationEnvelopeV2CodecTests.CreateEnvelope(),
            candidateKey,
            "recovery-password",
            cancellationToken);

        AssertActivationError(result, AuthorizationEnvelopeActivationErrorCode.PersistenceFailed);
        Assert.Equal(
            AuthorizationEnvelopeErrorCode.WriteFailed,
            Assert.Single(result.Errors.OfType<AuthorizationEnvelopeError>()).Code);
    }

    [Fact]
    public async Task ActivateAsync_WhenAlreadyCancelled_PropagatesWithoutKdfOrPersistence()
    {
        var password = new Mock<IMasterPasswordService>();
        var vault = new Mock<IStoredVaultKeyVerifier>();
        var store = new Mock<IAuthorizationEnvelopeStore>();
        using var sut = CreateActivator(password, vault, store);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sut.ActivateAsync(
            AuthorizationEnvelopeV2CodecTests.CreateEnvelope(),
            new byte[32],
            "recovery-password",
            cancellation.Token));

        password.VerifyNoOtherCalls();
        vault.VerifyNoOtherCalls();
        store.VerifyNoOtherCalls();
    }

    private static Mock<IMasterPasswordService> PasswordReturning(
        byte[] candidateKey,
        CancellationToken cancellationToken)
    {
        var password = new Mock<IMasterPasswordService>();
        password.Setup(service => service.UnwrapKeyV2Async(
                It.IsAny<PasswordKeyWrapperV2>(),
                "recovery-password",
                cancellationToken))
            .ReturnsAsync(() => (byte[])candidateKey.Clone());
        return password;
    }

    private static AuthorizationEnvelopeActivator CreateActivator(
        Mock<IMasterPasswordService> password,
        Mock<IStoredVaultKeyVerifier> vault,
        Mock<IAuthorizationEnvelopeStore> store) =>
        new(password.Object, vault.Object, store.Object, NullLogger<AuthorizationEnvelopeActivator>.Instance);

    private static void AssertActivationError(
        Result result,
        AuthorizationEnvelopeActivationErrorCode expectedCode)
    {
        Assert.False(result.IsSuccess);
        Assert.Equal(
            expectedCode,
            Assert.Single(result.Errors.OfType<AuthorizationEnvelopeActivationError>()).Code);
    }
}
