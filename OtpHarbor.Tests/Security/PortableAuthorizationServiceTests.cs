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

public sealed class PortableAuthorizationServiceTests
{
    [Fact]
    public async Task InitializeAsync_WhenQuickUnlockPreferenceHasNoWrapper_ProjectsPasswordGate()
    {
        var dependencies = new Dependencies
        {
            SessionState = new AuthorizationEnvelopeSessionState(true, true, false)
        };
        dependencies.Settings.PreferredUnlockMethod = PreferredUnlockMethod.PlatformQuickUnlock;
        var sut = dependencies.CreateSut();

        await sut.InitializeAsync();

        Assert.True(sut.State.IsConfigured);
        Assert.Equal(PreferredUnlockMethod.Password, sut.State.PreferredUnlockMethod);
        Assert.Equal(AuthorizationGateKind.Password, sut.State.ConfiguredGate);
    }

    [Fact]
    public async Task InitializeAsync_WhenReviewedQuickUnlockExists_ProjectsHelloGate()
    {
        var dependencies = new Dependencies
        {
            SessionState = new AuthorizationEnvelopeSessionState(true, true, true)
        };
        dependencies.Settings.PreferredUnlockMethod = PreferredUnlockMethod.PlatformQuickUnlock;
        var sut = dependencies.CreateSut();

        await sut.InitializeAsync();

        Assert.True(sut.State.IsConfigured);
        Assert.Equal(PreferredUnlockMethod.PlatformQuickUnlock, sut.State.PreferredUnlockMethod);
        Assert.Equal(AuthorizationGateKind.Hello, sut.State.ConfiguredGate);
    }

    [Fact]
    public async Task InitializeAsync_WhenSessionFails_LeavesAuthorizationUnconfigured()
    {
        var dependencies = new Dependencies();
        dependencies.Session.Setup(value => value.InitializeAsync(CancellationToken.None))
            .ReturnsAsync(Result.Fail<AuthorizationEnvelopeSessionState>(new AuthorizationEnvelopeSessionError(
                AuthorizationEnvelopeSessionErrorCode.LoadFailed,
                "synthetic load failure")));
        var sut = dependencies.CreateSut();

        await sut.InitializeAsync();

        Assert.False(sut.State.IsConfigured);
        Assert.Equal(AuthorizationGateKind.None, sut.State.ConfiguredGate);
    }

