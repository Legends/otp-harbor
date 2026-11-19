using System.Security.Cryptography;
using FluentResults;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TOTP.Core.Enums;
using TOTP.Core.Models;
using TOTP.Core.Security;
using TOTP.Core.Security.Interfaces;
using TOTP.Core.Security.Models;
using TOTP.Infrastructure.Security;

namespace TOTP.Tests.Security;

public sealed partial class PortableSensitiveAuthorizationTests
{
    [Theory]
    [InlineData(PreferredUnlockMethod.PlatformQuickUnlock, true)]
    [InlineData(PreferredUnlockMethod.PlatformDeviceCredential, true)]
    [InlineData(PreferredUnlockMethod.PlatformQuickUnlock, false)]
    [InlineData(PreferredUnlockMethod.PlatformDeviceCredential, false)]
    public async Task ChangeToPassword_CurrentPlatformVerificationPrecedesPreferenceSaveAndPreservesRecovery(
        PreferredUnlockMethod previous, bool saveSucceeds)
    {
        using var context = new Context(previous);
        var verified = false;
        context.Session.Setup(value => value.TryUnlockWithPlatformAsync(It.IsAny<CancellationToken>()))
            .Callback(() => verified = true).ReturnsAsync(Result.Ok(AuthorizationResult.Success));
        context.Preferences.Setup(value => value.SaveAsync())
            .Callback(() => Assert.True(verified))
            .ReturnsAsync(saveSucceeds ? Result.Ok() : Result.Fail("Synthetic preference failure"));

        Assert.Equal(saveSucceeds ? AuthorizationResult.Success : AuthorizationResult.Failed,
            await context.Create().ConfigureUnlockMethodWithPlatformAsync(PreferredUnlockMethod.Password, previous,
                TestContext.Current.CancellationToken));

        Assert.Equal(saveSucceeds ? PreferredUnlockMethod.Password : previous, context.Settings.PreferredUnlockMethod);
        context.Enrollment.VerifyNoOtherCalls();
        context.PasswordLifecycle.VerifyNoOtherCalls();
        context.Session.Verify(value => value.TryUnlockWithPasswordAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.All(context.Security.Copies, copy => Assert.All(copy, value => Assert.Equal(0, value)));
    }

    [Theory]
    [InlineData(PreferredUnlockMethod.PlatformQuickUnlock)]
    [InlineData(PreferredUnlockMethod.PlatformDeviceCredential)]
    public async Task DisableAppLock_CurrentPlatformMethodAuthenticatesAndPreservesPasswordRecovery(PreferredUnlockMethod method)
    {
        using var context = new Context(method);
        ReadOnlyMemory<byte> authorizedKey = default;
        context.Unattended.Setup(value => value.EnableWithAuthenticatedKeyAsync(It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
            .Callback((ReadOnlyMemory<byte> key, CancellationToken _) =>
            {
                Assert.Equal(Context.Key, key.ToArray());
                authorizedKey = key;
                context.SessionState = new(true, true, false, true);
            }).ReturnsAsync(Result.Ok());
        var sut = context.Create();

        Assert.Equal(AuthorizationResult.Success, await sut.SetAppLockEnabledWithPlatformAsync(false, method, TestContext.Current.CancellationToken));

        Assert.False(context.Settings.AppLockEnabled);
        context.Session.Verify(value => value.TryUnlockWithPlatformAsync(It.IsAny<CancellationToken>()), Times.Once);
        context.Session.Verify(value => value.TryUnlockWithPasswordAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        context.Unattended.Verify(value => value.EnableAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        context.PasswordLifecycle.VerifyNoOtherCalls();
        Assert.All(authorizedKey.ToArray(), value => Assert.Equal(0, value));
        Assert.All(context.Security.Copies, copy => Assert.All(copy, value => Assert.Equal(0, value)));
    }

    [Theory]
    [InlineData(AuthorizationResult.Cancelled)]
    [InlineData(AuthorizationResult.Failed)]
    [InlineData(AuthorizationResult.PasswordRequired)]
    public async Task DisableAppLock_FailedAuthenticationNeverEnrollsOrSaves(AuthorizationResult outcome)
    {
        using var context = new Context();
        context.Session.Setup(value => value.TryUnlockWithPlatformAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok(outcome));

        Assert.Equal(outcome, await context.Create().SetAppLockEnabledWithPlatformAsync(false, PreferredUnlockMethod.PlatformQuickUnlock, TestContext.Current.CancellationToken));

        Assert.True(context.Settings.AppLockEnabled);
        context.Unattended.Verify(value => value.EnableWithAuthenticatedKeyAsync(It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()), Times.Never);
        context.Preferences.Verify(value => value.SaveAsync(), Times.Never);
    }

    [Fact]
    public async Task PasswordPolicy_DoesNotAllowAnUnenrolledPlatformMethodToAuthorizeSettings()
    {
        using var context = new Context(PreferredUnlockMethod.Password);
        Assert.Equal(AuthorizationResult.PasswordRequired,
            await context.Create().AuthorizeSensitiveOperationAsync(PreferredUnlockMethod.PlatformQuickUnlock, TestContext.Current.CancellationToken));
        context.Session.Verify(value => value.TryUnlockWithPlatformAsync(It.IsAny<CancellationToken>()), Times.Never);
        context.Platform.Verify(value => value.RegisterAsync(It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(PreferredUnlockMethod.PlatformQuickUnlock)]
    [InlineData(PreferredUnlockMethod.PlatformDeviceCredential)]
    public async Task DisabledAppLock_UsesFreshTemporaryChallengeAndNeverSilentUnattendedUnlock(PreferredUnlockMethod method)
    {
        using var context = new Context(method, appLockEnabled: false);
        ReadOnlyMemory<byte> challenge = default;
        context.Platform.Setup(value => value.RegisterAsync(It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
            .Callback((ReadOnlyMemory<byte> value, CancellationToken _) =>
            {
                Assert.NotEqual(Context.Key, value.ToArray());
                challenge = value;
            }).ReturnsAsync(() => Result.Ok(Wrapper(method)));

        Assert.Equal(AuthorizationResult.Success, await context.Create().AuthorizeSensitiveOperationAsync(method, TestContext.Current.CancellationToken));

        Assert.False(context.Settings.AppLockEnabled);
        Assert.All(challenge.ToArray(), value => Assert.Equal(0, value));
        context.Platform.Verify(value => value.RemoveAsync(It.IsAny<PlatformQuickUnlockWrapperV2>(), CancellationToken.None), Times.Once);
        context.Session.Verify(value => value.TryUnlockWithPlatformAsync(It.IsAny<CancellationToken>()), Times.Never);
        context.Preferences.Verify(value => value.SaveAsync(), Times.Never);
        context.Enrollment.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task DisabledAppLock_CancelledPlatformPromptDoesNotAuthorize()
    {
        using var context = new Context(appLockEnabled: false);
        context.Platform.Setup(value => value.RegisterAsync(It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Fail<PlatformQuickUnlockWrapperV2>(new PlatformQuickUnlockError(PlatformQuickUnlockErrorCode.Cancelled, "Synthetic cancellation")));
        Assert.Equal(AuthorizationResult.Cancelled,
            await context.Create().AuthorizeSensitiveOperationAsync(PreferredUnlockMethod.PlatformQuickUnlock, TestContext.Current.CancellationToken));
        context.Preferences.Verify(value => value.SaveAsync(), Times.Never);
    }

    [Fact]
    public async Task DisabledAppLock_TemporaryRegistrationCleanupFailureDoesNotAuthorize()
    {
        using var context = new Context(appLockEnabled: false);
        var wrapper = Wrapper(PreferredUnlockMethod.PlatformQuickUnlock);
        context.Platform.Setup(value => value.RegisterAsync(It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok(wrapper));
        context.Platform.Setup(value => value.RemoveAsync(wrapper, CancellationToken.None))
            .ReturnsAsync(Result.Fail("Synthetic cleanup failure"));

        Assert.Equal(AuthorizationResult.Failed, await context.Create().SetAppLockEnabledWithPlatformAsync(true,
            PreferredUnlockMethod.PlatformQuickUnlock, TestContext.Current.CancellationToken));

        Assert.False(context.Settings.AppLockEnabled);
        context.Platform.Verify(value => value.RemoveAsync(wrapper, CancellationToken.None), Times.Exactly(2));
        context.Preferences.Verify(value => value.SaveAsync(), Times.Never);
        context.Unattended.VerifyNoOtherCalls();
        Assert.All(context.Security.Copies, copy => Assert.All(copy, value => Assert.Equal(0, value)));
    }

    [Fact]
    public async Task DisabledAppLock_UnavailablePlatformMethodDoesNotPrompt()
    {
        using var context = new Context(appLockEnabled: false);
        context.Platform.Setup(value => value.GetAvailabilityAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(PlatformQuickUnlockAvailability.NotSupported);
        Assert.Equal(AuthorizationResult.NotAvailable,
            await context.Create().AuthorizeSensitiveOperationAsync(PreferredUnlockMethod.PlatformQuickUnlock, TestContext.Current.CancellationToken));
        context.Platform.Verify(value => value.RegisterAsync(It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task LockDuringPrompt_CancelsAuthorizationBeforeAnyWrapperWrite()
    {
        using var context = new Context();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        context.Session.Setup(value => value.TryUnlockWithPlatformAsync(It.IsAny<CancellationToken>()))
            .Returns(async (CancellationToken token) =>
            {
                entered.TrySetResult();
                await Task.Delay(Timeout.Infinite, token);
                return Result.Ok(AuthorizationResult.Success);
            });
        var sut = context.Create();
        var operation = sut.SetAppLockEnabledWithPlatformAsync(false, PreferredUnlockMethod.PlatformQuickUnlock, TestContext.Current.CancellationToken);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        sut.Lock();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
        context.Unattended.Verify(value => value.EnableWithAuthenticatedKeyAsync(It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()), Times.Never);
        context.Preferences.Verify(value => value.SaveAsync(), Times.Never);
    }

    [Fact]
    public async Task KeyChangedDuringPrompt_DoesNotApplyAuthorizationToAnotherVault()
    {
        using var context = new Context();
        context.Session.Setup(value => value.TryUnlockWithPlatformAsync(It.IsAny<CancellationToken>()))
            .Callback(() => context.Security.SetDek(new byte[32])).ReturnsAsync(Result.Ok(AuthorizationResult.Success));
        Assert.Equal(AuthorizationResult.Cancelled,
            await context.Create().SetAppLockEnabledWithPlatformAsync(false, PreferredUnlockMethod.PlatformQuickUnlock, TestContext.Current.CancellationToken));
        Assert.False(context.State.IsUnlocked);
        Assert.False(context.Security.IsUnlocked);
        context.Unattended.Verify(value => value.EnableWithAuthenticatedKeyAsync(It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(PreferredUnlockMethod.PlatformQuickUnlock)]
    [InlineData(PreferredUnlockMethod.PlatformDeviceCredential)]
    public async Task InitialSetupOffer_IsOptionalAndCanOnlyBeCompletedOnce(PreferredUnlockMethod? choice)
    {
        using var context = new Context();
        context.SessionState = new(true, true, false);
        context.Validation.Setup(value => value.IsValidNewWithConfirmation(It.IsAny<string>(), It.IsAny<string>())).Returns(true);
        context.PasswordLifecycle.Setup(value => value.ConfigureAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Result.Ok(SensitiveBuffer.CopyFrom(Context.Key)));
        var sut = context.Create();
        Assert.Equal(AuthorizationResult.Success, await sut.ConfigurePasswordAsync("synthetic recovery", "synthetic recovery"));

        Assert.Equal(AuthorizationResult.Success, await sut.CompleteInitialQuickUnlockSetupAsync(choice, TestContext.Current.CancellationToken));
        Assert.Equal(AuthorizationResult.PasswordRequired, await sut.CompleteInitialQuickUnlockSetupAsync(PreferredUnlockMethod.PlatformQuickUnlock, TestContext.Current.CancellationToken));
        context.Enrollment.Verify(value => value.EnableWithAuthenticatedKeyAsync(It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<PreferredUnlockMethod>(), It.IsAny<CancellationToken>()),
            choice is null ? Times.Never() : Times.Once());
        context.Enrollment.Verify(value => value.EnableAsync(It.IsAny<string>(), It.IsAny<PreferredUnlockMethod>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task InitialSetupOffer_CannotBeUsedWithoutFreshPasswordCreation()
    {
        using var context = new Context();
        Assert.Equal(AuthorizationResult.PasswordRequired,
            await context.Create().CompleteInitialQuickUnlockSetupAsync(PreferredUnlockMethod.PlatformQuickUnlock, TestContext.Current.CancellationToken));
        context.Enrollment.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task LockDuringPrompt_LateSuccessfulCompletionCannotReviveSession()
    {
        using var context = new Context();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var complete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        context.Session.Setup(value => value.TryUnlockWithPlatformAsync(It.IsAny<CancellationToken>()))
            .Returns(async (CancellationToken _) =>
            {
                entered.SetResult();
                await complete.Task.WaitAsync(TestContext.Current.CancellationToken);
                context.Security.SetDek(Context.Key);
                return Result.Ok(AuthorizationResult.Success);
            });
        var sut = context.Create();
        var operation = sut.SetAppLockEnabledWithPlatformAsync(false, PreferredUnlockMethod.PlatformQuickUnlock,
            TestContext.Current.CancellationToken);
        await entered.Task.WaitAsync(TestContext.Current.CancellationToken);
        sut.Lock();
        complete.SetResult();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);

        Assert.False(context.Security.IsUnlocked);
        Assert.False(context.State.IsUnlocked);
        context.Unattended.Verify(value => value.EnableWithAuthenticatedKeyAsync(It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DisableAppLock_PreferenceFailureRemovesUnattendedWrapperAndKeepsRecoveryGate()
    {
        using var context = new Context();
        context.Unattended.Setup(value => value.EnableWithAuthenticatedKeyAsync(It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
            .Callback(() => context.SessionState = new(true, true, false, true)).ReturnsAsync(Result.Ok());
        context.Preferences.Setup(value => value.SaveAsync()).ReturnsAsync(Result.Fail("Synthetic settings failure"));
        context.Unattended.Setup(value => value.DisableAsync(It.IsAny<CancellationToken>()))
            .Callback(() => context.SessionState = new(true, true, false)).ReturnsAsync(Result.Ok());

        Assert.Equal(AuthorizationResult.Failed, await context.Create().SetAppLockEnabledWithPlatformAsync(false,
            PreferredUnlockMethod.PlatformQuickUnlock, TestContext.Current.CancellationToken));

        Assert.True(context.Settings.AppLockEnabled);
        Assert.Equal(PreferredUnlockMethod.Password, context.State.PreferredUnlockMethod);
        context.Unattended.Verify(value => value.DisableAsync(It.IsAny<CancellationToken>()), Times.Once);
        context.PasswordLifecycle.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ChangeUnlockMethod_AuthorizesWithCurrentMethodAndEnrollsRequestedMethod()
    {
        using var context = new Context();
        Assert.Equal(AuthorizationResult.Success,
            await context.Create().ConfigureUnlockMethodWithPlatformAsync(PreferredUnlockMethod.PlatformDeviceCredential,
                PreferredUnlockMethod.PlatformQuickUnlock, TestContext.Current.CancellationToken));
        Assert.Equal(PreferredUnlockMethod.PlatformDeviceCredential, context.State.PreferredUnlockMethod);
        context.Session.Verify(value => value.TryUnlockWithPlatformAsync(It.IsAny<CancellationToken>()), Times.Once);
        context.Enrollment.Verify(value => value.EnableWithAuthenticatedKeyAsync(It.IsAny<ReadOnlyMemory<byte>>(),
            PreferredUnlockMethod.PlatformDeviceCredential, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(PreferredUnlockMethod.PlatformQuickUnlock, AuthorizationResult.Cancelled)]
    [InlineData(PreferredUnlockMethod.PlatformDeviceCredential, AuthorizationResult.Cancelled)]
    [InlineData(PreferredUnlockMethod.PlatformQuickUnlock, AuthorizationResult.Failed)]
    [InlineData(PreferredUnlockMethod.PlatformDeviceCredential, AuthorizationResult.Failed)]
    public async Task ChangeUnlockMethod_FailedCurrentAuthenticationNeverEnrollsReplacement(
        PreferredUnlockMethod previous, AuthorizationResult outcome)
    {
        using var context = new Context(previous);
        context.Session.Setup(value => value.TryUnlockWithPlatformAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok(outcome));
        var requested = previous == PreferredUnlockMethod.PlatformQuickUnlock
            ? PreferredUnlockMethod.PlatformDeviceCredential : PreferredUnlockMethod.PlatformQuickUnlock;

        Assert.Equal(outcome, await context.Create().ConfigureUnlockMethodWithPlatformAsync(
            requested, previous, TestContext.Current.CancellationToken));

        Assert.Equal(previous, context.State.PreferredUnlockMethod);
        Assert.Equal(previous, context.Settings.PreferredUnlockMethod);
        context.Enrollment.Verify(value => value.EnableWithAuthenticatedKeyAsync(It.IsAny<ReadOnlyMemory<byte>>(),
            It.IsAny<PreferredUnlockMethod>(), It.IsAny<CancellationToken>()), Times.Never);
        context.Preferences.Verify(value => value.SaveAsync(), Times.Never);
        Assert.All(context.Security.Copies, key => Assert.All(key, value => Assert.Equal(0, value)));
    }

    [Fact]
    public async Task ReenableAppLock_UsesChosenDeviceAuthenticationWithoutChangingPreferredMethod()
    {
        using var context = new Context(appLockEnabled: false);
        context.Platform.Setup(value => value.RegisterAsync(It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Result.Ok(Wrapper(PreferredUnlockMethod.PlatformQuickUnlock)));
        context.Unattended.Setup(value => value.DisableAsync(It.IsAny<CancellationToken>()))
            .Callback(() => context.SessionState = new(true, true, false)).ReturnsAsync(Result.Ok());

        Assert.Equal(AuthorizationResult.Success, await context.Create().SetAppLockEnabledWithPlatformAsync(true,
            PreferredUnlockMethod.PlatformQuickUnlock, TestContext.Current.CancellationToken));

        Assert.True(context.Settings.AppLockEnabled);
        Assert.Equal(PreferredUnlockMethod.PlatformQuickUnlock, context.State.PreferredUnlockMethod);
        context.Session.Verify(value => value.TryUnlockWithPlatformAsync(It.IsAny<CancellationToken>()), Times.Never);
        context.Unattended.Verify(value => value.DisableAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task InitialSetupOffer_LockInvalidatesOptionalEnrollment()
    {
        using var context = new Context();
        context.SessionState = new(true, true, false);
        context.Validation.Setup(value => value.IsValidNewWithConfirmation(It.IsAny<string>(), It.IsAny<string>())).Returns(true);
        context.PasswordLifecycle.Setup(value => value.ConfigureAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Result.Ok(SensitiveBuffer.CopyFrom(Context.Key)));
        var sut = context.Create();
        Assert.Equal(AuthorizationResult.Success, await sut.ConfigurePasswordAsync("synthetic recovery", "synthetic recovery"));
        sut.Lock();

        Assert.Equal(AuthorizationResult.PasswordRequired, await sut.CompleteInitialQuickUnlockSetupAsync(
            PreferredUnlockMethod.PlatformQuickUnlock, TestContext.Current.CancellationToken));
        context.Enrollment.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(PlatformQuickUnlockContract.WindowsHelloTpmProvider)]
    [InlineData(PlatformQuickUnlockContract.MacOSKeychainProvider)]
    public async Task DisabledDesktopAppLock_UsesFreshPresenceChallengeAndClearsIt(string provider)
    {
        using var context = new Context(appLockEnabled: false);
        context.Platform.SetupGet(value => value.ProviderId).Returns(provider);
        ReadOnlyMemory<byte> challenge = default;
        var wrapper = DesktopProofWrapper(provider);
        context.Platform.Setup(value => value.RegisterAsync(It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
            .Callback((ReadOnlyMemory<byte> value, CancellationToken _) => challenge = value).ReturnsAsync(Result.Ok(wrapper));
        context.Platform.Setup(value => value.TryUnlockAsync(wrapper, It.IsAny<CancellationToken>()))
            .Returns(() => Task.FromResult(Result.Ok(PlatformQuickUnlockAttempt.Successful(SensitiveBuffer.CopyFrom(challenge.Span)))));

        Assert.Equal(AuthorizationResult.Success, await context.Create().AuthorizeSensitiveOperationAsync(
            PreferredUnlockMethod.PlatformQuickUnlock, TestContext.Current.CancellationToken));

        context.Platform.Verify(value => value.TryUnlockAsync(wrapper, It.IsAny<CancellationToken>()),
            provider == PlatformQuickUnlockContract.MacOSKeychainProvider ? Times.Once() : Times.Never());
        context.Platform.Verify(value => value.RemoveAsync(wrapper, CancellationToken.None), Times.Once);
        Assert.All(challenge.ToArray(), value => Assert.Equal(0, value));
        context.Session.Verify(value => value.TryUnlockWithPlatformAsync(It.IsAny<CancellationToken>()), Times.Never);
        context.Preferences.Verify(value => value.SaveAsync(), Times.Never);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DisabledMacAppLock_CancellationOrWrongChallengeDoesNotAuthorize(bool cancelled)
    {
        using var context = new Context(appLockEnabled: false);
        var wrapper = DesktopProofWrapper(PlatformQuickUnlockContract.MacOSKeychainProvider);
        context.Platform.SetupGet(value => value.ProviderId).Returns(wrapper.Provider);
        context.Platform.Setup(value => value.RegisterAsync(It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>())).ReturnsAsync(Result.Ok(wrapper));
        context.Platform.Setup(value => value.TryUnlockAsync(wrapper, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok(cancelled ? PlatformQuickUnlockAttempt.WithoutKey(PlatformQuickUnlockStatus.Cancelled)
                : PlatformQuickUnlockAttempt.Successful(SensitiveBuffer.CopyFrom(new byte[32]))));
        Assert.Equal(cancelled ? AuthorizationResult.Cancelled : AuthorizationResult.Failed,
            await context.Create().AuthorizeSensitiveOperationAsync(PreferredUnlockMethod.PlatformQuickUnlock,
                TestContext.Current.CancellationToken));
        context.Platform.Verify(value => value.RemoveAsync(wrapper, CancellationToken.None), Times.Once);
        context.Preferences.Verify(value => value.SaveAsync(), Times.Never);
    }

    private static PlatformQuickUnlockWrapperV2 DesktopProofWrapper(string provider) => new()
    {
        Provider = provider, ProviderVersion = 1,
        AuthenticationPolicy = PlatformQuickUnlockContract.UserVerificationRequired,
        KeyReference = "SYNTHETIC_DESKTOP_PROOF",
        WrappedKey = new PlatformWrappedKeyV2
        {
            Algorithm = provider == PlatformQuickUnlockContract.MacOSKeychainProvider
                ? PlatformQuickUnlockContract.KeychainItemReferenceAlgorithm : PlatformQuickUnlockContract.RsaOaepSha256Algorithm,
            Ciphertext = new byte[provider == PlatformQuickUnlockContract.MacOSKeychainProvider ? 32 : 256]
        }
    };

    private static PlatformQuickUnlockWrapperV2 Wrapper(PreferredUnlockMethod method) => new()
    {
        Provider = method == PreferredUnlockMethod.PlatformDeviceCredential
            ? PlatformQuickUnlockContract.AndroidKeystoreDeviceCredentialProvider
            : PlatformQuickUnlockContract.AndroidKeystoreBiometricProvider,
        ProviderVersion = 1,
        AuthenticationPolicy = PlatformQuickUnlockContract.UserVerificationRequired,
        KeyReference = "synthetic-authentication-challenge",
        WrappedKey = new() { Algorithm = PlatformQuickUnlockContract.AndroidAes256GcmAlgorithm, Nonce = new byte[12], Ciphertext = new byte[48] }
    };

    private sealed class Context : IDisposable
    {
        public static byte[] Key => Enumerable.Range(1, 32).Select(value => (byte)value).ToArray();
        public AppSettings Settings { get; } = new();
        public AuthorizationState State { get; } = new();
        public MemorySecurityContext Security { get; } = new();
        public Mock<ISettingsService> Preferences { get; } = new();
        public Mock<IAuthorizationEnvelopeSession> Session { get; } = new();
        public Mock<IAuthorizationEnvelopePasswordLifecycle> PasswordLifecycle { get; } = new();
        public Mock<IPlatformQuickUnlockEnrollment> Enrollment { get; } = new();
        public Mock<IPlatformUnattendedUnlockEnrollment> Unattended { get; } = new();
        public Mock<IPlatformQuickUnlock> Platform { get; } = new();
        public Mock<IPasswordValidationService> Validation { get; } = new();
        public AuthorizationEnvelopeSessionState SessionState { get; set; }

        public Context(PreferredUnlockMethod method = PreferredUnlockMethod.PlatformQuickUnlock, bool appLockEnabled = true)
        {
            Settings.AppLockEnabled = appLockEnabled;
            Settings.PreferredUnlockMethod = method;
            State.SetConfiguration(true, appLockEnabled ? method : PreferredUnlockMethod.Password);
            State.Unlock();
            SessionState = new(true, true, appLockEnabled && method != PreferredUnlockMethod.Password, !appLockEnabled, method);
            Preferences.SetupGet(value => value.Current).Returns(Settings);
            Preferences.Setup(value => value.SaveAsync()).ReturnsAsync(Result.Ok());
            Session.SetupGet(value => value.State).Returns(() => SessionState);
            Session.Setup(value => value.InitializeAsync(It.IsAny<CancellationToken>())).ReturnsAsync(() => Result.Ok(SessionState));
            Session.Setup(value => value.TryUnlockWithPlatformAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Result.Ok(AuthorizationResult.Success));
            Platform.SetupGet(value => value.UnlockMethod).Returns(method);
            Platform.SetupGet(value => value.ProviderId).Returns(Wrapper(method).Provider);
            Platform.Setup(value => value.GetAvailabilityAsync(It.IsAny<CancellationToken>())).ReturnsAsync(PlatformQuickUnlockAvailability.Available);
            Platform.Setup(value => value.RemoveAsync(It.IsAny<PlatformQuickUnlockWrapperV2>(), It.IsAny<CancellationToken>())).ReturnsAsync(Result.Ok());
            Enrollment.Setup(value => value.EnableWithAuthenticatedKeyAsync(It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<PreferredUnlockMethod>(), It.IsAny<CancellationToken>()))
                .Callback((ReadOnlyMemory<byte> _, PreferredUnlockMethod selected, CancellationToken _) => SessionState = new(true, true, true, false, selected))
                .ReturnsAsync(Result.Ok());
        }

        public PortableAuthorizationService Create(IPlatformQuickUnlockEnrollment? enrollment = null,
            IPlatformQuickUnlock? platform = null) => new(Preferences.Object, Session.Object, PasswordLifecycle.Object,
            enrollment ?? Enrollment.Object, platform ?? Platform.Object, Validation.Object, Security, State,
            NullLogger<PortableAuthorizationService>.Instance, Unattended.Object);

        public void Dispose() => Security.Lock();
    }

    private sealed class MemorySecurityContext : ISecurityContext
    {
        private byte[]? _key = Context.Key;
        public List<byte[]> Copies { get; } = [];
        public bool IsUnlocked => _key is not null;
        public byte[] GetDekCopy()
        {
            var copy = _key?.ToArray() ?? throw new InvalidOperationException("Synthetic locked context");
            Copies.Add(copy);
            return copy;
        }
        public void SetDek(byte[] dek) { Lock(); _key = dek.ToArray(); }
        public void Lock() { if (_key is not null) CryptographicOperations.ZeroMemory(_key); _key = null; }
    }
}
