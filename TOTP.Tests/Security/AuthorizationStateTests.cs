using TOTP.Core.Enums;
using TOTP.Core.Security;
using TOTP.Core.Security.Models;

namespace TOTP.Tests.Security;

public sealed class AuthorizationStateTests
{
    [Fact]
    public void SetConfiguration_WithPasswordPreference_ProjectsPasswordGate()
    {
        var sut = new AuthorizationState();
        var changed = 0;
        sut.Changed += (_, _) => changed++;

        sut.SetConfiguration(isConfigured: true, PreferredUnlockMethod.Password);

        Assert.True(sut.IsConfigured);
        Assert.Equal(PreferredUnlockMethod.Password, sut.PreferredUnlockMethod);
        Assert.Equal(AuthorizationGateKind.Password, sut.ConfiguredGate);
        Assert.Equal(1, changed);
    }

    [Fact]
    public void SetConfiguration_WithPlatformQuickUnlock_ProjectsLegacyHelloGate()
    {
        var sut = new AuthorizationState();

        sut.SetConfiguration(isConfigured: true, PreferredUnlockMethod.PlatformQuickUnlock);

        Assert.True(sut.IsConfigured);
        Assert.Equal(PreferredUnlockMethod.PlatformQuickUnlock, sut.PreferredUnlockMethod);
        Assert.Equal(AuthorizationGateKind.Hello, sut.ConfiguredGate);
    }

    [Fact]
    public void SetConfiguration_WithDeviceCredential_ProjectsDeviceCredentialGate()
    {
        var sut = new AuthorizationState();

        sut.SetConfiguration(
            isConfigured: true,
            PreferredUnlockMethod.PlatformDeviceCredential);

        Assert.True(sut.IsConfigured);
        Assert.Equal(
            PreferredUnlockMethod.PlatformDeviceCredential,
            sut.PreferredUnlockMethod);
        Assert.Equal(AuthorizationGateKind.DeviceCredential, sut.ConfiguredGate);
    }

    [Fact]
    public void SetConfiguration_WhenNotConfigured_FallsBackToPasswordAndNoGate()
    {
        var sut = new AuthorizationState();

        sut.SetConfiguration(isConfigured: false, PreferredUnlockMethod.PlatformQuickUnlock);

        Assert.False(sut.IsConfigured);
        Assert.Equal(PreferredUnlockMethod.Password, sut.PreferredUnlockMethod);
        Assert.Equal(AuthorizationGateKind.None, sut.ConfiguredGate);
    }

    [Fact]
    public void SetConfiguration_WithUnknownPreference_FailsClosedToPassword()
    {
        var sut = new AuthorizationState();

        sut.SetConfiguration(isConfigured: true, (PreferredUnlockMethod)999);

        Assert.True(sut.IsConfigured);
        Assert.Equal(PreferredUnlockMethod.Password, sut.PreferredUnlockMethod);
        Assert.Equal(AuthorizationGateKind.Password, sut.ConfiguredGate);
    }
}
