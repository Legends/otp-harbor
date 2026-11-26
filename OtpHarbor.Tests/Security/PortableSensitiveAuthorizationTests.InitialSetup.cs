using FluentResults;
using Moq;
using TOTP.Core.Enums;
using TOTP.Core.Security;
using TOTP.Core.Security.Models;
using TOTP.Infrastructure.Security;

namespace TOTP.Tests.Security;

public sealed partial class PortableSensitiveAuthorizationTests
{
    [Theory]
    [InlineData(InitialAppLockChoice.MasterPassword)]
    [InlineData(InitialAppLockChoice.StrongBiometrics)]
    [InlineData(InitialAppLockChoice.DeviceScreenLock)]
    [InlineData(InitialAppLockChoice.AppLockDisabled)]
    public async Task InitialAuthenticationChoice_UsesFreshSetupGrantOnceAndClearsKeyCopies(InitialAppLockChoice choice)
    {
        using var context = new Context(PreferredUnlockMethod.Password);
        var sut = await ConfigureInitialPasswordAsync(context);
        context.Unattended.Setup(value => value.EnableWithAuthenticatedKeyAsync(
                It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
            .Callback(() => context.SessionState = new(true, true, false, true, PreferredUnlockMethod.Password))
            .ReturnsAsync(Result.Ok());

        Assert.Equal(AuthorizationResult.Success,
            await sut.CompleteInitialAppLockSetupAsync(choice, TestContext.Current.CancellationToken));
        Assert.Equal(choice != InitialAppLockChoice.AppLockDisabled, context.Settings.AppLockEnabled);
        Assert.Equal(choice switch
        {
            InitialAppLockChoice.StrongBiometrics => PreferredUnlockMethod.PlatformQuickUnlock,
            InitialAppLockChoice.DeviceScreenLock => PreferredUnlockMethod.PlatformDeviceCredential,
            _ => PreferredUnlockMethod.Password
        }, context.State.PreferredUnlockMethod);
        context.Unattended.Verify(value => value.EnableWithAuthenticatedKeyAsync(
            It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()),
            choice == InitialAppLockChoice.AppLockDisabled ? Times.Once : Times.Never);
        context.Unattended.Verify(value => value.EnableAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.All(context.Security.Copies, bytes => Assert.All(bytes, value => Assert.Equal((byte)0, value)));
        Assert.Equal(AuthorizationResult.PasswordRequired,
            await sut.CompleteInitialAppLockSetupAsync(InitialAppLockChoice.AppLockDisabled, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InitialAppLockDeactivation_RequiresFreshSetupAndAnUnlockedSession(bool configureThenLock)
    {
        using var context = new Context(PreferredUnlockMethod.Password);
        var sut = configureThenLock ? await ConfigureInitialPasswordAsync(context) : context.Create();
        if (configureThenLock) sut.Lock();
        Assert.Equal(AuthorizationResult.PasswordRequired,
            await sut.CompleteInitialAppLockSetupAsync(InitialAppLockChoice.AppLockDisabled, TestContext.Current.CancellationToken));
        Assert.True(context.Settings.AppLockEnabled);
        context.Unattended.Verify(value => value.EnableWithAuthenticatedKeyAsync(
            It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InitialAppLockDeactivation_FailedEnrollmentOrSaveKeepsProtectionEnabled(bool failSave)
    {
        using var context = new Context(PreferredUnlockMethod.Password);
        var sut = await ConfigureInitialPasswordAsync(context);
        context.Unattended.Setup(value => value.EnableWithAuthenticatedKeyAsync(
                It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(failSave ? Result.Ok() : Result.Fail("Synthetic enrollment failure"));
        context.Unattended.Setup(value => value.DisableAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Result.Ok());
        if (failSave) context.Preferences.Setup(value => value.SaveAsync()).ReturnsAsync(Result.Fail("Synthetic save failure"));
        Assert.Equal(AuthorizationResult.Failed,
            await sut.CompleteInitialAppLockSetupAsync(InitialAppLockChoice.AppLockDisabled, TestContext.Current.CancellationToken));
        Assert.True(context.Settings.AppLockEnabled);
        context.Unattended.Verify(value => value.DisableAsync(It.IsAny<CancellationToken>()), failSave ? Times.Once : Times.Never);
        Assert.All(context.Security.Copies, bytes => Assert.All(bytes, value => Assert.Equal((byte)0, value)));
    }

    [Fact]
    public async Task InitialAppLockDeactivation_LockDuringEnrollmentCannotDisableProtection()
    {
        using var context = new Context(PreferredUnlockMethod.Password);
        var sut = await ConfigureInitialPasswordAsync(context);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = new TaskCompletionSource<Result>(TaskCreationOptions.RunContinuationsAsynchronously);
        context.Unattended.Setup(value => value.EnableWithAuthenticatedKeyAsync(
            It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>())).Returns(() =>
            {
                entered.SetResult();
                return completed.Task;
            });
        var operation = sut.CompleteInitialAppLockSetupAsync(InitialAppLockChoice.AppLockDisabled, TestContext.Current.CancellationToken);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        sut.Lock();
        completed.SetResult(Result.Ok());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
        Assert.True(context.Settings.AppLockEnabled);
        Assert.False(context.State.IsUnlocked);
        Assert.False(context.Security.IsUnlocked);
        Assert.All(context.Security.Copies, bytes => Assert.All(bytes, value => Assert.Equal((byte)0, value)));
    }

    private static async Task<PortableAuthorizationService> ConfigureInitialPasswordAsync(Context context)
    {
        context.SessionState = new(true, true, false, false, PreferredUnlockMethod.Password);
        context.Validation.Setup(value => value.IsValidNewWithConfirmation(It.IsAny<string>(), It.IsAny<string>())).Returns(true);
        context.PasswordLifecycle.Setup(value => value.ConfigureAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Result.Ok(SensitiveBuffer.CopyFrom(Context.Key)));
        var service = context.Create();
        Assert.Equal(AuthorizationResult.Success, await service.ConfigurePasswordAsync("synthetic setup", "synthetic setup"));
        return service;
    }
}
