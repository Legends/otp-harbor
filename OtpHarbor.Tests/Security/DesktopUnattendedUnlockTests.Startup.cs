using System.Security.Cryptography;
using FluentResults;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TOTP.Avalonia.Desktop.Localization;
using TOTP.Avalonia.Desktop.Startup;
using TOTP.Core.Enums;
using TOTP.Core.Security;
using TOTP.Core.Security.Interfaces;
using TOTP.Core.Security.Models;
using TOTP.DAL.Services;
using TOTP.Infrastructure.Security;
using TOTP.Infrastructure.Services;
using TOTP.Platform.Windows.Security;
using TOTP.Tests.Common;

namespace TOTP.Tests.Security;

public sealed partial class DesktopUnattendedUnlockTests
{
    [Theory]
    [InlineData(PlatformQuickUnlockContract.WindowsCngUnattendedProvider, "valid")]
    [InlineData(PlatformQuickUnlockContract.WindowsCngUnattendedProvider, "missing key")]
    [InlineData(PlatformQuickUnlockContract.WindowsCngUnattendedProvider, "tampered")]
    [InlineData(PlatformQuickUnlockContract.MacOSUnattendedProvider, "valid")]
    [InlineData(PlatformQuickUnlockContract.MacOSUnattendedProvider, "missing key")]
    [InlineData(PlatformQuickUnlockContract.MacOSUnattendedProvider, "tampered")]
    [InlineData(PlatformQuickUnlockContract.LinuxUnattendedProvider, "valid")]
    [InlineData(PlatformQuickUnlockContract.LinuxUnattendedProvider, "missing key")]
    [InlineData(PlatformQuickUnlockContract.LinuxUnattendedProvider, "tampered")]
    public async Task DisabledAppLock_ColdDesktopStartupUsesPersistedWrapperWithoutBiometricPrompt(string provider, string scenario)
    {
        if (provider == PlatformQuickUnlockContract.WindowsCngUnattendedProvider && !OperatingSystem.IsWindows()) return;
        var cancellation = TestContext.Current.CancellationToken;
        var directory = Path.Combine(Path.GetTempPath(), "otp-harbor-startup-tests", Guid.NewGuid().ToString("N"));
        var sourceKey = Enumerable.Range(1, 32).Select(value => (byte)value).ToArray();
        var secrets = new MemorySecrets();
        IPlatformUnattendedUnlock unattended = provider == PlatformQuickUnlockContract.WindowsCngUnattendedProvider
            ? new WindowsCngUnattendedUnlock(NullLogger<WindowsCngUnattendedUnlock>.Instance)
            : new SecretStoreUnattendedUnlock(secrets, provider, NullLogger<SecretStoreUnattendedUnlock>.Instance);
        var presence = new Mock<IPlatformQuickUnlock>();
        presence.SetupGet(value => value.ProviderId).Returns(PlatformQuickUnlockContract.WindowsHelloTpmProvider);
        presence.Setup(value => value.TryUnlockAsync(It.IsAny<PlatformQuickUnlockWrapperV2>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Result.Ok(PlatformQuickUnlockAttempt.Successful(SensitiveBuffer.CopyFrom(sourceKey))));
        presence.Setup(value => value.RemoveAsync(It.IsAny<PlatformQuickUnlockWrapperV2>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok());
        var verifier = new Mock<IStoredVaultKeyVerifier>();
        verifier.Setup(value => value.VerifyAsync(It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ReadOnlyMemory<byte> key, CancellationToken _) => Result.Ok(
                key.Span.SequenceEqual(sourceKey) ? VaultKeyVerificationStatus.Verified : VaultKeyVerificationStatus.AuthenticationFailed));
        var password = new Mock<IMasterPasswordService>(MockBehavior.Strict);
        PlatformQuickUnlockWrapperV2? registered = null;
        try
        {
            using (var first = new StartupProcess(directory, unattended, presence.Object, verifier.Object, password.Object))
            {
                Assert.True((await first.Settings.LoadAsync()).IsSuccess);
                first.Settings.Current.PreferredUnlockMethod = PreferredUnlockMethod.PlatformQuickUnlock;
                Assert.True((await first.Settings.SaveAsync()).IsSuccess);
                var initial = AuthorizationEnvelopeV2CodecTests.CreateEnvelope() with
                {
                    QuickUnlockWrapper = new PlatformQuickUnlockWrapperV2
                    {
                        Provider = presence.Object.ProviderId, ProviderVersion = 1,
                        AuthenticationPolicy = PlatformQuickUnlockContract.UserVerificationRequired,
                        KeyReference = "SYNTHETIC_BIOMETRIC_WRAPPER",
                        WrappedKey = new PlatformWrappedKeyV2
                        {
                            Algorithm = PlatformQuickUnlockContract.RsaOaepSha256Algorithm, Ciphertext = new byte[256]
                        }
                    }
                };
                Assert.True((await first.Envelopes.SaveAsync(initial, cancellation)).IsSuccess);
                await first.Authorization.InitializeAsync();
                first.Security.SetDek(sourceKey);
                first.Authorization.State.Unlock();

                var disabled = await first.Authorization.SetAppLockEnabledWithPlatformAsync(false,
                    PreferredUnlockMethod.PlatformQuickUnlock, cancellation);
                var saved = (await first.Envelopes.LoadAsync(cancellation)).Value!;
                registered = saved.QuickUnlockWrapper;
                Assert.Equal(AuthorizationResult.Success, disabled);
                Assert.NotNull(registered);
                Assert.Equal(provider, registered.Provider);
                Assert.False(first.Settings.Current.AppLockEnabled);
                Assert.Equal(PreferredUnlockMethod.Password, first.Authorization.State.PreferredUnlockMethod);
                Assert.Equal(PreferredUnlockMethod.PlatformQuickUnlock, first.Settings.Current.PreferredUnlockMethod);
                presence.Verify(value => value.TryUnlockAsync(It.IsAny<PlatformQuickUnlockWrapperV2>(), It.IsAny<CancellationToken>()), Times.Once);
                if (scenario == "tampered")
                {
                    registered.WrappedKey.Ciphertext[0] ^= 1;
                    Assert.True((await first.Envelopes.SaveAsync(saved, cancellation)).IsSuccess);
                }
            }
            if (scenario == "missing key") Assert.True((await unattended.RemoveAsync(registered!, cancellation)).IsSuccess);
            presence.Invocations.Clear();

            using var restarted = new StartupProcess(directory, unattended, presence.Object, verifier.Object, password.Object);
            var coordinator = new AvaloniaStartupCoordinator(restarted.Settings, restarted.Authorization,
                Mock.Of<IAvaloniaLocalizationService>(), NullLogger<AvaloniaStartupCoordinator>.Instance);
            var outcome = await coordinator.InitializeAsync(cancellation);

            Assert.Equal(scenario switch
            {
                "valid" => AvaloniaStartupOutcome.ReadyUnlocked,
                "missing key" => AvaloniaStartupOutcome.ReadyForUnlock,
                _ => AvaloniaStartupOutcome.ReadyForPasswordFallback
            }, outcome);
            Assert.Equal(scenario == "valid", restarted.Authorization.State.IsUnlocked);
            Assert.Equal(scenario == "valid", restarted.Security.IsUnlocked);
            Assert.Equal(scenario != "valid", restarted.Settings.Current.AppLockEnabled);
            Assert.Equal(PreferredUnlockMethod.PlatformQuickUnlock, restarted.Settings.Current.PreferredUnlockMethod);
            if (scenario == "valid")
            {
                var recovered = restarted.Security.GetDekCopy();
                try { Assert.Equal(sourceKey, recovered); }
                finally { CryptographicOperations.ZeroMemory(recovered); }
            }
            presence.Verify(value => value.TryUnlockAsync(It.IsAny<PlatformQuickUnlockWrapperV2>(), It.IsAny<CancellationToken>()), Times.Never);
            presence.Verify(value => value.RegisterAsync(It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()), Times.Never);
            password.VerifyNoOtherCalls();
        }
        finally
        {
            if (registered is not null) Assert.True((await unattended.RemoveAsync(registered, CancellationToken.None)).IsSuccess);
            foreach (var secret in secrets.Values.Values) CryptographicOperations.ZeroMemory(secret);
            CryptographicOperations.ZeroMemory(sourceKey);
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class StartupProcess : IDisposable
    {
        private readonly AppPreferencesStore _preferences;
        private readonly AuthorizationEnvelopeSession _session;
        private readonly PlatformUnattendedUnlockEnrollment _enrollment;
        public AuthorizationEnvelopeStore Envelopes { get; }
        public PortableSettingsService Settings { get; }
        public SecurityContext Security { get; } = new();
        public PortableAuthorizationService Authorization { get; }

        public StartupProcess(string directory, IPlatformUnattendedUnlock unattended, IPlatformQuickUnlock presence,
            IStoredVaultKeyVerifier verifier, IMasterPasswordService password)
        {
            _preferences = new AppPreferencesStore(Path.Combine(directory, "preferences.json"),
                NullLogger<AppPreferencesStore>.Instance, NoOpPlatformFileSecurity.Instance);
            Envelopes = new AuthorizationEnvelopeStore(Path.Combine(directory, "authorization.json"),
                NullLogger<AuthorizationEnvelopeStore>.Instance, NoOpPlatformFileSecurity.Instance);
            Settings = new PortableSettingsService(_preferences, NullLogger<PortableSettingsService>.Instance);
            _session = new AuthorizationEnvelopeSession(Envelopes, password, verifier, Security,
                [unattended, presence], NullLogger<AuthorizationEnvelopeSession>.Instance);
            _enrollment = new PlatformUnattendedUnlockEnrollment(Envelopes, password, verifier, unattended,
                [unattended, presence], NullLogger<PlatformUnattendedUnlockEnrollment>.Instance);
            Authorization = new PortableAuthorizationService(Settings, _session,
                Mock.Of<IAuthorizationEnvelopePasswordLifecycle>(), Mock.Of<IPlatformQuickUnlockEnrollment>(), presence,
                Mock.Of<IPasswordValidationService>(), Security, new AuthorizationState(),
                NullLogger<PortableAuthorizationService>.Instance, _enrollment);
        }

        public void Dispose()
        {
            _session.Dispose();
            Security.Dispose();
            Settings.Dispose();
            _enrollment.Dispose();
            Envelopes.Dispose();
            _preferences.Dispose();
        }
    }
}
