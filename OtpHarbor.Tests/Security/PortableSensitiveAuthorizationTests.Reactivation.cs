using System.Text.Json;
using FluentResults;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TOTP.Core.Enums;
using TOTP.Core.Security.Interfaces;
using TOTP.Core.Security.Models;
using TOTP.Infrastructure.Security;
using TOTP.Platform.Windows.Security;

namespace TOTP.Tests.Security;

public sealed partial class PortableSensitiveAuthorizationTests
{
    [Theory]
    [InlineData("success")]
    [InlineData("cancel")]
    [InlineData("session changes")]
    [InlineData("key changes")]
    [InlineData("preference changes")]
    [InlineData("wrapper save fails")]
    [InlineData("preference save fails")]
    public async Task DesktopReactivation_UsesOneWindowsVerificationAndEnrollment(string scenario)
    {
        using var context = new Context(appLockEnabled: false);
        var gate = new Mock<IHelloGate>();
        gate.Setup(value => value.GetAvailabilityAsync(It.IsAny<CancellationToken>())).ReturnsAsync(PlatformQuickUnlockAvailability.Available);
        gate.Setup(value => value.BeginPromptSession()).Returns(Mock.Of<IDisposable>());
        gate.Setup(value => value.RequestVerificationAsync(It.IsAny<CancellationToken>()))
            .Callback(() =>
            {
                if (scenario == "session changes") context.State.Lock();
                if (scenario == "key changes") context.Security.SetDek(Enumerable.Repeat((byte)9, 32).ToArray());
                if (scenario == "preference changes") context.Settings.PreferredUnlockMethod = PreferredUnlockMethod.Password;
            }).ReturnsAsync(scenario == "cancel" ? AuthorizationResult.Cancelled : AuthorizationResult.Success);
        byte[]? protectedInput = null;
        gate.Setup(value => value.ProtectKeyAsync(It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback((byte[] key, string _, CancellationToken _) =>
            {
                Assert.Equal(Context.Key, key);
                protectedInput = key;
            }).ReturnsAsync(new byte[256]);
        gate.Setup(value => value.RemoveKeyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var platform = new WindowsPlatformQuickUnlock(gate.Object, NullLogger<WindowsPlatformQuickUnlock>.Instance);
        var store = ReactivationStore(context, scenario == "wrapper save fails");
        var unattended = new Mock<IPlatformUnattendedUnlock>();
        unattended.SetupGet(value => value.ProviderId).Returns(PlatformQuickUnlockContract.WindowsCngUnattendedProvider);
        unattended.Setup(value => value.RemoveAsync(It.IsAny<PlatformQuickUnlockWrapperV2>(), CancellationToken.None)).ReturnsAsync(Result.Ok());
        var vault = new Mock<IStoredVaultKeyVerifier>();
        vault.Setup(value => value.VerifyAsync(It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok(VaultKeyVerificationStatus.Verified));
        using var enrollment = new PlatformQuickUnlockEnrollment(store.Object, Mock.Of<IMasterPasswordService>(),
            vault.Object, [unattended.Object, platform], NullLogger<PlatformQuickUnlockEnrollment>.Instance);
        if (scenario == "preference save fails") context.Preferences.Setup(value => value.SaveAsync()).ReturnsAsync(Result.Fail("synthetic save failure"));

        var result = await context.Create(enrollment, platform).ReactivateAppLockWithPlatformAsync(
            PreferredUnlockMethod.PlatformQuickUnlock, TestContext.Current.CancellationToken);

        Assert.Equal(scenario switch
        {
            "success" => AuthorizationResult.Success,
            "wrapper save fails" or "preference save fails" => AuthorizationResult.Failed,
            _ => AuthorizationResult.Cancelled
        }, result);
        gate.Verify(value => value.RequestVerificationAsync(It.IsAny<CancellationToken>()), Times.Once);
        gate.Verify(value => value.ProtectKeyAsync(It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            scenario == "cancel" ? Times.Never() : Times.Once());
        Assert.Equal(scenario is "success" or "preference save fails", context.Settings.AppLockEnabled);
        context.Session.Verify(value => value.TryUnlockWithPlatformAsync(It.IsAny<CancellationToken>()), Times.Never);
        context.Unattended.Verify(value => value.DisableAsync(It.IsAny<CancellationToken>()), Times.Never);
        if (scenario == "success")
        {
            Assert.True(context.State.IsUnlocked);
            Assert.Equal(PreferredUnlockMethod.PlatformQuickUnlock, context.State.PreferredUnlockMethod);
            gate.Verify(value => value.RemoveKeyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }
        if (scenario == "preference save fails") Assert.False(context.State.IsUnlocked);
        if (protectedInput is not null) Assert.All(protectedInput, value => Assert.Equal(0, value));
        Assert.All(context.Security.Copies, key => Assert.All(key, value => Assert.Equal(0, value)));
    }

    [Theory]
    [InlineData("success")]
    [InlineData("cancel")]
    [InlineData("wrong key")]
    public async Task DesktopReactivation_KeychainMustVerifyPresenceAndRecoverMatchingKey(string scenario)
    {
        using var context = new Context(appLockEnabled: false);
        var store = ReactivationStore(context);
        var platform = new Mock<IPlatformQuickUnlock>();
        platform.SetupGet(value => value.ProviderId).Returns(PlatformQuickUnlockContract.MacOSKeychainProvider);
        platform.SetupGet(value => value.UnlockMethod).Returns(PreferredUnlockMethod.PlatformQuickUnlock);
        platform.Setup(value => value.GetAvailabilityAsync(It.IsAny<CancellationToken>())).ReturnsAsync(PlatformQuickUnlockAvailability.Available);
        var wrapper = DesktopProofWrapper(PlatformQuickUnlockContract.MacOSKeychainProvider);
        platform.Setup(value => value.RegisterAsync(It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>())).ReturnsAsync(Result.Ok(wrapper));
        ReadOnlyMemory<byte> returnedKey = default;
        platform.Setup(value => value.TryUnlockAsync(wrapper, It.IsAny<CancellationToken>())).Returns(() =>
        {
            if (scenario == "cancel") return Task.FromResult(Result.Ok(PlatformQuickUnlockAttempt.WithoutKey(PlatformQuickUnlockStatus.Cancelled)));
            var buffer = SensitiveBuffer.CopyFrom(scenario == "success" ? Context.Key : new byte[32]);
            returnedKey = buffer.Memory;
            return Task.FromResult(Result.Ok(PlatformQuickUnlockAttempt.Successful(buffer)));
        });
        platform.Setup(value => value.RemoveAsync(wrapper, CancellationToken.None)).ReturnsAsync(Result.Ok());
        var vault = new Mock<IStoredVaultKeyVerifier>();
        vault.Setup(value => value.VerifyAsync(It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok(VaultKeyVerificationStatus.Verified));
        using var enrollment = new PlatformQuickUnlockEnrollment(store.Object, Mock.Of<IMasterPasswordService>(),
            vault.Object, platform.Object, NullLogger<PlatformQuickUnlockEnrollment>.Instance);

        var result = await context.Create(enrollment, platform.Object).ReactivateAppLockWithPlatformAsync(
            PreferredUnlockMethod.PlatformQuickUnlock, TestContext.Current.CancellationToken);

        Assert.Equal(scenario == "success" ? AuthorizationResult.Success : scenario == "cancel" ? AuthorizationResult.Cancelled : AuthorizationResult.Failed, result);
        platform.Verify(value => value.RegisterAsync(It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()), Times.Once);
        platform.Verify(value => value.TryUnlockAsync(wrapper, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(scenario == "success", context.Settings.AppLockEnabled);
        store.Verify(value => value.SaveAsync(It.IsAny<AuthorizationEnvelopeV2>(), It.IsAny<CancellationToken>()), scenario == "success" ? Times.Once() : Times.Never());
        Assert.All(returnedKey.ToArray(), value => Assert.Equal(0, value));
    }

    [Theory]
    [InlineData(PreferredUnlockMethod.PlatformQuickUnlock, false)]
    [InlineData(PreferredUnlockMethod.PlatformQuickUnlock, true)]
    [InlineData(PreferredUnlockMethod.PlatformDeviceCredential, false)]
    [InlineData(PreferredUnlockMethod.PlatformDeviceCredential, true)]
    public async Task AndroidReactivation_RegistersActualKeyOnceWithoutASeparateChallengeOrUnlock(
        PreferredUnlockMethod method, bool cancelled)
    {
        using var context = new Context(method, appLockEnabled: false);
        var store = ReactivationStore(context, androidMethod: method);
        var registeredActualKey = false;
        context.Platform.Setup(value => value.RegisterAsync(It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
            .Callback((ReadOnlyMemory<byte> key, CancellationToken _) =>
            {
                Assert.Equal(Context.Key, key.ToArray());
                registeredActualKey = true;
            })
            .ReturnsAsync(() => cancelled
                ? Result.Fail<PlatformQuickUnlockWrapperV2>(new PlatformQuickUnlockError(PlatformQuickUnlockErrorCode.Cancelled, "Synthetic cancellation"))
                : Result.Ok(Wrapper(method)));
        var vault = new Mock<IStoredVaultKeyVerifier>();
        vault.Setup(value => value.VerifyAsync(It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok(VaultKeyVerificationStatus.Verified));
        using var enrollment = new PlatformQuickUnlockEnrollment(store.Object, Mock.Of<IMasterPasswordService>(),
            vault.Object, context.Platform.Object, NullLogger<PlatformQuickUnlockEnrollment>.Instance);

        var result = await context.Create(enrollment).ReactivateAppLockWithPlatformAsync(method, TestContext.Current.CancellationToken);

        Assert.Equal(cancelled ? AuthorizationResult.Cancelled : AuthorizationResult.Success, result);
        context.Platform.Verify(value => value.RegisterAsync(It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()), Times.Once);
        context.Platform.Verify(value => value.TryUnlockAsync(It.IsAny<PlatformQuickUnlockWrapperV2>(), It.IsAny<CancellationToken>()), Times.Never);
        context.Session.Verify(value => value.TryUnlockWithPlatformAsync(It.IsAny<CancellationToken>()), Times.Never);
        store.Verify(value => value.SaveAsync(It.IsAny<AuthorizationEnvelopeV2>(), It.IsAny<CancellationToken>()), cancelled ? Times.Never() : Times.Once());
        Assert.Equal(!cancelled, context.Settings.AppLockEnabled);
        Assert.True(context.State.IsUnlocked);
        Assert.True(registeredActualKey);
        Assert.All(context.Security.Copies, key => Assert.All(key, value => Assert.Equal(0, value)));
    }

    [Fact]
    public async Task Reactivation_CancellationAfterWrapperCommitEnablesProtectionAndLocks()
    {
        using var context = new Context(appLockEnabled: false);
        using var cancellation = new CancellationTokenSource();
        context.Enrollment.Setup(value => value.EnableWithPlatformVerificationAsync(
                It.IsAny<ReadOnlyMemory<byte>>(), PreferredUnlockMethod.PlatformQuickUnlock,
                It.IsAny<Func<bool>>(), It.IsAny<CancellationToken>()))
            .Callback((ReadOnlyMemory<byte> _, PreferredUnlockMethod _, Func<bool> canCommit, CancellationToken _) =>
            {
                Assert.True(canCommit());
                context.SessionState = new(true, true, true, false, PreferredUnlockMethod.PlatformQuickUnlock);
                cancellation.Cancel();
            }).ReturnsAsync(Result.Ok());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => context.Create()
            .ReactivateAppLockWithPlatformAsync(PreferredUnlockMethod.PlatformQuickUnlock, cancellation.Token));

        Assert.True(context.Settings.AppLockEnabled);
        Assert.False(context.State.IsUnlocked);
        Assert.False(context.Security.IsUnlocked);
        context.Preferences.Verify(value => value.SaveAsync(), Times.Never);
        Assert.All(context.Security.Copies, key => Assert.All(key, value => Assert.Equal(0, value)));
    }

    private static Mock<IAuthorizationEnvelopeStore> ReactivationStore(Context context, bool failSave = false,
        PreferredUnlockMethod? androidMethod = null)
    {
        var store = new Mock<IAuthorizationEnvelopeStore>();
        var envelope = AuthorizationEnvelopeV2CodecTests.CreateEnvelope() with
        {
            QuickUnlockWrapper = androidMethod.HasValue ? new PlatformQuickUnlockWrapperV2
            {
                Provider = PlatformQuickUnlockContract.AndroidKeystoreUnattendedProvider, ProviderVersion = 1,
                AuthenticationPolicy = PlatformQuickUnlockContract.UserVerificationNotRequired,
                KeyReference = "SYNTHETIC_ANDROID_UNATTENDED",
                WrappedKey = new PlatformWrappedKeyV2 { Algorithm = PlatformQuickUnlockContract.AndroidAes256GcmAlgorithm,
                    Nonce = new byte[12], Ciphertext = new byte[48] }
            } : new PlatformQuickUnlockWrapperV2
            {
                Provider = PlatformQuickUnlockContract.WindowsCngUnattendedProvider, ProviderVersion = 1,
                AuthenticationPolicy = PlatformQuickUnlockContract.UserVerificationNotRequired,
                KeyReference = PlatformQuickUnlockContract.DesktopUnattendedReferencePrefix + Guid.NewGuid().ToString("N"),
                WrappedKey = new PlatformWrappedKeyV2 { Algorithm = PlatformQuickUnlockContract.RsaOaepSha256Algorithm, Ciphertext = new byte[256] }
            }
        };
        store.Setup(value => value.LoadAsync(It.IsAny<CancellationToken>())).Returns(() => Task.FromResult(Result.Ok<AuthorizationEnvelopeV2?>(
            JsonSerializer.Deserialize<AuthorizationEnvelopeV2>(JsonSerializer.Serialize(envelope)))));
        store.Setup(value => value.SaveAsync(It.IsAny<AuthorizationEnvelopeV2>(), It.IsAny<CancellationToken>()))
            .Callback(() =>
            {
                if (!failSave) context.SessionState = new(true, true, true, false, androidMethod ?? PreferredUnlockMethod.PlatformQuickUnlock);
            }).ReturnsAsync(failSave ? Result.Fail("synthetic wrapper save failure") : Result.Ok());
        return store;
    }
}
