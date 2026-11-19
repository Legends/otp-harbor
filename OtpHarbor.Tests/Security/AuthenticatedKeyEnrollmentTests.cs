using FluentResults;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TOTP.Core.Enums;
using TOTP.Core.Security.Interfaces;
using TOTP.Core.Security.Models;
using TOTP.Infrastructure.Security;

namespace TOTP.Tests.Security;

public sealed class AuthenticatedKeyEnrollmentTests
{
    [Theory]
    [InlineData(false, "success")]
    [InlineData(true, "success")]
    [InlineData(false, "verification failure")]
    [InlineData(true, "verification failure")]
    [InlineData(false, "save failure")]
    [InlineData(true, "save failure")]
    [InlineData(false, "cancel registration")]
    [InlineData(true, "cancel registration")]
    public async Task Enrollment_VerifiesKeyPreservesRecoveryAndRollsBackFailures(bool unattended, string scenario)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var token = cancellation.Token;
        var envelope = AuthorizationEnvelopeV2CodecTests.CreateEnvelope();
        var passwordCiphertext = envelope.PasswordWrapper.WrappedKey.Ciphertext.ToArray();
        var key = Enumerable.Range(1, 32).Select(value => (byte)value).ToArray();
        var store = new Mock<IAuthorizationEnvelopeStore>();
        store.Setup(value => value.LoadAsync(token)).ReturnsAsync(Result.Ok<AuthorizationEnvelopeV2?>(envelope));
        var verified = false;
        var vault = new Mock<IStoredVaultKeyVerifier>();
        vault.Setup(value => value.VerifyAsync(It.IsAny<ReadOnlyMemory<byte>>(), token))
            .Callback((ReadOnlyMemory<byte> received, CancellationToken _) =>
            {
                Assert.Equal(key, received.ToArray());
                verified = true;
            }).ReturnsAsync(scenario == "verification failure"
                ? Result.Fail<VaultKeyVerificationStatus>("Synthetic vault verification failure")
                : Result.Ok(VaultKeyVerificationStatus.Verified));
        var password = new Mock<IMasterPasswordService>(MockBehavior.Strict);
        var platform = new Mock<IPlatformQuickUnlock>();
        if (unattended) platform.As<IPlatformUnattendedUnlock>();
        var provider = unattended ? PlatformQuickUnlockContract.AndroidKeystoreUnattendedProvider
            : PlatformQuickUnlockContract.AndroidKeystoreBiometricProvider;
        platform.SetupGet(value => value.ProviderId).Returns(provider);
        platform.SetupGet(value => value.UnlockMethod).Returns(PreferredUnlockMethod.PlatformQuickUnlock);
        platform.Setup(value => value.GetAvailabilityAsync(token)).ReturnsAsync(PlatformQuickUnlockAvailability.Available);
        var wrapper = new PlatformQuickUnlockWrapperV2
        {
            Provider = provider,
            ProviderVersion = 1,
            AuthenticationPolicy = unattended ? PlatformQuickUnlockContract.UserVerificationNotRequired
                : PlatformQuickUnlockContract.UserVerificationRequired,
            KeyReference = "synthetic-enrollment-test",
            WrappedKey = new() { Algorithm = PlatformQuickUnlockContract.AndroidAes256GcmAlgorithm,
                Nonce = new byte[12], Ciphertext = Enumerable.Repeat((byte)7, 48).ToArray() }
        };
        ReadOnlyMemory<byte> ownedKey = default;
        platform.Setup(value => value.RegisterAsync(It.IsAny<ReadOnlyMemory<byte>>(), token))
            .Callback((ReadOnlyMemory<byte> received, CancellationToken _) =>
            {
                Assert.True(verified);
                Assert.Equal(key, received.ToArray());
                ownedKey = received;
                if (scenario == "cancel registration") cancellation.Cancel();
            }).ReturnsAsync(Result.Ok(wrapper));
        platform.Setup(value => value.RemoveAsync(wrapper, CancellationToken.None)).ReturnsAsync(Result.Ok());
        store.Setup(value => value.SaveAsync(It.IsAny<AuthorizationEnvelopeV2>(), token))
            .Callback((AuthorizationEnvelopeV2 saved, CancellationToken _) =>
            {
                Assert.True(verified);
                Assert.Same(envelope.PasswordWrapper, saved.PasswordWrapper);
                Assert.Equal(passwordCiphertext, saved.PasswordWrapper.WrappedKey.Ciphertext);
                Assert.Equal(provider, saved.QuickUnlockWrapper?.Provider);
            }).ReturnsAsync(scenario == "save failure" ? Result.Fail("Synthetic persistence failure") : Result.Ok());
        using var quickEnrollment = new PlatformQuickUnlockEnrollment(store.Object, password.Object, vault.Object,
            platform.Object, NullLogger<PlatformQuickUnlockEnrollment>.Instance);
        using var unattendedEnrollment = unattended ? new PlatformUnattendedUnlockEnrollment(store.Object, password.Object,
            vault.Object, platform.As<IPlatformUnattendedUnlock>().Object, [platform.Object], NullLogger<PlatformUnattendedUnlockEnrollment>.Instance) : null;
        Task<Result> Enroll() => unattended
            ? unattendedEnrollment!.EnableWithAuthenticatedKeyAsync(key, token)
            : quickEnrollment.EnableWithAuthenticatedKeyAsync(key, PreferredUnlockMethod.PlatformQuickUnlock, token);

        if (scenario == "cancel registration")
            await Assert.ThrowsAnyAsync<OperationCanceledException>(Enroll);
        else
            Assert.Equal(scenario == "success", (await Enroll()).IsSuccess);

        store.Verify(value => value.SaveAsync(It.IsAny<AuthorizationEnvelopeV2>(), token),
            scenario is "success" or "save failure" ? Times.Once() : Times.Never());
        platform.Verify(value => value.RegisterAsync(It.IsAny<ReadOnlyMemory<byte>>(), token),
            scenario == "verification failure" ? Times.Never() : Times.Once());
        platform.Verify(value => value.RemoveAsync(wrapper, CancellationToken.None),
            scenario is "save failure" or "cancel registration" ? Times.Once() : Times.Never());
        password.VerifyNoOtherCalls();
        Assert.Equal(Enumerable.Range(1, 32).Select(value => (byte)value), key);
        Assert.All(ownedKey.ToArray(), value => Assert.Equal(0, value));
        Assert.All(envelope.PasswordWrapper.WrappedKey.Ciphertext, value => Assert.Equal(0, value));
        if (scenario != "verification failure")
            Assert.All(wrapper.WrappedKey.Ciphertext, value => Assert.Equal(0, value));
    }
}
