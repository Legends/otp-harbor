using FluentResults;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TOTP.Core.Security;
using TOTP.Core.Security.Interfaces;
using TOTP.Core.Security.Models;
using TOTP.Infrastructure.Security;

namespace TOTP.Tests.Security;

public sealed class AuthorizationEnvelopeSessionTests
{
    [Fact]
    public async Task InitializeAsync_WhenEnvelopeIsMissing_ReportsUnconfiguredFirstRun()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var store = StoreReturning(null, cancellationToken);
        using var sut = CreateSession(store);

        var result = await sut.InitializeAsync(cancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(new AuthorizationEnvelopeSessionState(true, false, false), result.Value);
        Assert.Equal(result.Value, sut.State);
    }

    [Fact]
    public async Task InitializeAsync_WithSupportedQuickUnlock_ReportsCapability()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var envelope = AuthorizationEnvelopeV2CodecTests.CreateEnvelope() with
        {
            QuickUnlockWrapper = CreateSupportedQuickUnlockWrapper()
        };
        var store = StoreReturning(envelope, cancellationToken);
        using var sut = CreateSession(store);

        var result = await sut.InitializeAsync(cancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            new AuthorizationEnvelopeSessionState(
                true,
                true,
                true,
                PlatformUnlockMethod: TOTP.Core.Enums.PreferredUnlockMethod.PlatformQuickUnlock),
            result.Value);
    }

    [Fact]
    public async Task InitializeAsync_WithUnsupportedQuickUnlock_PreservesPasswordRecoveryOnly()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var unsupported = CreateSupportedQuickUnlockWrapper() with { Provider = "future-provider" };
        var envelope = AuthorizationEnvelopeV2CodecTests.CreateEnvelope() with
        {
            QuickUnlockWrapper = unsupported
        };
        var store = StoreReturning(envelope, cancellationToken);
        using var sut = CreateSession(store);

        var result = await sut.InitializeAsync(cancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(new AuthorizationEnvelopeSessionState(true, true, false), result.Value);
    }

    [Theory]
    [InlineData(AuthorizationEnvelopeErrorCode.ReadAccessDenied)]
    [InlineData(AuthorizationEnvelopeErrorCode.Malformed)]
    public async Task InitializeAsync_WhenStoreFails_RemainsUninitializedAndPreservesTypedFailure(
        AuthorizationEnvelopeErrorCode storeErrorCode)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var store = new Mock<IAuthorizationEnvelopeStore>();
        store.Setup(value => value.LoadAsync(cancellationToken))
            .ReturnsAsync(Result.Fail<AuthorizationEnvelopeV2?>(new AuthorizationEnvelopeError(
                storeErrorCode,
                "synthetic failure")));
        using var sut = CreateSession(store);

        var result = await sut.InitializeAsync(cancellationToken);

        AssertSessionError(result, AuthorizationEnvelopeSessionErrorCode.LoadFailed);
        Assert.Equal(
            storeErrorCode,
            Assert.Single(result.Errors.OfType<AuthorizationEnvelopeError>()).Code);
        Assert.Equal(AuthorizationEnvelopeSessionState.NotInitialized, sut.State);
    }

    [Fact]
    public async Task TryUnlockWithPasswordAsync_BeforeInitialization_ReturnsTypedFailure()
    {
        using var sut = CreateSession(new Mock<IAuthorizationEnvelopeStore>());

        var result = await sut.TryUnlockWithPasswordAsync(
            "password",
            TestContext.Current.CancellationToken);

        AssertSessionError(result, AuthorizationEnvelopeSessionErrorCode.NotInitialized);
    }

    [Fact]
    public async Task TryUnlockWithPasswordAsync_WhenUnconfigured_ReturnsNotConfigured()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var store = StoreReturning(null, cancellationToken);
        using var sut = CreateSession(store);
        Assert.True((await sut.InitializeAsync(cancellationToken)).IsSuccess);

        var result = await sut.TryUnlockWithPasswordAsync("password", cancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(AuthorizationResult.NotConfigured, result.Value);
    }

    [Fact]
    public async Task TryUnlockWithPasswordAsync_WhenPasswordIsWrong_ReturnsInvalidCredentials()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var store = StoreReturning(AuthorizationEnvelopeV2CodecTests.CreateEnvelope(), cancellationToken);
        var password = new Mock<IMasterPasswordService>();
        password.Setup(value => value.UnwrapKeyV2Async(
                It.IsAny<PasswordKeyWrapperV2>(),
                "wrong-password",
                cancellationToken))
            .ReturnsAsync((byte[]?)null);
        var vault = new Mock<IStoredVaultKeyVerifier>();
        var security = new Mock<ISecurityContext>();
        using var sut = CreateSession(store, password, vault, security);
        Assert.True((await sut.InitializeAsync(cancellationToken)).IsSuccess);

        var result = await sut.TryUnlockWithPasswordAsync("wrong-password", cancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(AuthorizationResult.InvalidCredentials, result.Value);
        vault.VerifyNoOtherCalls();
        security.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(VaultKeyVerificationStatus.Verified)]
    [InlineData(VaultKeyVerificationStatus.VaultNotFound)]
    public async Task TryUnlockWithPasswordAsync_AfterVaultVerification_UnlocksContextAndClearsKey(
        VaultKeyVerificationStatus vaultStatus)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var store = StoreReturning(AuthorizationEnvelopeV2CodecTests.CreateEnvelope(), cancellationToken);
        var recoveredKey = Enumerable.Range(1, 32).Select(value => (byte)value).ToArray();
        var expectedKey = (byte[])recoveredKey.Clone();
        var password = PasswordReturning(recoveredKey, cancellationToken);
        var vault = new Mock<IStoredVaultKeyVerifier>();
        vault.Setup(value => value.VerifyAsync(It.IsAny<ReadOnlyMemory<byte>>(), cancellationToken))
            .ReturnsAsync(Result.Ok(vaultStatus));
        byte[]? contextKey = null;
        var security = new Mock<ISecurityContext>();
        security.Setup(value => value.SetDek(It.IsAny<byte[]>()))
            .Callback<byte[]>(value => contextKey = (byte[])value.Clone());
        using var sut = CreateSession(store, password, vault, security);
        Assert.True((await sut.InitializeAsync(cancellationToken)).IsSuccess);

        var result = await sut.TryUnlockWithPasswordAsync("password", cancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(AuthorizationResult.Success, result.Value);
        Assert.Equal(expectedKey, contextKey);
        Assert.All(recoveredKey, value => Assert.Equal(0, value));
        security.Verify(value => value.SetDek(It.IsAny<byte[]>()), Times.Once);
    }

    [Fact]
    public async Task TryUnlockWithPasswordAsync_WhenRecoveredKeyLengthIsInvalid_FailsClosedAndClearsKey()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var store = StoreReturning(AuthorizationEnvelopeV2CodecTests.CreateEnvelope(), cancellationToken);
        var recoveredKey = new byte[31];
        Array.Fill(recoveredKey, (byte)7);
        var password = PasswordReturning(recoveredKey, cancellationToken);
        var vault = new Mock<IStoredVaultKeyVerifier>();
        var security = new Mock<ISecurityContext>();
        using var sut = CreateSession(store, password, vault, security);
        Assert.True((await sut.InitializeAsync(cancellationToken)).IsSuccess);

        var result = await sut.TryUnlockWithPasswordAsync("password", cancellationToken);

        AssertSessionError(result, AuthorizationEnvelopeSessionErrorCode.VaultVerificationFailed);
        Assert.All(recoveredKey, value => Assert.Equal(0, value));
        vault.VerifyNoOtherCalls();
        security.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(VaultKeyVerificationStatus.AuthenticationFailed)]
    [InlineData(VaultKeyVerificationStatus.InvalidVaultFormat)]
    [InlineData(VaultKeyVerificationStatus.InvalidCandidateKey)]
    public async Task TryUnlockWithPasswordAsync_WhenVaultDoesNotVerify_DoesNotUnlock(
        VaultKeyVerificationStatus vaultStatus)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var store = StoreReturning(AuthorizationEnvelopeV2CodecTests.CreateEnvelope(), cancellationToken);
        var recoveredKey = new byte[32];
        var password = PasswordReturning(recoveredKey, cancellationToken);
        var vault = new Mock<IStoredVaultKeyVerifier>();
        vault.Setup(value => value.VerifyAsync(It.IsAny<ReadOnlyMemory<byte>>(), cancellationToken))
            .ReturnsAsync(Result.Ok(vaultStatus));
        var security = new Mock<ISecurityContext>();
        using var sut = CreateSession(store, password, vault, security);
        Assert.True((await sut.InitializeAsync(cancellationToken)).IsSuccess);

        var result = await sut.TryUnlockWithPasswordAsync("password", cancellationToken);

        AssertSessionError(result, AuthorizationEnvelopeSessionErrorCode.VaultVerificationFailed);
        security.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task TryUnlockWithPasswordAsync_WhenVaultReadFails_PreservesTypedFailure()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var store = StoreReturning(AuthorizationEnvelopeV2CodecTests.CreateEnvelope(), cancellationToken);
        var password = PasswordReturning(new byte[32], cancellationToken);
        var vault = new Mock<IStoredVaultKeyVerifier>();
        vault.Setup(value => value.VerifyAsync(It.IsAny<ReadOnlyMemory<byte>>(), cancellationToken))
            .ReturnsAsync(Result.Fail<VaultKeyVerificationStatus>(new StoredVaultVerificationError(
                StoredVaultVerificationErrorCode.ReadFailed,
                "synthetic failure")));
        var security = new Mock<ISecurityContext>();
        using var sut = CreateSession(store, password, vault, security);
        Assert.True((await sut.InitializeAsync(cancellationToken)).IsSuccess);

        var result = await sut.TryUnlockWithPasswordAsync("password", cancellationToken);

        AssertSessionError(result, AuthorizationEnvelopeSessionErrorCode.VaultVerificationFailed);
        Assert.Equal(
            StoredVaultVerificationErrorCode.ReadFailed,
            Assert.Single(result.Errors.OfType<StoredVaultVerificationError>()).Code);
        security.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task TryUnlockWithPlatformAsync_BeforeInitialization_ReturnsTypedFailure()
    {
        var platform = new Mock<IPlatformQuickUnlock>();
        using var sut = CreateSession(
            new Mock<IAuthorizationEnvelopeStore>(),
            platformAdapters: [platform.Object]);

        var result = await sut.TryUnlockWithPlatformAsync(TestContext.Current.CancellationToken);

        AssertSessionError(result, AuthorizationEnvelopeSessionErrorCode.NotInitialized);
        platform.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task TryUnlockWithPlatformAsync_WhenEnvelopeIsMissing_ReturnsNotConfigured()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var platform = new Mock<IPlatformQuickUnlock>();
        var store = StoreReturning(null, cancellationToken);
        using var sut = CreateSession(store, platformAdapters: [platform.Object]);
        Assert.True((await sut.InitializeAsync(cancellationToken)).IsSuccess);

        var result = await sut.TryUnlockWithPlatformAsync(cancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(AuthorizationResult.NotConfigured, result.Value);
        platform.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task TryUnlockWithPlatformAsync_WithoutSupportedWrapper_RequiresPasswordWithoutPlatformAccess()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var platform = new Mock<IPlatformQuickUnlock>();
        var store = StoreReturning(AuthorizationEnvelopeV2CodecTests.CreateEnvelope(), cancellationToken);
        using var sut = CreateSession(store, platformAdapters: [platform.Object]);
        Assert.True((await sut.InitializeAsync(cancellationToken)).IsSuccess);

        var result = await sut.TryUnlockWithPlatformAsync(cancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(AuthorizationResult.PasswordRequired, result.Value);
        platform.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task TryUnlockWithPlatformAsync_WithoutMatchingAdapter_RequiresPassword()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var envelope = CreateEnvelopeWithQuickUnlock();
        var otherPlatform = new Mock<IPlatformQuickUnlock>();
        otherPlatform.SetupGet(value => value.ProviderId).Returns("other-provider");
        var store = StoreReturning(envelope, cancellationToken);
        using var sut = CreateSession(store, platformAdapters: [otherPlatform.Object]);
        Assert.True((await sut.InitializeAsync(cancellationToken)).IsSuccess);

        var result = await sut.TryUnlockWithPlatformAsync(cancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(AuthorizationResult.PasswordRequired, result.Value);
        otherPlatform.VerifyGet(value => value.ProviderId, Times.Once);
        otherPlatform.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(PlatformQuickUnlockStatus.Cancelled, AuthorizationResult.Cancelled)]
    [InlineData(PlatformQuickUnlockStatus.DisabledByPolicy, AuthorizationResult.DisabledByPolicy)]
    [InlineData(PlatformQuickUnlockStatus.RetriesExhausted, AuthorizationResult.TooManyAttempts)]
    [InlineData(PlatformQuickUnlockStatus.VerificationFailed, AuthorizationResult.Failed)]
    [InlineData(PlatformQuickUnlockStatus.NotAvailable, AuthorizationResult.PasswordRequired)]
    [InlineData(PlatformQuickUnlockStatus.NotConfigured, AuthorizationResult.PasswordRequired)]
    [InlineData(PlatformQuickUnlockStatus.KeyNotFound, AuthorizationResult.PasswordRequired)]
    public async Task TryUnlockWithPlatformAsync_MapsExpectedPlatformOutcome(
        PlatformQuickUnlockStatus status,
        AuthorizationResult expectedResult)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var envelope = CreateEnvelopeWithQuickUnlock();
        var platform = PlatformFor(envelope.QuickUnlockWrapper!);
        platform.Setup(value => value.TryUnlockAsync(envelope.QuickUnlockWrapper!, cancellationToken))
            .ReturnsAsync(Result.Ok(PlatformQuickUnlockAttempt.WithoutKey(status)));
        var vault = new Mock<IStoredVaultKeyVerifier>();
        var security = new Mock<ISecurityContext>();
        var store = StoreReturning(envelope, cancellationToken);
        using var sut = CreateSession(
            store,
            vault: vault,
            security: security,
            platformAdapters: [platform.Object]);
        Assert.True((await sut.InitializeAsync(cancellationToken)).IsSuccess);

        var result = await sut.TryUnlockWithPlatformAsync(cancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(expectedResult, result.Value);
        vault.VerifyNoOtherCalls();
        security.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task TryUnlockWithPlatformAsync_WhenPlatformKeyWasReset_PasswordRecoveryStillUnlocks()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var envelope = CreateEnvelopeWithQuickUnlock();
        var platform = PlatformFor(envelope.QuickUnlockWrapper!);
        platform.Setup(value => value.TryUnlockAsync(envelope.QuickUnlockWrapper!, cancellationToken))
            .ReturnsAsync(Result.Ok(PlatformQuickUnlockAttempt.WithoutKey(
                PlatformQuickUnlockStatus.KeyNotFound)));
        var recoveredKey = Enumerable.Range(1, 32).Select(value => (byte)value).ToArray();
        var password = PasswordReturning(recoveredKey, cancellationToken);
        var vault = new Mock<IStoredVaultKeyVerifier>();
        vault.Setup(value => value.VerifyAsync(It.IsAny<ReadOnlyMemory<byte>>(), cancellationToken))
            .ReturnsAsync(Result.Ok(VaultKeyVerificationStatus.Verified));
        var security = new Mock<ISecurityContext>();
        var store = StoreReturning(envelope, cancellationToken);
        using var sut = CreateSession(
            store,
            password,
            vault,
            security,
            [platform.Object]);
        Assert.True((await sut.InitializeAsync(cancellationToken)).IsSuccess);

        var platformResult = await sut.TryUnlockWithPlatformAsync(cancellationToken);
        var passwordResult = await sut.TryUnlockWithPasswordAsync("password", cancellationToken);

        Assert.True(platformResult.IsSuccess);
        Assert.Equal(AuthorizationResult.PasswordRequired, platformResult.Value);
        Assert.True(passwordResult.IsSuccess);
        Assert.Equal(AuthorizationResult.Success, passwordResult.Value);
        security.Verify(value => value.SetDek(It.IsAny<byte[]>()), Times.Once);
        Assert.All(recoveredKey, value => Assert.Equal(0, value));
    }

    [Theory]
    [InlineData(VaultKeyVerificationStatus.Verified)]
    [InlineData(VaultKeyVerificationStatus.VaultNotFound)]
    public async Task TryUnlockWithPlatformAsync_AfterVaultVerification_UnlocksAndDisposesPlatformKey(
        VaultKeyVerificationStatus vaultStatus)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var envelope = CreateEnvelopeWithQuickUnlock();
        var expectedKey = Enumerable.Range(1, 32).Select(value => (byte)value).ToArray();
        var platformKey = SensitiveBuffer.CopyFrom(expectedKey);
        var platform = PlatformFor(envelope.QuickUnlockWrapper!);
        platform.Setup(value => value.TryUnlockAsync(envelope.QuickUnlockWrapper!, cancellationToken))
            .ReturnsAsync(Result.Ok(PlatformQuickUnlockAttempt.Successful(platformKey)));
        var vault = new Mock<IStoredVaultKeyVerifier>();
        vault.Setup(value => value.VerifyAsync(It.IsAny<ReadOnlyMemory<byte>>(), cancellationToken))
            .Callback<ReadOnlyMemory<byte>, CancellationToken>((key, _) => Assert.Equal(expectedKey, key.ToArray()))
            .ReturnsAsync(Result.Ok(vaultStatus));
        byte[]? contextKey = null;
        byte[]? contextInput = null;
        var security = new Mock<ISecurityContext>();
        security.Setup(value => value.SetDek(It.IsAny<byte[]>()))
            .Callback<byte[]>(key =>
            {
                contextInput = key;
                contextKey = (byte[])key.Clone();
            });
        var store = StoreReturning(envelope, cancellationToken);
        using var sut = CreateSession(
            store,
            vault: vault,
            security: security,
            platformAdapters: [platform.Object]);
        Assert.True((await sut.InitializeAsync(cancellationToken)).IsSuccess);

        var result = await sut.TryUnlockWithPlatformAsync(cancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(AuthorizationResult.Success, result.Value);
        Assert.Equal(expectedKey, contextKey);
        Assert.NotNull(contextInput);
        Assert.All(contextInput, value => Assert.Equal(0, value));
        Assert.Throws<ObjectDisposedException>(() => _ = platformKey.Memory);
        security.Verify(value => value.SetDek(It.IsAny<byte[]>()), Times.Once);
    }

    [Fact]
    public async Task TryUnlockWithPlatformAsync_WhenRecoveredKeyDoesNotVerify_DoesNotUnlockAndDisposesKey()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var envelope = CreateEnvelopeWithQuickUnlock();
        var platformKey = SensitiveBuffer.CopyFrom(new byte[32]);
        var platform = PlatformFor(envelope.QuickUnlockWrapper!);
        platform.Setup(value => value.TryUnlockAsync(envelope.QuickUnlockWrapper!, cancellationToken))
            .ReturnsAsync(Result.Ok(PlatformQuickUnlockAttempt.Successful(platformKey)));
        var vault = new Mock<IStoredVaultKeyVerifier>();
        vault.Setup(value => value.VerifyAsync(It.IsAny<ReadOnlyMemory<byte>>(), cancellationToken))
            .ReturnsAsync(Result.Ok(VaultKeyVerificationStatus.AuthenticationFailed));
        var security = new Mock<ISecurityContext>();
        var store = StoreReturning(envelope, cancellationToken);
        using var sut = CreateSession(
            store,
            vault: vault,
            security: security,
            platformAdapters: [platform.Object]);
        Assert.True((await sut.InitializeAsync(cancellationToken)).IsSuccess);

        var result = await sut.TryUnlockWithPlatformAsync(cancellationToken);

        AssertSessionError(result, AuthorizationEnvelopeSessionErrorCode.VaultVerificationFailed);
        Assert.Throws<ObjectDisposedException>(() => _ = platformKey.Memory);
        security.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task TryUnlockWithPlatformAsync_WhenAdapterFails_PreservesTypedFailure()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var envelope = CreateEnvelopeWithQuickUnlock();
        var platform = PlatformFor(envelope.QuickUnlockWrapper!);
        platform.Setup(value => value.TryUnlockAsync(envelope.QuickUnlockWrapper!, cancellationToken))
            .ReturnsAsync(Result.Fail<PlatformQuickUnlockAttempt>(new PlatformQuickUnlockError(
                PlatformQuickUnlockErrorCode.UnlockFailed,
                "synthetic platform failure")));
        var store = StoreReturning(envelope, cancellationToken);
        using var sut = CreateSession(store, platformAdapters: [platform.Object]);
        Assert.True((await sut.InitializeAsync(cancellationToken)).IsSuccess);

        var result = await sut.TryUnlockWithPlatformAsync(cancellationToken);

        AssertSessionError(result, AuthorizationEnvelopeSessionErrorCode.PlatformUnlockFailed);
        Assert.Equal(
            PlatformQuickUnlockErrorCode.UnlockFailed,
            Assert.Single(result.Errors.OfType<PlatformQuickUnlockError>()).Code);
    }

    [Fact]
    public async Task TryUnlockWithPlatformAsync_WhenApplicationIsCancelled_PropagatesCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        var envelope = CreateEnvelopeWithQuickUnlock();
        var platform = PlatformFor(envelope.QuickUnlockWrapper!);
        platform.Setup(value => value.TryUnlockAsync(envelope.QuickUnlockWrapper!, cancellation.Token))
            .Returns(async () =>
            {
                await cancellation.CancelAsync();
                throw new OperationCanceledException(cancellation.Token);
            });
        var vault = new Mock<IStoredVaultKeyVerifier>();
        var security = new Mock<ISecurityContext>();
        var store = StoreReturning(envelope, cancellation.Token);
        using var sut = CreateSession(
            store,
            vault: vault,
            security: security,
            platformAdapters: [platform.Object]);
        Assert.True((await sut.InitializeAsync(cancellation.Token)).IsSuccess);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => sut.TryUnlockWithPlatformAsync(cancellation.Token));

        vault.VerifyNoOtherCalls();
        security.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task InitializeAsync_WhenReloaded_ClearsPreviouslyCachedEnvelopeBuffers()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var envelope = AuthorizationEnvelopeV2CodecTests.CreateEnvelope();
        var store = new Mock<IAuthorizationEnvelopeStore>();
        store.SetupSequence(value => value.LoadAsync(cancellationToken))
            .ReturnsAsync(Result.Ok<AuthorizationEnvelopeV2?>(envelope))
            .ReturnsAsync(Result.Ok<AuthorizationEnvelopeV2?>(null));
        using var sut = CreateSession(store);
        Assert.True((await sut.InitializeAsync(cancellationToken)).IsSuccess);

        var result = await sut.InitializeAsync(cancellationToken);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.IsConfigured);
        Assert.All(envelope.PasswordWrapper.Kdf.Salt, value => Assert.Equal(0, value));
        Assert.All(envelope.PasswordWrapper.WrappedKey.Nonce, value => Assert.Equal(0, value));
        Assert.All(envelope.PasswordWrapper.WrappedKey.Ciphertext, value => Assert.Equal(0, value));
    }

    [Fact]
    public async Task Dispose_AfterInitialization_ClearsCachedEnvelopeBuffers()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var envelope = AuthorizationEnvelopeV2CodecTests.CreateEnvelope();
        var store = StoreReturning(envelope, cancellationToken);
        var sut = CreateSession(store);
        Assert.True((await sut.InitializeAsync(cancellationToken)).IsSuccess);

        sut.Dispose();

        Assert.All(envelope.PasswordWrapper.Kdf.Salt, value => Assert.Equal(0, value));
        Assert.All(envelope.PasswordWrapper.WrappedKey.Nonce, value => Assert.Equal(0, value));
        Assert.All(envelope.PasswordWrapper.WrappedKey.Ciphertext, value => Assert.Equal(0, value));
    }

    [Fact]
    public async Task InitializeAsync_WhenCancelled_PropagatesWithoutLoading()
    {
        var store = new Mock<IAuthorizationEnvelopeStore>();
        using var sut = CreateSession(store);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => sut.InitializeAsync(cancellation.Token));

        store.VerifyNoOtherCalls();
        Assert.Equal(AuthorizationEnvelopeSessionState.NotInitialized, sut.State);
    }

    private static Mock<IAuthorizationEnvelopeStore> StoreReturning(
        AuthorizationEnvelopeV2? envelope,
        CancellationToken cancellationToken)
    {
        var store = new Mock<IAuthorizationEnvelopeStore>();
        store.Setup(value => value.LoadAsync(cancellationToken))
            .ReturnsAsync(Result.Ok<AuthorizationEnvelopeV2?>(envelope));
        return store;
    }

    private static Mock<IMasterPasswordService> PasswordReturning(
        byte[] recoveredKey,
        CancellationToken cancellationToken)
    {
        var password = new Mock<IMasterPasswordService>();
        password.Setup(value => value.UnwrapKeyV2Async(
                It.IsAny<PasswordKeyWrapperV2>(),
                "password",
                cancellationToken))
            .ReturnsAsync(recoveredKey);
        return password;
    }

    private static AuthorizationEnvelopeSession CreateSession(
        Mock<IAuthorizationEnvelopeStore> store,
        Mock<IMasterPasswordService>? password = null,
        Mock<IStoredVaultKeyVerifier>? vault = null,
        Mock<ISecurityContext>? security = null,
        IEnumerable<IPlatformQuickUnlock>? platformAdapters = null) =>
        new(
            store.Object,
            (password ?? new Mock<IMasterPasswordService>()).Object,
            (vault ?? new Mock<IStoredVaultKeyVerifier>()).Object,
            (security ?? new Mock<ISecurityContext>()).Object,
            platformAdapters ?? [],
            NullLogger<AuthorizationEnvelopeSession>.Instance);

    private static AuthorizationEnvelopeV2 CreateEnvelopeWithQuickUnlock() =>
        AuthorizationEnvelopeV2CodecTests.CreateEnvelope() with
        {
            QuickUnlockWrapper = CreateSupportedQuickUnlockWrapper()
        };

    private static Mock<IPlatformQuickUnlock> PlatformFor(PlatformQuickUnlockWrapperV2 wrapper)
    {
        var platform = new Mock<IPlatformQuickUnlock>();
        platform.SetupGet(value => value.ProviderId).Returns(wrapper.Provider);
        return platform;
    }

    private static PlatformQuickUnlockWrapperV2 CreateSupportedQuickUnlockWrapper() => new()
    {
        Provider = PlatformQuickUnlockContract.WindowsHelloTpmProvider,
        ProviderVersion = PlatformQuickUnlockContract.WindowsHelloTpmProviderVersion,
        AuthenticationPolicy = PlatformQuickUnlockContract.UserVerificationRequired,
        KeyReference = "TOTP_TPM_SYNTHETIC_SESSION",
        WrappedKey = new PlatformWrappedKeyV2
        {
            Algorithm = PlatformQuickUnlockContract.RsaOaepSha256Algorithm,
            Ciphertext = new byte[256]
        }
    };

    private static void AssertSessionError<T>(
        Result<T> result,
        AuthorizationEnvelopeSessionErrorCode expectedCode)
    {
        Assert.False(result.IsSuccess);
        Assert.Equal(
            expectedCode,
            Assert.Single(result.Errors.OfType<AuthorizationEnvelopeSessionError>()).Code);
    }
}
