using System.Text.Json;
using FluentResults;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TOTP.Core.Security.Interfaces;
using TOTP.Core.Security;
using TOTP.Core.Security.Models;
using TOTP.Infrastructure.Security;

namespace TOTP.Tests.Security;

public sealed partial class DesktopUnattendedUnlockTests
{
    [Theory]
    [InlineData(PlatformQuickUnlockContract.MacOSUnattendedProvider)]
    [InlineData(PlatformQuickUnlockContract.LinuxUnattendedProvider)]
    public async Task SecretStoreEnrollment_RestartsWithoutPromptAndRemovalRevokesAccess(string provider)
    {
        var secrets = new MemorySecrets();
        var adapter = new SecretStoreUnattendedUnlock(secrets, provider, NullLogger<SecretStoreUnattendedUnlock>.Instance);
        var store = new EnvelopeStore();
        var password = new Mock<IMasterPasswordService>();
        password.Setup(value => value.UnwrapKeyV2Async(It.IsAny<PasswordKeyWrapperV2>(), "synthetic", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Enumerable.Repeat((byte)42, 32).ToArray());
        var verifier = new Mock<IStoredVaultKeyVerifier>();
        verifier.Setup(value => value.VerifyAsync(It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok(VaultKeyVerificationStatus.Verified));
        using var enrollment = new PlatformUnattendedUnlockEnrollment(store, password.Object, verifier.Object, adapter,
            [adapter], NullLogger<PlatformUnattendedUnlockEnrollment>.Instance);
        Assert.True((await enrollment.EnableAsync("synthetic", TestContext.Current.CancellationToken)).IsSuccess);
        var wrapper = store.Snapshot!.QuickUnlockWrapper!;
        Assert.True(PlatformQuickUnlockContract.IsSupported(wrapper));
        Assert.True(PlatformQuickUnlockContract.IsSupportedUnattendedWrapper(wrapper));
        Assert.Null(PlatformQuickUnlockContract.GetUnlockMethod(wrapper));
        var security = new Mock<ISecurityContext>();
        byte[]? installed = null;
        security.Setup(value => value.SetDek(It.IsAny<byte[]>())).Callback((byte[] key) => installed = key.ToArray());
        using var session = new AuthorizationEnvelopeSession(store, password.Object, verifier.Object, security.Object,
            [adapter], NullLogger<AuthorizationEnvelopeSession>.Instance);
        Assert.True((await session.InitializeAsync(TestContext.Current.CancellationToken)).Value.HasUnattendedUnlock);
        Assert.Equal(AuthorizationResult.Success, (await session.TryUnlockWithPlatformAsync(TestContext.Current.CancellationToken)).Value);
        Assert.Equal(Enumerable.Repeat((byte)42, 32), installed);
        Assert.True((await enrollment.DisableAsync(TestContext.Current.CancellationToken)).IsSuccess);
        Assert.Null(store.Snapshot.QuickUnlockWrapper);
        Assert.Empty(secrets.Values);
        using var missing = (await adapter.TryUnlockAsync(wrapper, TestContext.Current.CancellationToken)).Value;
        Assert.Equal(PlatformQuickUnlockStatus.KeyNotFound, missing.Status);
        Assert.NotNull(store.Snapshot.PasswordWrapper);
    }

    [Fact]
    public async Task SecretStoreAdapter_TamperAndMalformedReferencesCannotRecoverAKey()
    {
        var secrets = new MemorySecrets();
        var adapter = new SecretStoreUnattendedUnlock(secrets, PlatformQuickUnlockContract.LinuxUnattendedProvider,
            NullLogger<SecretStoreUnattendedUnlock>.Instance);
        var wrapper = (await adapter.RegisterAsync(new byte[32], TestContext.Current.CancellationToken)).Value;
        var reads = secrets.Reads;
        var tampered = wrapper with { KeyReference = "other-app-secret" };
        Assert.False((await adapter.TryUnlockAsync(tampered, TestContext.Current.CancellationToken)).IsSuccess);
        Assert.False((await adapter.RemoveAsync(tampered, TestContext.Current.CancellationToken)).IsSuccess);
        Assert.Equal(reads, secrets.Reads);
        wrapper.WrappedKey.Ciphertext[0] ^= 1;
        Assert.False((await adapter.TryUnlockAsync(wrapper, TestContext.Current.CancellationToken)).IsSuccess);
        Assert.Equal(reads + 1, secrets.Reads);
        Assert.All(secrets.LastReturned.ToArray(), value => Assert.Equal(0, value));
        Assert.Single(secrets.Values);
    }

    [Theory]
    [InlineData(PlatformQuickUnlockContract.WindowsCngUnattendedProvider, PlatformQuickUnlockContract.RsaOaepSha256Algorithm, 256)]
    [InlineData(PlatformQuickUnlockContract.MacOSUnattendedProvider, AesGcmWrappedKeyV2.AlgorithmIdentifier, 48)]
    [InlineData(PlatformQuickUnlockContract.LinuxUnattendedProvider, AesGcmWrappedKeyV2.AlgorithmIdentifier, 48)]
    public void Contract_DesktopProvidersHaveStrictUnattendedOnlyMetadata(string provider, string algorithm, int size)
    {
        var wrapper = new PlatformQuickUnlockWrapperV2
        {
            Provider = provider, ProviderVersion = 1,
            AuthenticationPolicy = PlatformQuickUnlockContract.UserVerificationNotRequired,
            KeyReference = PlatformQuickUnlockContract.DesktopUnattendedReferencePrefix + Guid.NewGuid().ToString("N"),
            WrappedKey = new PlatformWrappedKeyV2 { Algorithm = algorithm, Nonce = size == 48 ? new byte[12] : null, Ciphertext = new byte[size] }
        };
        Assert.True(PlatformQuickUnlockContract.IsSupportedUnattendedWrapper(wrapper));
        var serialized = AuthorizationEnvelopeV2Codec.Serialize(AuthorizationEnvelopeV2CodecTests.CreateEnvelope() with { QuickUnlockWrapper = wrapper });
        Assert.True(serialized.IsSuccess);
        Assert.True(AuthorizationEnvelopeV2Codec.Deserialize(serialized.Value).IsSuccess);
        Assert.Null(PlatformQuickUnlockContract.GetUnlockMethod(wrapper));
        Assert.False(PlatformQuickUnlockContract.IsSupported(wrapper with { AuthenticationPolicy = PlatformQuickUnlockContract.UserVerificationRequired }));
        Assert.False(PlatformQuickUnlockContract.IsSupported(wrapper with { ProviderVersion = 2 }));
        Assert.False(PlatformQuickUnlockContract.IsSupported(wrapper with { KeyReference = "arbitrary-reference" }));
        Assert.False(PlatformQuickUnlockContract.IsSupported(wrapper with { WrappedKey = wrapper.WrappedKey with { Nonce = new byte[11] } }));
        Assert.False(PlatformQuickUnlockContract.IsSupported(wrapper with { WrappedKey = wrapper.WrappedKey with { Ciphertext = new byte[size - 1] } }));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SecretStore_FailedOrCancelledRegistrationRemovesPartialSecret(bool cancel)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var secrets = new MemorySecrets { FailStore = !cancel };
        if (cancel) secrets.AfterStore = lifetime.Cancel;
        var adapter = new SecretStoreUnattendedUnlock(secrets, PlatformQuickUnlockContract.LinuxUnattendedProvider,
            NullLogger<SecretStoreUnattendedUnlock>.Instance);
        if (cancel)
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => adapter.RegisterAsync(new byte[32], lifetime.Token));
        else
            Assert.False((await adapter.RegisterAsync(new byte[32], lifetime.Token)).IsSuccess);
        Assert.Empty(secrets.Values);
    }

    [Fact]
    public async Task SecretStore_InvalidRecoveredKeyIsClearedAndNotReturned()
    {
        var secrets = new MemorySecrets();
        var adapter = new SecretStoreUnattendedUnlock(secrets, PlatformQuickUnlockContract.LinuxUnattendedProvider,
            NullLogger<SecretStoreUnattendedUnlock>.Instance);
        var wrapper = (await adapter.RegisterAsync(new byte[32], TestContext.Current.CancellationToken)).Value;
        secrets.Values[wrapper.KeyReference] = Enumerable.Repeat((byte)7, 16).ToArray();
        Assert.False((await adapter.TryUnlockAsync(wrapper, TestContext.Current.CancellationToken)).IsSuccess);
        Assert.All(secrets.LastReturned.ToArray(), value => Assert.Equal(0, value));
    }

    [Fact]
    public async Task SecretStore_CleanupFailureLeavesRandomWrappingMaterialInsteadOfVaultKey()
    {
        var secrets = new MemorySecrets { FailStore = true, FailDelete = true };
        var adapter = new SecretStoreUnattendedUnlock(secrets, PlatformQuickUnlockContract.LinuxUnattendedProvider,
            NullLogger<SecretStoreUnattendedUnlock>.Instance);
        var vaultKey = Enumerable.Repeat((byte)42, 32).ToArray();
        Assert.False((await adapter.RegisterAsync(vaultKey, TestContext.Current.CancellationToken)).IsSuccess);
        Assert.NotEqual(vaultKey, Assert.Single(secrets.Values).Value);
        Assert.All(secrets.LastStoredInput.ToArray(), value => Assert.Equal(0, value));
        Assert.All(vaultKey, value => Assert.Equal(42, value));
    }

    [Fact]
    public async Task SecretStore_AuthenticatedCiphertextCannotMoveBetweenProviders()
    {
        var secrets = new MemorySecrets();
        var mac = new SecretStoreUnattendedUnlock(secrets, PlatformQuickUnlockContract.MacOSUnattendedProvider,
            NullLogger<SecretStoreUnattendedUnlock>.Instance);
        var linux = new SecretStoreUnattendedUnlock(secrets, PlatformQuickUnlockContract.LinuxUnattendedProvider,
            NullLogger<SecretStoreUnattendedUnlock>.Instance);
        var wrapper = (await mac.RegisterAsync(new byte[32], TestContext.Current.CancellationToken)).Value;
        Assert.False((await linux.TryUnlockAsync(wrapper with { Provider = linux.ProviderId }, TestContext.Current.CancellationToken)).IsSuccess);
    }

    [Fact]
    public async Task AuthenticatedEnrollmentNeverSelectsAnUnattendedAdapter()
    {
        var platform = new Mock<IPlatformUnattendedUnlock>();
        using var enrollment = new PlatformQuickUnlockEnrollment(Mock.Of<IAuthorizationEnvelopeStore>(),
            Mock.Of<IMasterPasswordService>(), Mock.Of<IStoredVaultKeyVerifier>(), [platform.Object],
            NullLogger<PlatformQuickUnlockEnrollment>.Instance);
        Assert.Equal(PlatformQuickUnlockAvailability.NotSupported, await enrollment.GetAvailabilityAsync(
            TOTP.Core.Enums.PreferredUnlockMethod.PlatformQuickUnlock, TestContext.Current.CancellationToken));
        platform.VerifyNoOtherCalls();
    }

    private sealed class EnvelopeStore : IAuthorizationEnvelopeStore
    {
        public AuthorizationEnvelopeV2? Snapshot = AuthorizationEnvelopeV2CodecTests.CreateEnvelope();
        public Task<Result<AuthorizationEnvelopeV2?>> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Ok<AuthorizationEnvelopeV2?>(Clone(Snapshot!)));
        public Task<Result> SaveAsync(AuthorizationEnvelopeV2 envelope, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Snapshot = Clone(envelope);
            return Task.FromResult(Result.Ok());
        }
        public void Dispose() { }
        private static AuthorizationEnvelopeV2 Clone(AuthorizationEnvelopeV2 envelope) =>
            JsonSerializer.Deserialize<AuthorizationEnvelopeV2>(JsonSerializer.Serialize(envelope))!;
    }