    [Fact]
    public async Task TryUnlockOnStartupAsync_WithPasswordPreference_RequiresPasswordWithoutPlatformPrompt()
    {
        var dependencies = new Dependencies
        {
            SessionState = new AuthorizationEnvelopeSessionState(true, true, true)
        };
        var sut = dependencies.CreateSut();
        await sut.InitializeAsync();

        var result = await sut.TryUnlockOnStartupAsync(TestContext.Current.CancellationToken);

        Assert.Equal(AuthorizationResult.PasswordRequired, result);
        dependencies.Session.Verify(value => value.TryUnlockWithPlatformAsync(
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task TryUnlockOnStartupAsync_WithQuickUnlockPreference_DelegatesAndUnlocksState()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var dependencies = new Dependencies
        {
            SessionState = new AuthorizationEnvelopeSessionState(true, true, true)
        };
        dependencies.Settings.PreferredUnlockMethod = PreferredUnlockMethod.PlatformQuickUnlock;
        dependencies.Session.Setup(value => value.TryUnlockWithPlatformAsync(cancellationToken))
            .ReturnsAsync(Result.Ok(AuthorizationResult.Success));
        var sut = dependencies.CreateSut();
        await sut.InitializeAsync();

        var result = await sut.TryUnlockOnStartupAsync(cancellationToken);

        Assert.Equal(AuthorizationResult.Success, result);
        Assert.True(sut.State.IsUnlocked);
    }

    [Fact]
    public async Task TryUnlockOnStartupAsync_WithDisabledAppLock_UsesUnattendedWrapper()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var dependencies = new Dependencies
        {
            SessionState = new AuthorizationEnvelopeSessionState(
                true,
                true,
                true,
                HasUnattendedUnlock: true)
        };
        dependencies.Settings.AppLockEnabled = false;
        dependencies.Settings.PreferredUnlockMethod = PreferredUnlockMethod.PlatformQuickUnlock;
        dependencies.Session.Setup(value => value.TryUnlockWithPlatformAsync(cancellationToken))
            .ReturnsAsync(Result.Ok(AuthorizationResult.Success));
        var sut = dependencies.CreateSut();
        await sut.InitializeAsync();

        var result = await sut.TryUnlockOnStartupAsync(cancellationToken);

        Assert.Equal(AuthorizationResult.Success, result);
        Assert.True(sut.State.IsUnlocked);
        dependencies.UnattendedEnrollment.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task TryUnlockOnStartupAsync_WhenUnattendedKeyIsMissing_FailsClosedToAppLock()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var dependencies = new Dependencies
        {
            SessionState = new AuthorizationEnvelopeSessionState(
                true,
                true,
                true,
                HasUnattendedUnlock: true)
        };
        dependencies.Settings.AppLockEnabled = false;
        dependencies.Settings.PreferredUnlockMethod = PreferredUnlockMethod.PlatformQuickUnlock;
        dependencies.Session.Setup(value => value.TryUnlockWithPlatformAsync(cancellationToken))
            .ReturnsAsync(Result.Ok(AuthorizationResult.PasswordRequired));
        dependencies.UnattendedEnrollment.Setup(value => value.DisableAsync(CancellationToken.None))
            .ReturnsAsync(Result.Ok());
        var sut = dependencies.CreateSut();
        await sut.InitializeAsync();

        var result = await sut.TryUnlockOnStartupAsync(cancellationToken);

        Assert.Equal(AuthorizationResult.PasswordRequired, result);
        Assert.True(dependencies.Settings.AppLockEnabled);
        Assert.Equal(
            PreferredUnlockMethod.PlatformQuickUnlock,
            dependencies.Settings.PreferredUnlockMethod);
        dependencies.SettingsService.Verify(value => value.SaveAsync(), Times.Once);
    }

    [Fact]
    public async Task SetAppLockEnabledAsync_FromDisabled_RestoresPreviousUnlockMethod()
    {
        var operations = new List<string>();
        var dependencies = new Dependencies
        {
            SessionState = new AuthorizationEnvelopeSessionState(
                true,
                true,
                true,
                HasUnattendedUnlock: true)
        };
        dependencies.Settings.AppLockEnabled = false;
        dependencies.Settings.PreferredUnlockMethod = PreferredUnlockMethod.PlatformQuickUnlock;
        dependencies.UnattendedEnrollment.Setup(value => value.DisableAsync(CancellationToken.None))
            .Callback(() =>
            {
                operations.Add("disable-wrapper");
                dependencies.SessionState = new AuthorizationEnvelopeSessionState(true, true, true);
            })
            .ReturnsAsync(Result.Ok());
        dependencies.SettingsService.Setup(value => value.SaveAsync())
            .Callback(() => operations.Add("save-preference"))
            .ReturnsAsync(Result.Ok());
        dependencies.Enrollment.Setup(value => value.EnableAsync(
                "recovery-password",
                CancellationToken.None))
            .Callback(() => dependencies.SessionState =
                new AuthorizationEnvelopeSessionState(true, true, true))
            .ReturnsAsync(Result.Ok());
        var sut = dependencies.CreateSut();
        await sut.InitializeAsync();

        var result = await sut.SetAppLockEnabledAsync(true, "recovery-password");

        Assert.Equal(AuthorizationResult.Success, result);
        Assert.Equal(["disable-wrapper", "save-preference", "save-preference"], operations);
        Assert.True(dependencies.Settings.AppLockEnabled);
        Assert.Equal(
            PreferredUnlockMethod.PlatformQuickUnlock,
            dependencies.Settings.PreferredUnlockMethod);
        Assert.Equal(AuthorizationGateKind.Hello, sut.State.ConfiguredGate);
    }

    [Fact]
    public async Task SetAppLockEnabledAsync_WhenWrapperRemovalFails_LeavesDisabledPreferenceRetryable()
    {
        var dependencies = new Dependencies
        {
            SessionState = new AuthorizationEnvelopeSessionState(
                true,
                true,
                true,
                HasUnattendedUnlock: true)
        };
        dependencies.Settings.AppLockEnabled = false;
        dependencies.Settings.PreferredUnlockMethod = PreferredUnlockMethod.PlatformQuickUnlock;
        dependencies.UnattendedEnrollment.Setup(value => value.DisableAsync(CancellationToken.None))
            .ReturnsAsync(Result.Fail("synthetic wrapper removal failure"));
        var sut = dependencies.CreateSut();
        await sut.InitializeAsync();

        var result = await sut.SetAppLockEnabledAsync(true, string.Empty);

        Assert.Equal(AuthorizationResult.Failed, result);
        Assert.False(dependencies.Settings.AppLockEnabled);
        Assert.Equal(
            PreferredUnlockMethod.PlatformQuickUnlock,
            dependencies.Settings.PreferredUnlockMethod);
        dependencies.SettingsService.Verify(value => value.SaveAsync(), Times.Never);
    }

    [Fact]
    public async Task TryUnlockWithHelloAsync_WhenPlatformRequiresRecovery_ProjectsPasswordGate()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var dependencies = new Dependencies
        {
            SessionState = new AuthorizationEnvelopeSessionState(true, true, true)
        };
        dependencies.Settings.PreferredUnlockMethod = PreferredUnlockMethod.PlatformQuickUnlock;
        dependencies.Session.Setup(value => value.TryUnlockWithPlatformAsync(cancellationToken))
            .ReturnsAsync(Result.Ok(AuthorizationResult.PasswordRequired));
        var sut = dependencies.CreateSut();
        await sut.InitializeAsync();

        var result = await sut.TryUnlockWithHelloAsync(cancellationToken);

        Assert.Equal(AuthorizationResult.PasswordRequired, result);
        Assert.Equal(AuthorizationGateKind.Password, sut.State.ConfiguredGate);
        Assert.Equal(PreferredUnlockMethod.PlatformQuickUnlock, dependencies.Settings.PreferredUnlockMethod);
        dependencies.SettingsService.Verify(value => value.SaveAsync(), Times.Never);
    }

