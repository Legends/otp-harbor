using FluentResults;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TOTP.Core.Security.Interfaces;
using TOTP.Core.Security.Models;
using TOTP.Platform.Windows.Security;

namespace TOTP.Tests.Platform.Windows.Security;

public sealed class WindowsPlatformQuickUnlockTests
{
    [Fact]
    public void ProviderId_UsesReviewedWindowsProviderIdentifier()
    {
        var sut = CreateAdapter(new Mock<IHelloGate>());

        Assert.Equal(PlatformQuickUnlockContract.WindowsHelloTpmProvider, sut.ProviderId);
    }

    [Fact]
    public async Task GetAvailabilityAsync_DelegatesDetailedPlatformOutcome()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var hello = new Mock<IHelloGate>();
        hello.Setup(value => value.GetAvailabilityAsync(cancellationToken))
            .ReturnsAsync(PlatformQuickUnlockAvailability.DisabledByPolicy);
        var sut = CreateAdapter(hello);

        var result = await sut.GetAvailabilityAsync(cancellationToken);

        Assert.Equal(PlatformQuickUnlockAvailability.DisabledByPolicy, result);
    }

    [Fact]
    public async Task RegisterAsync_AfterVerification_EmitsReviewedMetadataAndClearsOwnedKeyCopy()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var vaultKey = Enumerable.Range(1, 32).Select(value => (byte)value).ToArray();
        var vaultKeySnapshot = (byte[])vaultKey.Clone();
        byte[]? providerInput = null;
        string? keyReference = null;
        var hello = new Mock<IHelloGate>();
        hello.Setup(value => value.RequestVerificationAsync(cancellationToken))
            .ReturnsAsync(AuthorizationResult.Success);
        hello.Setup(value => value.ProtectKeyAsync(
                It.IsAny<byte[]>(),
                It.IsAny<string>(),
                cancellationToken))
            .Callback<byte[], string, CancellationToken>((key, reference, _) =>
            {
                providerInput = key;
                keyReference = reference;
            })
            .ReturnsAsync(new byte[256]);
        var sut = CreateAdapter(hello);

        var result = await sut.RegisterAsync(vaultKey, cancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(vaultKeySnapshot, vaultKey);
        Assert.NotNull(providerInput);
        Assert.All(providerInput, value => Assert.Equal(0, value));
        Assert.StartsWith("TOTP_TPM_", keyReference, StringComparison.Ordinal);
        Assert.Equal(PlatformQuickUnlockContract.WindowsHelloTpmProvider, result.Value.Provider);
        Assert.Equal(PlatformQuickUnlockContract.WindowsHelloTpmProviderVersion, result.Value.ProviderVersion);
        Assert.Equal(PlatformQuickUnlockContract.UserVerificationRequired, result.Value.AuthenticationPolicy);
        Assert.Equal(keyReference, result.Value.KeyReference);
        Assert.Equal(PlatformQuickUnlockContract.RsaOaepSha256Algorithm, result.Value.WrappedKey.Algorithm);
        Assert.Null(result.Value.WrappedKey.Nonce);
        Assert.Equal(256, result.Value.WrappedKey.Ciphertext.Length);
        Assert.True(PlatformQuickUnlockContract.IsSupported(result.Value));
    }

    [Fact]
    public async Task RegisterAsync_WithInvalidVaultKey_FailsBeforePrompt()
    {
        var hello = new Mock<IHelloGate>();
        var sut = CreateAdapter(hello);

        var result = await sut.RegisterAsync(new byte[31], TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(PlatformQuickUnlockErrorCode.InvalidKeyMaterial, ErrorCode(result.Errors));
        hello.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(AuthorizationResult.Cancelled, PlatformQuickUnlockErrorCode.Cancelled)]
    [InlineData(AuthorizationResult.DisabledByPolicy, PlatformQuickUnlockErrorCode.DisabledByPolicy)]
    [InlineData(AuthorizationResult.TooManyAttempts, PlatformQuickUnlockErrorCode.RetriesExhausted)]
    [InlineData(AuthorizationResult.NotAvailable, PlatformQuickUnlockErrorCode.Unavailable)]
    [InlineData(AuthorizationResult.Failed, PlatformQuickUnlockErrorCode.RegistrationFailed)]
    public async Task RegisterAsync_WhenVerificationDoesNotSucceed_DoesNotCreateKey(
        AuthorizationResult verification,
        PlatformQuickUnlockErrorCode expectedError)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var hello = new Mock<IHelloGate>();
        hello.Setup(value => value.RequestVerificationAsync(cancellationToken)).ReturnsAsync(verification);
        var sut = CreateAdapter(hello);

        var result = await sut.RegisterAsync(new byte[32], cancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(expectedError, ErrorCode(result.Errors));
        hello.Verify(value => value.ProtectKeyAsync(
            It.IsAny<byte[]>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RegisterAsync_WhenProviderReturnsInvalidCiphertext_RemovesIncompleteKey()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        string? keyReference = null;
        var returnedCiphertext = Enumerable.Repeat((byte)7, 12).ToArray();
        var hello = new Mock<IHelloGate>();
        hello.Setup(value => value.RequestVerificationAsync(cancellationToken))
            .ReturnsAsync(AuthorizationResult.Success);
        hello.Setup(value => value.ProtectKeyAsync(
                It.IsAny<byte[]>(),
                It.IsAny<string>(),
                cancellationToken))
            .Callback<byte[], string, CancellationToken>((_, reference, _) => keyReference = reference)
            .ReturnsAsync(returnedCiphertext);
        hello.Setup(value => value.RemoveKeyAsync(It.IsAny<string>(), CancellationToken.None))
            .Returns(Task.CompletedTask);
        var sut = CreateAdapter(hello);

        var result = await sut.RegisterAsync(new byte[32], cancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(PlatformQuickUnlockErrorCode.InvalidKeyMaterial, ErrorCode(result.Errors));
        Assert.All(returnedCiphertext, value => Assert.Equal(0, value));
        hello.Verify(value => value.RemoveKeyAsync(keyReference!, CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task RegisterAsync_WhenKeyProtectionFails_RemovesIncompleteKeyAndReturnsTypedFailure()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        string? keyReference = null;
        var hello = new Mock<IHelloGate>();
        hello.Setup(value => value.RequestVerificationAsync(cancellationToken))
            .ReturnsAsync(AuthorizationResult.Success);
        hello.Setup(value => value.ProtectKeyAsync(
                It.IsAny<byte[]>(),
                It.IsAny<string>(),
                cancellationToken))
            .Callback<byte[], string, CancellationToken>((_, reference, _) => keyReference = reference)
            .ThrowsAsync(new InvalidOperationException("Synthetic provider failure."));
        hello.Setup(value => value.RemoveKeyAsync(It.IsAny<string>(), CancellationToken.None))
            .Returns(Task.CompletedTask);
        var sut = CreateAdapter(hello);

        var result = await sut.RegisterAsync(new byte[32], cancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(PlatformQuickUnlockErrorCode.RegistrationFailed, ErrorCode(result.Errors));
        hello.Verify(value => value.RemoveKeyAsync(keyReference!, CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task TryUnlockAsync_WithInvalidMetadata_FailsBeforePrompt()
    {
        var hello = new Mock<IHelloGate>();
        var sut = CreateAdapter(hello);

        var result = await sut.TryUnlockAsync(
            CreateWrapper() with { Provider = "unknown" },
            TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(PlatformQuickUnlockErrorCode.InvalidMetadata, ErrorCode(result.Errors));
        hello.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task TryUnlockAsync_AfterVerification_ReturnsOwnedKeyAndClearsProviderArray()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var recoveredKey = Enumerable.Range(1, 32).Select(value => (byte)value).ToArray();
        var expectedKey = (byte[])recoveredKey.Clone();
        var wrapper = CreateWrapper();
        var hello = new Mock<IHelloGate>();
        hello.Setup(value => value.RequestVerificationAsync(cancellationToken))
            .ReturnsAsync(AuthorizationResult.Success);
        hello.Setup(value => value.UnprotectKeyAsync(
                wrapper.WrappedKey.Ciphertext,
                wrapper.KeyReference,
                cancellationToken))
            .ReturnsAsync(recoveredKey);
        var sut = CreateAdapter(hello);

        var result = await sut.TryUnlockAsync(wrapper, cancellationToken);

        Assert.True(result.IsSuccess);
        using var attempt = result.Value;
        Assert.True(attempt.IsSuccess);
        Assert.Equal(expectedKey, attempt.VaultKey?.Memory.ToArray());
        Assert.All(recoveredKey, value => Assert.Equal(0, value));
    }

    [Fact]
    public async Task TryUnlockAsync_KeepsPromptSessionOpenThroughTpmKeyUnwrap()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var wrapper = CreateWrapper();
        var promptDisposed = false;
        var prompt = new Mock<IDisposable>();
        prompt.Setup(value => value.Dispose()).Callback(() => promptDisposed = true);
        var hello = new Mock<IHelloGate>();
        hello.Setup(value => value.BeginPromptSession()).Returns(prompt.Object);
        hello.Setup(value => value.RequestVerificationAsync(cancellationToken))
            .Callback(() => Assert.False(promptDisposed))
            .ReturnsAsync(AuthorizationResult.Success);
        hello.Setup(value => value.UnprotectKeyAsync(
                wrapper.WrappedKey.Ciphertext,
                wrapper.KeyReference,
                cancellationToken))
            .Callback(() => Assert.False(promptDisposed))
            .ReturnsAsync(new byte[32]);
        var sut = CreateAdapter(hello);

        var result = await sut.TryUnlockAsync(wrapper, cancellationToken);

        Assert.True(result.IsSuccess);
        result.Value.Dispose();
        Assert.True(promptDisposed);
        prompt.Verify(value => value.Dispose(), Times.Once);
    }

    [Theory]
    [InlineData(AuthorizationResult.Cancelled, PlatformQuickUnlockStatus.Cancelled)]
    [InlineData(AuthorizationResult.TooManyAttempts, PlatformQuickUnlockStatus.RetriesExhausted)]
    [InlineData(AuthorizationResult.DisabledByPolicy, PlatformQuickUnlockStatus.DisabledByPolicy)]
    [InlineData(AuthorizationResult.NotAvailable, PlatformQuickUnlockStatus.NotAvailable)]
    [InlineData(AuthorizationResult.NotConfigured, PlatformQuickUnlockStatus.NotConfigured)]
    [InlineData(AuthorizationResult.Failed, PlatformQuickUnlockStatus.VerificationFailed)]
    public async Task TryUnlockAsync_WhenVerificationDoesNotSucceed_ReturnsExpectedOutcome(
        AuthorizationResult verification,
        PlatformQuickUnlockStatus expectedStatus)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var hello = new Mock<IHelloGate>();
        hello.Setup(value => value.RequestVerificationAsync(cancellationToken)).ReturnsAsync(verification);
        var sut = CreateAdapter(hello);

        var result = await sut.TryUnlockAsync(CreateWrapper(), cancellationToken);

        Assert.True(result.IsSuccess);
        using var attempt = result.Value;
        Assert.Equal(expectedStatus, attempt.Status);
        Assert.Null(attempt.VaultKey);
        hello.Verify(value => value.UnprotectKeyAsync(
            It.IsAny<byte[]>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task TryUnlockAsync_WhenPlatformKeyIsMissing_RoutesToPasswordRecovery()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var hello = new Mock<IHelloGate>();
        hello.Setup(value => value.RequestVerificationAsync(cancellationToken))
            .ReturnsAsync(AuthorizationResult.Success);
        hello.Setup(value => value.UnprotectKeyAsync(
                It.IsAny<byte[]>(),
                It.IsAny<string>(),
                cancellationToken))
            .ReturnsAsync((byte[]?)null);
        var sut = CreateAdapter(hello);

        var result = await sut.TryUnlockAsync(CreateWrapper(), cancellationToken);

        Assert.True(result.IsSuccess);
        using var attempt = result.Value;
        Assert.Equal(PlatformQuickUnlockStatus.KeyNotFound, attempt.Status);
    }

    [Fact]
    public async Task TryUnlockAsync_WhenRecoveredKeyIsInvalid_ClearsItAndReturnsTypedFailure()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var recoveredKey = Enumerable.Repeat((byte)9, 31).ToArray();
        var hello = new Mock<IHelloGate>();
        hello.Setup(value => value.RequestVerificationAsync(cancellationToken))
            .ReturnsAsync(AuthorizationResult.Success);
        hello.Setup(value => value.UnprotectKeyAsync(
                It.IsAny<byte[]>(),
                It.IsAny<string>(),
                cancellationToken))
            .ReturnsAsync(recoveredKey);
        var sut = CreateAdapter(hello);

        var result = await sut.TryUnlockAsync(CreateWrapper(), cancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(PlatformQuickUnlockErrorCode.InvalidKeyMaterial, ErrorCode(result.Errors));
        Assert.All(recoveredKey, value => Assert.Equal(0, value));
    }

    [Fact]
    public async Task RemoveAsync_WithValidMetadata_DelegatesIdempotentRemoval()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var wrapper = CreateWrapper();
        var hello = new Mock<IHelloGate>();
        hello.Setup(value => value.RemoveKeyAsync(wrapper.KeyReference, cancellationToken))
            .Returns(Task.CompletedTask);
        var sut = CreateAdapter(hello);

        var result = await sut.RemoveAsync(wrapper, cancellationToken);

        Assert.True(result.IsSuccess);
        hello.Verify(value => value.RemoveKeyAsync(wrapper.KeyReference, cancellationToken), Times.Once);
    }

    [Fact]
    public async Task RemoveAsync_WithInvalidMetadata_FailsBeforePlatformRemoval()
    {
        var hello = new Mock<IHelloGate>();
        var sut = CreateAdapter(hello);

        var result = await sut.RemoveAsync(
            CreateWrapper() with { AuthenticationPolicy = "unreviewed-policy" },
            TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(PlatformQuickUnlockErrorCode.InvalidMetadata, ErrorCode(result.Errors));
        hello.VerifyNoOtherCalls();
    }

    private static WindowsPlatformQuickUnlock CreateAdapter(Mock<IHelloGate> hello) =>
        new(hello.Object, NullLogger<WindowsPlatformQuickUnlock>.Instance);

    private static PlatformQuickUnlockWrapperV2 CreateWrapper() => new()
    {
        Provider = PlatformQuickUnlockContract.WindowsHelloTpmProvider,
        ProviderVersion = PlatformQuickUnlockContract.WindowsHelloTpmProviderVersion,
        AuthenticationPolicy = PlatformQuickUnlockContract.UserVerificationRequired,
        KeyReference = "TOTP_TPM_SYNTHETIC_ADAPTER",
        WrappedKey = new PlatformWrappedKeyV2
        {
            Algorithm = PlatformQuickUnlockContract.RsaOaepSha256Algorithm,
            Ciphertext = new byte[256]
        }
    };

    private static PlatformQuickUnlockErrorCode ErrorCode(IEnumerable<IError> errors) =>
        Assert.IsType<PlatformQuickUnlockError>(Assert.Single(errors)).Code;
}