    private sealed class MemorySecrets : IPlatformSecretStore
    {
        public Dictionary<string, byte[]> Values { get; } = [];
        public int Reads;
        public bool FailStore;
        public bool FailDelete;
        public Action? AfterStore;
        public ReadOnlyMemory<byte> LastReturned;
        public ReadOnlyMemory<byte> LastStoredInput;
        public string ProviderId => "synthetic-os-secret-store";
        public Task<PlatformSecretStoreAvailability> GetAvailabilityAsync(CancellationToken cancellationToken = default) => Task.FromResult(PlatformSecretStoreAvailability.Available);
        public Task<Result> StoreAsync(string reference, ReadOnlyMemory<byte> secret, CancellationToken cancellationToken = default)
        {
            LastStoredInput = secret;
            Values[reference] = secret.ToArray();
            AfterStore?.Invoke();
            return Task.FromResult(FailStore ? Result.Fail("synthetic store failure") : Result.Ok());
        }
        public Task<Result<SensitiveBuffer?>> RetrieveAsync(string reference, CancellationToken cancellationToken = default)
        {
            Reads++;
            var result = Values.TryGetValue(reference, out var bytes) ? SensitiveBuffer.CopyFrom(bytes) : null;
            if (result is not null) LastReturned = result.Memory;
            return Task.FromResult(Result.Ok(result));
        }
        public Task<Result> DeleteAsync(string reference, CancellationToken cancellationToken = default)
        {
            if (FailDelete) return Task.FromResult(Result.Fail("synthetic cleanup failure"));
            if (Values.Remove(reference, out var bytes)) Array.Clear(bytes);
            return Task.FromResult(Result.Ok());
        }
    }
}
