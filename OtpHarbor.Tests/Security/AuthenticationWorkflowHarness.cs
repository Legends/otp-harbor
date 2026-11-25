using System.Security.Cryptography;
using FluentResults;
using Microsoft.Extensions.Logging.Abstractions;
using TOTP.Core.Enums;
using TOTP.Core.Models;
using TOTP.Core.Security;
using TOTP.Core.Security.Interfaces;
using TOTP.Core.Security.Models;
using TOTP.DAL.Services;
using TOTP.Infrastructure.Security;
using TOTP.Infrastructure.Services;
using TOTP.Tests.Common;

namespace TOTP.Tests.Security;

// Real application services and encrypted files; only device authentication is simulated.
internal sealed class AuthenticationWorkflowHarness : IDisposable
{
    public const string Password = "SyntheticRecoveryPassword2026";
    public string DirectoryPath { get; } = Path.Combine(Path.GetTempPath(), "otp-auth-workflow-" + Guid.NewGuid().ToString("N"));
    public string VaultPath => Path.Combine(DirectoryPath, "vault.bin");
    public string EnvelopePath => Path.Combine(DirectoryPath, "authorization-envelope.bin");
    public DeviceAdapter Biometrics { get; } = new(PlatformQuickUnlockContract.AndroidKeystoreBiometricProvider, PreferredUnlockMethod.PlatformQuickUnlock);
    public DeviceAdapter ScreenLock { get; } = new(PlatformQuickUnlockContract.AndroidKeystoreDeviceCredentialProvider, PreferredUnlockMethod.PlatformDeviceCredential);
    private UnattendedAdapter Unattended { get; } = new();

    public async Task<Process> OpenAsync()
    {
        Directory.CreateDirectory(DirectoryPath);
        var process = new Process(this);
        var loaded = await process.Settings.LoadAsync();
        Assert.True(loaded.IsSuccess);
        await process.Authorization.InitializeAsync();
        return process;
    }

    public async Task<Process> CreateAsync(InitialAppLockChoice choice)
    {
        var process = await OpenAsync();
        Assert.Equal(AuthorizationResult.Success, await process.Authorization.ConfigurePasswordAsync(Password, Password));
        Assert.Equal(AuthorizationResult.Success, await process.Authorization.CompleteInitialAppLockSetupAsync(choice));
        return process;
    }

    public DeviceAdapter Adapter(PreferredUnlockMethod method) =>
        method == PreferredUnlockMethod.PlatformDeviceCredential ? ScreenLock : Biometrics;

