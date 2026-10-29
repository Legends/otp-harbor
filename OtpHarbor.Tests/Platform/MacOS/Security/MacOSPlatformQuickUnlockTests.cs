using FluentResults;
using Microsoft.Extensions.Logging;
using Moq;
using TOTP.Core.Security.Interfaces;
using TOTP.Core.Security.Models;
using TOTP.Platform.MacOS.Security;

namespace TOTP.Tests.Platform.MacOS.Security;

public sealed class MacOSPlatformQuickUnlockTests
{
    [Fact]
    public async Task RegisterAndUnlock_StoresKeychainReferenceAndReturnsOwnedVaultKey()
    {
        using var store = new MemorySecretStore();
        var sut = CreateSut(store);
        var key = Enumerable.Range(1, 32).Select(value => (byte)value).ToArray();
        var expectedKey = key.ToArray();

        var registered = await sut.RegisterAsync(key, TestContext.Current.CancellationToken);
        key.AsSpan().Clear();
        var unlocked = await sut.TryUnlockAsync(
            registered.Value,
            TestContext.Current.CancellationToken);

        Assert.True(registered.IsSuccess);
        Assert.True(PlatformQuickUnlockContract.IsSupported(registered.Value));
        Assert.Equal(PlatformQuickUnlockContract.MacOSKeychainProvider, registered.Value.Provider);
        Assert.False(registered.Value.WrappedKey.Ciphertext.SequenceEqual(expectedKey));
        Assert.True(unlocked.IsSuccess);
        using var attempt = unlocked.Value;
        Assert.True(attempt.IsSuccess);
        Assert.Equal(expectedKey, attempt.VaultKey!.Memory.ToArray());
    }

    [Fact]
    public async Task TryUnlockAsync_WhenBindingIsTampered_RejectsBeforeKeychainRead()
    {
        using var store = new MemorySecretStore();
        var sut = CreateSut(store);
        var registered = await sut.RegisterAsync(new byte[32], TestContext.Current.CancellationToken);
        var tampered = registered.Value with
        {
            WrappedKey = registered.Value.WrappedKey with
            {
                Ciphertext = registered.Value.WrappedKey.Ciphertext.ToArray()
            }
        };
        tampered.WrappedKey.Ciphertext[0] ^= 0xff;
        store.RetrieveCount = 0;

        var result = await sut.TryUnlockAsync(tampered, TestContext.Current.CancellationToken);

        Assert.True(result.IsFailed);
        Assert.Equal(0, store.RetrieveCount);
    }

    [Fact]
    public async Task TryUnlockAsync_WhenKeychainItemIsMissing_RequiresRecoveryWithoutKey()
    {
        using var store = new MemorySecretStore();
        var sut = CreateSut(store);
        var registered = await sut.RegisterAsync(new byte[32], TestContext.Current.CancellationToken);
        await store.DeleteAsync(registered.Value.KeyReference, TestContext.Current.CancellationToken);

        var result = await sut.TryUnlockAsync(registered.Value, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(PlatformQuickUnlockStatus.KeyNotFound, result.Value.Status);
        Assert.Null(result.Value.VaultKey);
    }

    [Fact]
    public async Task RemoveAsync_IsIdempotent()
    {
        using var store = new MemorySecretStore();
        var sut = CreateSut(store);
        var registered = await sut.RegisterAsync(new byte[32], TestContext.Current.CancellationToken);

        var first = await sut.RemoveAsync(registered.Value, TestContext.Current.CancellationToken);
        var second = await sut.RemoveAsync(registered.Value, TestContext.Current.CancellationToken);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
    }

    private static MacOSPlatformQuickUnlock CreateSut(IPlatformSecretStore store) =>
        new(store, Mock.Of<ILogger<MacOSPlatformQuickUnlock>>());

    private sealed class MemorySecretStore : IPlatformSecretStore, IDisposable
    {
        private readonly Dictionary<string, byte[]> _values = new(StringComparer.Ordinal);
        public int RetrieveCount { get; set; }
        public string ProviderId => PlatformQuickUnlockContract.MacOSKeychainProvider;

        public Task<PlatformSecretStoreAvailability> GetAvailabilityAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(PlatformSecretStoreAvailability.Available);

        public Task<Result> StoreAsync(
            string secretReference,
            ReadOnlyMemory<byte> secret,
            CancellationToken cancellationToken = default)
        {
            _values[secretReference] = secret.ToArray();
            return Task.FromResult(Result.Ok());
        }

        public Task<Result<SensitiveBuffer?>> RetrieveAsync(
            string secretReference,
            CancellationToken cancellationToken = default)
        {
            RetrieveCount++;
            SensitiveBuffer? value = _values.TryGetValue(secretReference, out var bytes)
                ? SensitiveBuffer.CopyFrom(bytes)
                : null;
            return Task.FromResult(Result.Ok(value));
        }

        public Task<Result> DeleteAsync(
            string secretReference,
            CancellationToken cancellationToken = default)
        {
            if (_values.Remove(secretReference, out var bytes)) bytes.AsSpan().Clear();
            return Task.FromResult(Result.Ok());
        }

        public void Dispose()
        {
            foreach (var value in _values.Values) value.AsSpan().Clear();
            _values.Clear();
        }
    }
}
