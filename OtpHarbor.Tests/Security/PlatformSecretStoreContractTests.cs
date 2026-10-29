using FluentResults;
using TOTP.Core.Security.Interfaces;
using TOTP.Core.Security.Models;

namespace TOTP.Tests.Security;

public sealed class PlatformSecretStoreContractTests
{
    [Fact]
    public void Availability_DefaultValue_IsNotAvailable()
    {
        Assert.Equal(PlatformSecretStoreAvailability.Unknown, default);
        Assert.NotEqual(
            PlatformSecretStoreAvailability.Available,
            default(PlatformSecretStoreAvailability));
    }

    [Fact]
    public async Task StoreAndRetrieve_ReturnsAnIndependentCallerOwnedBuffer()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var provider = new InMemoryPlatformSecretStore();
        IPlatformSecretStore store = provider;
        var source = new byte[] { 1, 2, 3, 4 };

        var stored = await store.StoreAsync("quick-unlock-key", source, cancellationToken);
        source.AsSpan().Clear();
        var retrieved = await store.RetrieveAsync("quick-unlock-key", cancellationToken);

        Assert.True(stored.IsSuccess);
        Assert.True(retrieved.IsSuccess);
        using var secret = Assert.IsType<SensitiveBuffer>(retrieved.Value);
        Assert.Equal([1, 2, 3, 4], secret.Memory.ToArray());
    }

    [Fact]
    public async Task Retrieve_WhenReferenceIsAbsent_ReturnsSuccessfulNull()
    {
        using var provider = new InMemoryPlatformSecretStore();
        IPlatformSecretStore store = provider;

        var result = await store.RetrieveAsync(
            "missing",
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task Delete_IsIdempotentAndRemovesStoredSecret()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var provider = new InMemoryPlatformSecretStore();
        IPlatformSecretStore store = provider;
        await store.StoreAsync("quick-unlock-key", new byte[] { 1, 2, 3 }, cancellationToken);

        var firstDelete = await store.DeleteAsync("quick-unlock-key", cancellationToken);
        var secondDelete = await store.DeleteAsync("quick-unlock-key", cancellationToken);
        var retrieved = await store.RetrieveAsync("quick-unlock-key", cancellationToken);

        Assert.True(firstDelete.IsSuccess);
        Assert.True(secondDelete.IsSuccess);
        Assert.True(retrieved.IsSuccess);
        Assert.Null(retrieved.Value);
    }

    [Fact]
    public void SensitiveBuffer_Dispose_ClearsOwnedMemoryAndPreventsReuse()
    {
        var secret = SensitiveBuffer.CopyFrom(new byte[] { 1, 2, 3, 4 });
        var retainedView = secret.Memory;

        secret.Dispose();

        Assert.All(retainedView.ToArray(), value => Assert.Equal(0, value));
        Assert.Throws<ObjectDisposedException>(() => _ = secret.Memory);
        secret.Dispose();
    }

    [Fact]
    public void PlatformSecretStoreError_PreservesTypedCodeWithoutSecretData()
    {
        var error = new PlatformSecretStoreError(
            PlatformSecretStoreErrorCode.AccessDenied,
            "Platform secret access was denied.");

        Assert.Equal(PlatformSecretStoreErrorCode.AccessDenied, error.Code);
        Assert.Equal("Platform secret access was denied.", error.Message);
    }

    private sealed class InMemoryPlatformSecretStore : IPlatformSecretStore, IDisposable
    {
        private readonly Dictionary<string, byte[]> _secrets = new(StringComparer.Ordinal);

        public string ProviderId => "test-memory-store";

        public Task<PlatformSecretStoreAvailability> GetAvailabilityAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(PlatformSecretStoreAvailability.Available);
        }

        public Task<Result> StoreAsync(
            string secretReference,
            ReadOnlyMemory<byte> secret,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_secrets.Remove(secretReference, out var previous))
            {
                previous.AsSpan().Clear();
            }

            _secrets[secretReference] = secret.ToArray();
            return Task.FromResult(Result.Ok());
        }

        public Task<Result<SensitiveBuffer?>> RetrieveAsync(
            string secretReference,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SensitiveBuffer? result = _secrets.TryGetValue(secretReference, out var value)
                ? SensitiveBuffer.CopyFrom(value)
                : null;
            return Task.FromResult(Result.Ok(result));
        }

        public Task<Result> DeleteAsync(
            string secretReference,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_secrets.Remove(secretReference, out var removed))
            {
                removed.AsSpan().Clear();
            }

            return Task.FromResult(Result.Ok());
        }

        public void Dispose()
        {
            foreach (var secret in _secrets.Values)
            {
                secret.AsSpan().Clear();
            }

            _secrets.Clear();
        }
    }
}