    public void Dispose()
    {
        Biometrics.Dispose();
        ScreenLock.Dispose();
        Unattended.Dispose();
        var target = Path.GetFullPath(DirectoryPath);
        var temporaryRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!target.StartsWith(temporaryRoot, StringComparison.OrdinalIgnoreCase)
            || !Path.GetFileName(target).StartsWith("otp-auth-workflow-", StringComparison.Ordinal))
            throw new InvalidOperationException("Unexpected regression-test cleanup path.");
        if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
    }

    internal sealed class Process : IDisposable
    {
        private readonly AppPreferencesStore _preferences;
        private readonly AuthorizationEnvelopeSession _session;
        private readonly AuthorizationEnvelopeActivator _activator;
        private readonly AuthorizationEnvelopePasswordLifecycle _passwordLifecycle;
        private readonly PlatformQuickUnlockEnrollment _quickUnlock;
        private readonly PlatformUnattendedUnlockEnrollment _unattended;
        public SecurityContext Security { get; } = new();
        public PortableSettingsService Settings { get; }
        public AuthorizationEnvelopeStore Envelopes { get; }
        public PortableAuthorizationService Authorization { get; }
        public AccountManager Accounts { get; }

        public Process(AuthenticationWorkflowHarness harness)
        {
            var fileSecurity = NoOpPlatformFileSecurity.Instance;
            _preferences = new AppPreferencesStore(Path.Combine(harness.DirectoryPath, "preferences.json"), NullLogger<AppPreferencesStore>.Instance, fileSecurity);
            Envelopes = new AuthorizationEnvelopeStore(harness.EnvelopePath, NullLogger<AuthorizationEnvelopeStore>.Instance, fileSecurity);
            Settings = new PortableSettingsService(_preferences, NullLogger<PortableSettingsService>.Instance);
            var vault = new VaultService(Security);
            var verifier = new StoredVaultKeyVerifier(harness.VaultPath, vault, NullLogger<StoredVaultKeyVerifier>.Instance, fileSecurity);
            var password = new MasterPasswordService(NullLogger<MasterPasswordService>.Instance);
            var validation = new PasswordValidationService();
            IPlatformQuickUnlock[] adapters = [harness.Biometrics, harness.ScreenLock, harness.Unattended];
            _session = new AuthorizationEnvelopeSession(Envelopes, password, verifier, Security, adapters, NullLogger<AuthorizationEnvelopeSession>.Instance);
            _activator = new AuthorizationEnvelopeActivator(password, verifier, Envelopes, NullLogger<AuthorizationEnvelopeActivator>.Instance);
            _passwordLifecycle = new AuthorizationEnvelopePasswordLifecycle(Envelopes, password, validation, _activator, NullLogger<AuthorizationEnvelopePasswordLifecycle>.Instance);
            _quickUnlock = new PlatformQuickUnlockEnrollment(Envelopes, password, verifier, adapters, NullLogger<PlatformQuickUnlockEnrollment>.Instance);
            _unattended = new PlatformUnattendedUnlockEnrollment(Envelopes, password, verifier, harness.Unattended, adapters, NullLogger<PlatformUnattendedUnlockEnrollment>.Instance);
            Authorization = new PortableAuthorizationService(Settings, _session, _passwordLifecycle, _quickUnlock, harness.Biometrics,
                validation, Security, new AuthorizationState(), NullLogger<PortableAuthorizationService>.Instance, _unattended,
                platformQuickUnlockMethods: adapters);
            Accounts = new AccountManager(new AccountDAL(NullLogger<AccountDAL>.Instance, vault, harness.VaultPath, fileSecurity, harness.EnvelopePath));
        }

        public async Task<AuthorizationResult> ChangeAsync(InitialAppLockChoice choice)
        {
            var current = Authorization.State.PreferredUnlockMethod;
            var enabled = Settings.Current.AppLockEnabled;
            var target = choice switch
            {
                InitialAppLockChoice.StrongBiometrics => PreferredUnlockMethod.PlatformQuickUnlock,
                InitialAppLockChoice.DeviceScreenLock => PreferredUnlockMethod.PlatformDeviceCredential,
                _ => PreferredUnlockMethod.Password
            };
            if (choice == InitialAppLockChoice.AppLockDisabled)
                return !enabled || current == PreferredUnlockMethod.Password
                    ? await Authorization.SetAppLockEnabledAsync(false, Password)
                    : await Authorization.SetAppLockEnabledWithPlatformAsync(false, current);
            if (!enabled)
            {
                var activated = await Authorization.SetAppLockEnabledAsync(true, Password);
                if (activated != AuthorizationResult.Success) return activated;
                current = Authorization.State.PreferredUnlockMethod;
            }
            if (current == target) return await Authorization.SetAppLockEnabledAsync(true, Password);
            return current == PreferredUnlockMethod.Password
                ? await Authorization.ConfigureUnlockMethodAsync(target, Password)
                : await Authorization.ConfigureUnlockMethodWithPlatformAsync(target, current);
        }

        public void Dispose()
        {
            Authorization.Lock();
            _session.Dispose();
            _passwordLifecycle.Dispose();
            _activator.Dispose();
            _quickUnlock.Dispose();
            _unattended.Dispose();
            Envelopes.Dispose();
            Settings.Dispose();
            _preferences.Dispose();
            Security.Dispose();
        }
    }

    internal class DeviceAdapter(string provider, PreferredUnlockMethod method) : IPlatformQuickUnlock, IDisposable
    {
        private readonly byte[] _wrappingKey = RandomNumberGenerator.GetBytes(32);
        private readonly HashSet<string> _references = [];
        public string ProviderId => provider;
        public PreferredUnlockMethod UnlockMethod => method;
        public PlatformQuickUnlockStatus Outcome { get; set; } = PlatformQuickUnlockStatus.Succeeded;
        public PlatformQuickUnlockAvailability Availability { get; set; } = PlatformQuickUnlockAvailability.Available;
        public int AuthenticationCalls { get; private set; }
        public Task<PlatformQuickUnlockAvailability> GetAvailabilityAsync(CancellationToken cancellationToken = default) => Task.FromResult(Availability);

        public Task<Result<PlatformQuickUnlockWrapperV2>> RegisterAsync(ReadOnlyMemory<byte> vaultKey, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (this is not IPlatformUnattendedUnlock) AuthenticationCalls++;
            if (Outcome != PlatformQuickUnlockStatus.Succeeded) return Task.FromResult(Result.Fail<PlatformQuickUnlockWrapperV2>("Synthetic device registration rejected."));
            var nonce = RandomNumberGenerator.GetBytes(12);
            var ciphertext = new byte[48];
            using (var aes = new AesGcm(_wrappingKey, 16)) aes.Encrypt(nonce, vaultKey.Span, ciphertext.AsSpan(0, 32), ciphertext.AsSpan(32));
            var reference = Guid.NewGuid().ToString("N");
            _references.Add(reference);
            return Task.FromResult(Result.Ok(new PlatformQuickUnlockWrapperV2
            {
                Provider = ProviderId, ProviderVersion = 1, KeyReference = reference,
                AuthenticationPolicy = this is IPlatformUnattendedUnlock ? PlatformQuickUnlockContract.UserVerificationNotRequired : PlatformQuickUnlockContract.UserVerificationRequired,
                WrappedKey = new PlatformWrappedKeyV2 { Algorithm = PlatformQuickUnlockContract.AndroidAes256GcmAlgorithm, Nonce = nonce, Ciphertext = ciphertext }
            }));
        }

        public Task<Result<PlatformQuickUnlockAttempt>> TryUnlockAsync(PlatformQuickUnlockWrapperV2 wrapper, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (this is not IPlatformUnattendedUnlock) AuthenticationCalls++;
            if (Outcome != PlatformQuickUnlockStatus.Succeeded) return Task.FromResult(Result.Ok(PlatformQuickUnlockAttempt.WithoutKey(Outcome)));
            if (!_references.Contains(wrapper.KeyReference)) return Task.FromResult(Result.Ok(PlatformQuickUnlockAttempt.WithoutKey(PlatformQuickUnlockStatus.KeyNotFound)));
            var key = new byte[32];
            try
            {
                using var aes = new AesGcm(_wrappingKey, 16);
                aes.Decrypt(wrapper.WrappedKey.Nonce!, wrapper.WrappedKey.Ciphertext.AsSpan(0, 32), wrapper.WrappedKey.Ciphertext.AsSpan(32), key);
                return Task.FromResult(Result.Ok(PlatformQuickUnlockAttempt.Successful(SensitiveBuffer.CopyFrom(key))));
            }
            finally { CryptographicOperations.ZeroMemory(key); }
        }

        public Task<Result> RemoveAsync(PlatformQuickUnlockWrapperV2 wrapper, CancellationToken cancellationToken = default)
        {
            _references.Remove(wrapper.KeyReference);
            return Task.FromResult(Result.Ok());
        }
        public void Dispose() { CryptographicOperations.ZeroMemory(_wrappingKey); _references.Clear(); }
    }

    private sealed class UnattendedAdapter() : DeviceAdapter(PlatformQuickUnlockContract.AndroidKeystoreUnattendedProvider,
        PreferredUnlockMethod.Password), IPlatformUnattendedUnlock { }
}