    [Fact]
    public async Task TryUnlockWithPasswordAsync_WhenSessionSucceeds_UnlocksState()
    {
        var dependencies = new Dependencies();
        dependencies.Session.Setup(value => value.TryUnlockWithPasswordAsync(
                "recovery-password",
                CancellationToken.None))
            .ReturnsAsync(Result.Ok(AuthorizationResult.Success));
        var sut = dependencies.CreateSut();

        var result = await sut.TryUnlockWithPasswordAsync("recovery-password");

        Assert.Equal(AuthorizationResult.Success, result);
        Assert.True(sut.State.IsUnlocked);
    }

    [Fact]
    public async Task ConfigurePasswordAsync_WithInvalidConfirmation_DoesNotCreateEnvelope()
    {
        var dependencies = new Dependencies(validPasswordConfirmation: false);
        var sut = dependencies.CreateSut();

        var result = await sut.ConfigurePasswordAsync("password", "different");

        Assert.Equal(AuthorizationResult.InvalidCredentials, result);
        dependencies.PasswordLifecycle.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ConfigurePasswordAsync_WhenExistingVaultRejectsCandidateKey_ReturnsConflict()
    {
        var dependencies = new Dependencies();
        var failure = Result.Fail<SensitiveBuffer>(new AuthorizationEnvelopePasswordLifecycleError(
            AuthorizationEnvelopePasswordLifecycleErrorCode.ActivationFailed,
            "synthetic activation failure"));
        failure.WithError(new AuthorizationEnvelopeActivationError(
            AuthorizationEnvelopeActivationErrorCode.VaultVerificationFailed,
            "synthetic vault verification failure"));
        dependencies.PasswordLifecycle.Setup(value => value.ConfigureAsync(
                "recovery-password",
                CancellationToken.None))
            .ReturnsAsync(failure);
        var sut = dependencies.CreateSut();

        var result = await sut.ConfigurePasswordAsync(
            "recovery-password",
            "recovery-password");

        Assert.Equal(AuthorizationResult.ExistingVaultConflict, result);
        dependencies.SettingsService.Verify(value => value.SaveAsync(), Times.Never);
        dependencies.Security.Verify(value => value.SetDek(It.IsAny<byte[]>()), Times.Never);
    }

    [Fact]
    public async Task ConfigurePasswordAsync_AfterActivation_PersistsPreferenceAndClearsContextInput()
    {
        var expectedKey = Enumerable.Range(1, 32).Select(value => (byte)value).ToArray();
        var lifecycleKey = SensitiveBuffer.CopyFrom(expectedKey);
        byte[]? contextInput = null;
        var dependencies = new Dependencies
        {
            SessionState = new AuthorizationEnvelopeSessionState(true, true, false)
        };
        dependencies.PasswordLifecycle.Setup(value => value.ConfigureAsync(
                "recovery-password",
                CancellationToken.None))
            .ReturnsAsync(Result.Ok(lifecycleKey));
        dependencies.Security.Setup(value => value.SetDek(It.IsAny<byte[]>()))
            .Callback<byte[]>(key =>
            {
                Assert.Equal(expectedKey, key);
                contextInput = key;
            });
        var sut = dependencies.CreateSut();

        var result = await sut.ConfigurePasswordAsync("recovery-password", "recovery-password");

        Assert.Equal(AuthorizationResult.Success, result);
        Assert.Equal(PreferredUnlockMethod.Password, dependencies.Settings.PreferredUnlockMethod);
        dependencies.SettingsService.Verify(value => value.SaveAsync(), Times.Once);
        Assert.True(sut.State.IsConfigured);
        Assert.True(sut.State.IsUnlocked);
        Assert.NotNull(contextInput);
        Assert.All(contextInput, value => Assert.Equal(0, value));
        Assert.Throws<ObjectDisposedException>(() => _ = lifecycleKey.Memory);
    }

    [Fact]
    public async Task ConfigurePasswordAsync_WhenPreferenceSaveFails_RevertsPreferenceAndDoesNotActivateContext()
    {
        var lifecycleKey = SensitiveBuffer.CopyFrom(new byte[32]);
        var dependencies = new Dependencies
        {
            SessionState = new AuthorizationEnvelopeSessionState(true, true, false)
        };
        dependencies.Settings.PreferredUnlockMethod = PreferredUnlockMethod.PlatformQuickUnlock;
        dependencies.PasswordLifecycle.Setup(value => value.ConfigureAsync(
                It.IsAny<string>(),
                CancellationToken.None))
            .ReturnsAsync(Result.Ok(lifecycleKey));
        dependencies.SettingsService.Setup(value => value.SaveAsync())
            .ReturnsAsync(Result.Fail("synthetic preference failure"));
        var sut = dependencies.CreateSut();

        var result = await sut.ConfigurePasswordAsync("recovery-password", "recovery-password");

        Assert.Equal(AuthorizationResult.Failed, result);
        Assert.Equal(PreferredUnlockMethod.PlatformQuickUnlock, dependencies.Settings.PreferredUnlockMethod);
        dependencies.Security.Verify(value => value.SetDek(It.IsAny<byte[]>()), Times.Never);
        Assert.Throws<ObjectDisposedException>(() => _ = lifecycleKey.Memory);
    }

    [Fact]
    public async Task ConfigureHelloAsync_WithoutRecoveryPassword_RequiresPassword()
    {
        var dependencies = new Dependencies();
        var sut = dependencies.CreateSut();

        var result = await sut.ConfigureHelloAsync();

        Assert.Equal(AuthorizationResult.PasswordRequired, result);
        dependencies.Enrollment.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ConfigureHelloAsync_WithWrongRecoveryPassword_ReturnsInvalidCredentials()
    {
        var dependencies = new Dependencies();
        dependencies.Enrollment.Setup(value => value.EnableAsync(
                "wrong-password",
                CancellationToken.None))
            .ReturnsAsync(Result.Fail(new PlatformQuickUnlockEnrollmentError(
                PlatformQuickUnlockEnrollmentErrorCode.InvalidRecoveryPassword,
                "synthetic invalid password")));
        var sut = dependencies.CreateSut();

        var result = await sut.ConfigureHelloAsync("wrong-password");

        Assert.Equal(AuthorizationResult.InvalidCredentials, result);
        dependencies.SettingsService.Verify(value => value.SaveAsync(), Times.Never);
    }

    [Fact]
    public async Task ConfigureHelloAsync_AfterEnrollment_PersistsPreferenceAndProjectsHelloGate()
    {
        var dependencies = new Dependencies
        {
            SessionState = new AuthorizationEnvelopeSessionState(true, true, true)
        };
        dependencies.Enrollment.Setup(value => value.EnableAsync(
                "recovery-password",
                CancellationToken.None))
            .ReturnsAsync(Result.Ok());
        var sut = dependencies.CreateSut();

        var result = await sut.ConfigureHelloAsync("recovery-password");

        Assert.Equal(AuthorizationResult.Success, result);
        Assert.Equal(PreferredUnlockMethod.PlatformQuickUnlock, dependencies.Settings.PreferredUnlockMethod);
        Assert.Equal(AuthorizationGateKind.Hello, sut.State.ConfiguredGate);
        dependencies.SettingsService.Verify(value => value.SaveAsync(), Times.Once);
    }

    [Fact]
    public async Task SetAppLockEnabledAsync_WhenDisabled_PreservesDeviceCredentialPreference()
    {
        var dependencies = new Dependencies
        {
            SessionState = new AuthorizationEnvelopeSessionState(
                true,
                true,
                true,
                PlatformUnlockMethod: PreferredUnlockMethod.PlatformDeviceCredential)
        };
        dependencies.Settings.AppLockEnabled = true;
        dependencies.Settings.PreferredUnlockMethod =
            PreferredUnlockMethod.PlatformDeviceCredential;
        dependencies.UnattendedEnrollment.Setup(value => value.EnableAsync(
                "recovery-password",
                CancellationToken.None))
            .Callback(() => dependencies.SessionState =
                new AuthorizationEnvelopeSessionState(
                    true,
                    true,
                    true,
                    HasUnattendedUnlock: true))
            .ReturnsAsync(Result.Ok());
        var sut = dependencies.CreateSut();
        await sut.InitializeAsync();

        var result = await sut.SetAppLockEnabledAsync(false, "recovery-password");

        Assert.Equal(AuthorizationResult.Success, result);
        Assert.False(dependencies.Settings.AppLockEnabled);
        Assert.Equal(
            PreferredUnlockMethod.PlatformDeviceCredential,
            dependencies.Settings.PreferredUnlockMethod);
    }

    [Fact]
    public async Task ConfigureUnlockMethodAsync_WithDeviceCredential_PersistsDeviceGate()
    {
        var dependencies = new Dependencies
        {
            SessionState = new AuthorizationEnvelopeSessionState(true, true, false)
        };
        dependencies.Enrollment.Setup(value => value.EnableAsync(
                "recovery-password",
                PreferredUnlockMethod.PlatformDeviceCredential,
                CancellationToken.None))
            .Callback(() => dependencies.SessionState =
                new AuthorizationEnvelopeSessionState(true, true, true))
            .ReturnsAsync(Result.Ok());
        var sut = dependencies.CreateSut();
        await sut.InitializeAsync();

        var result = await sut.ConfigureUnlockMethodAsync(
            PreferredUnlockMethod.PlatformDeviceCredential,
            "recovery-password");

        Assert.Equal(AuthorizationResult.Success, result);
        Assert.Equal(
            PreferredUnlockMethod.PlatformDeviceCredential,
            dependencies.Settings.PreferredUnlockMethod);
        Assert.Equal(AuthorizationGateKind.DeviceCredential, sut.State.ConfiguredGate);
    }

    [Fact]
    public async Task ConfigureUnlockMethodAsync_WithPassword_RequiresRecoveryPassword()
    {
        var dependencies = new Dependencies
        {
            SessionState = new AuthorizationEnvelopeSessionState(
                true,
                true,
                true,
                PlatformUnlockMethod: PreferredUnlockMethod.PlatformQuickUnlock)
        };
        dependencies.Settings.PreferredUnlockMethod =
            PreferredUnlockMethod.PlatformQuickUnlock;
        dependencies.Session.Setup(value => value.TryUnlockWithPasswordAsync(
                "recovery-password",
                CancellationToken.None))
            .ReturnsAsync(Result.Ok(AuthorizationResult.Success));
        var sut = dependencies.CreateSut();
        await sut.InitializeAsync();

        var result = await sut.ConfigureUnlockMethodAsync(
            PreferredUnlockMethod.Password,
            "recovery-password");

        Assert.Equal(AuthorizationResult.Success, result);
        Assert.Equal(PreferredUnlockMethod.Password, dependencies.Settings.PreferredUnlockMethod);
        Assert.Equal(AuthorizationGateKind.Password, sut.State.ConfiguredGate);
    }

    [Fact]
    public async Task ConfigureUnlockMethodAsync_WithPasswordRejectsInvalidRecoveryPassword()
    {
        var dependencies = new Dependencies
        {
            SessionState = new AuthorizationEnvelopeSessionState(
                true,
                true,
                true,
                PlatformUnlockMethod: PreferredUnlockMethod.PlatformQuickUnlock)
        };
        dependencies.Settings.PreferredUnlockMethod =
            PreferredUnlockMethod.PlatformQuickUnlock;
        dependencies.Session.Setup(value => value.TryUnlockWithPasswordAsync(
                "wrong-password",
                CancellationToken.None))
            .ReturnsAsync(Result.Ok(AuthorizationResult.InvalidCredentials));
        var sut = dependencies.CreateSut();
        await sut.InitializeAsync();

        var result = await sut.ConfigureUnlockMethodAsync(
            PreferredUnlockMethod.Password,
            "wrong-password");

        Assert.Equal(AuthorizationResult.InvalidCredentials, result);
        Assert.Equal(
            PreferredUnlockMethod.PlatformQuickUnlock,
            dependencies.Settings.PreferredUnlockMethod);
        Assert.Equal(AuthorizationGateKind.Hello, sut.State.ConfiguredGate);
        dependencies.SettingsService.Verify(value => value.SaveAsync(), Times.Never);
    }

    [Fact]
    public async Task InitializeAsync_WhenPersistedPlatformPreferenceDiffersFromWrapper_UsesWrapperPolicy()
    {
        var dependencies = new Dependencies
        {
            SessionState = new AuthorizationEnvelopeSessionState(
                true,
                true,
                true,
                PlatformUnlockMethod: PreferredUnlockMethod.PlatformDeviceCredential)
        };
        dependencies.Settings.PreferredUnlockMethod =
            PreferredUnlockMethod.PlatformQuickUnlock;
        var sut = dependencies.CreateSut();

        await sut.InitializeAsync();

        Assert.Equal(AuthorizationGateKind.DeviceCredential, sut.State.ConfiguredGate);
        Assert.Equal(
            PreferredUnlockMethod.PlatformDeviceCredential,
            dependencies.Settings.PreferredUnlockMethod);
    }

    [Fact]
    public async Task SetAppLockEnabledAsync_WhenAlreadyFailClosed_RetriesStoredDeviceMethod()
    {
        var dependencies = new Dependencies
        {
            SessionState = new AuthorizationEnvelopeSessionState(true, true, false)
        };
        dependencies.Settings.AppLockEnabled = true;
        dependencies.Settings.PreferredUnlockMethod =
            PreferredUnlockMethod.PlatformDeviceCredential;
        dependencies.Enrollment.Setup(value => value.EnableAsync(
                "recovery-password",
                PreferredUnlockMethod.PlatformDeviceCredential,
                CancellationToken.None))
            .Callback(() => dependencies.SessionState =
                new AuthorizationEnvelopeSessionState(true, true, true))
            .ReturnsAsync(Result.Ok());
        var sut = dependencies.CreateSut();
        await sut.InitializeAsync();

        var result = await sut.SetAppLockEnabledAsync(true, "recovery-password");

        Assert.Equal(AuthorizationResult.Success, result);
        Assert.Equal(AuthorizationGateKind.DeviceCredential, sut.State.ConfiguredGate);
        dependencies.Enrollment.Verify(value => value.EnableAsync(
            "recovery-password",
            PreferredUnlockMethod.PlatformDeviceCredential,
            CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task SetGateAsync_ToHelloWithoutWrapper_RequiresPasswordWithoutSavingPreference()
    {
        var dependencies = new Dependencies
        {
            SessionState = new AuthorizationEnvelopeSessionState(true, true, false)
        };
        var sut = dependencies.CreateSut();
        await sut.InitializeAsync();

        var result = await sut.SetGateAsync(AuthorizationGateKind.Hello);

        Assert.Equal(AuthorizationResult.PasswordRequired, result);
        dependencies.SettingsService.Verify(value => value.SaveAsync(), Times.Never);
    }

    [Fact]
    public async Task SetGateAsync_WhenPreferenceSaveFails_RevertsInMemoryPreference()
    {
        var dependencies = new Dependencies
        {
            SessionState = new AuthorizationEnvelopeSessionState(true, true, true)
        };
        dependencies.SettingsService.Setup(value => value.SaveAsync())
            .ReturnsAsync(Result.Fail("synthetic preference failure"));
        var sut = dependencies.CreateSut();
        await sut.InitializeAsync();

        var result = await sut.SetGateAsync(AuthorizationGateKind.Hello);

        Assert.Equal(AuthorizationResult.Failed, result);
        Assert.Equal(PreferredUnlockMethod.Password, dependencies.Settings.PreferredUnlockMethod);
        Assert.Equal(AuthorizationGateKind.Password, sut.State.ConfiguredGate);
    }

    [Fact]
    public async Task ChangePasswordAsync_AfterReplacement_RefreshesSessionAndClearsContextInput()
    {
        var expectedKey = Enumerable.Repeat((byte)8, 32).ToArray();
        var lifecycleKey = SensitiveBuffer.CopyFrom(expectedKey);
        byte[]? contextInput = null;
        var dependencies = new Dependencies
        {
            SessionState = new AuthorizationEnvelopeSessionState(true, true, true)
        };
        dependencies.PasswordLifecycle.Setup(value => value.ChangePasswordAsync(
                "current-password",
                "new-password",
                CancellationToken.None))
            .ReturnsAsync(Result.Ok(lifecycleKey));
        dependencies.Security.Setup(value => value.SetDek(It.IsAny<byte[]>()))
            .Callback<byte[]>(key => contextInput = key);
        var sut = dependencies.CreateSut();

        var result = await sut.ChangePasswordAsync("current-password", "new-password");

        Assert.Equal(AuthorizationResult.Success, result);
        Assert.True(sut.State.IsUnlocked);
        Assert.NotNull(contextInput);
        Assert.All(contextInput, value => Assert.Equal(0, value));
        Assert.Throws<ObjectDisposedException>(() => _ = lifecycleKey.Memory);
    }

    [Fact]
    public async Task Lock_ClearsSecurityContextAndLocksState()
    {
        var dependencies = new Dependencies();
        dependencies.Session.Setup(value => value.TryUnlockWithPasswordAsync(
                It.IsAny<string>(),
                CancellationToken.None))
            .ReturnsAsync(Result.Ok(AuthorizationResult.Success));
        var sut = dependencies.CreateSut();
        Assert.Equal(AuthorizationResult.Success, await sut.TryUnlockWithPasswordAsync("password"));

        sut.Lock();

        dependencies.Security.Verify(value => value.Lock(), Times.Once);
        Assert.False(sut.State.IsUnlocked);
    }

    private sealed class Dependencies
    {
        public AppSettings Settings { get; } = new();
        public Mock<ISettingsService> SettingsService { get; } = new();
        public Mock<IAuthorizationEnvelopeSession> Session { get; } = new();
        public Mock<IAuthorizationEnvelopePasswordLifecycle> PasswordLifecycle { get; } = new();
        public Mock<IPlatformQuickUnlockEnrollment> Enrollment { get; } = new();
        public Mock<IPlatformUnattendedUnlockEnrollment> UnattendedEnrollment { get; } = new();
        public Mock<IPlatformQuickUnlock> Platform { get; } = new();
        public Mock<IPasswordValidationService> Validation { get; } = new();
        public Mock<ISecurityContext> Security { get; } = new();
        public AuthorizationState State { get; } = new();

        public AuthorizationEnvelopeSessionState SessionState { get; set; } =
            AuthorizationEnvelopeSessionState.NotInitialized;

        public Dependencies(
            bool validPasswordConfirmation = true,
            bool validNewPassword = true)
        {
            SettingsService.SetupGet(value => value.Current).Returns(Settings);
            SettingsService.Setup(value => value.SaveAsync()).ReturnsAsync(Result.Ok());
            Session.SetupGet(value => value.State).Returns(() => SessionState);
            Session.Setup(value => value.InitializeAsync(CancellationToken.None))
                .ReturnsAsync(() => Result.Ok(SessionState));
            Validation.Setup(value => value.IsValidNewWithConfirmation(
                    It.IsAny<string>(),
                    It.IsAny<string>()))
                .Returns(validPasswordConfirmation);
            Validation.Setup(value => value.IsValidNew(It.IsAny<string>()))
                .Returns(validNewPassword);
        }

        public PortableAuthorizationService CreateSut() => new(
            SettingsService.Object,
            Session.Object,
            PasswordLifecycle.Object,
            Enrollment.Object,
            Platform.Object,
            Validation.Object,
            Security.Object,
            State,
            NullLogger<PortableAuthorizationService>.Instance,
            UnattendedEnrollment.Object);
    }
}
