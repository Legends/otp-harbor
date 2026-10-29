using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using TOTP.Core.Security.Models;
using TOTP.DAL.Services;
using TOTP.Tests.Common;
using TOTP.Tests.Security;

namespace TOTP.Tests.Integration;

public sealed class AuthorizationEnvelopeStoreTests
{
    [Fact]
    public async Task LoadAsync_WhenFileIsMissing_ReturnsSuccessfulNull()
    {
        using var temp = new TempDir();
        using var store = CreateStore(Path.Combine(temp.Path, "authorization-envelope.bin"));

        var result = await store.LoadAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task SaveThenLoad_RoundTripsPortablePlainJsonEnvelope()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var temp = new TempDir();
        var path = Path.Combine(temp.Path, "authorization-envelope.bin");
        using var store = CreateStore(path);

        var saved = await store.SaveAsync(AuthorizationEnvelopeV2CodecTests.CreateEnvelope(), cancellationToken);
        var loaded = await store.LoadAsync(cancellationToken);
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);

        Assert.True(saved.IsSuccess);
        Assert.True(loaded.IsSuccess);
        Assert.NotNull(loaded.Value);
        Assert.Equal((byte)'{', bytes.First(value => !char.IsWhiteSpace((char)value)));
        Assert.Contains("totp-authorization-envelope", Encoding.UTF8.GetString(bytes), StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadAsync_WhenPayloadIsMalformed_ReturnsTypedFailure()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var temp = new TempDir();
        var path = Path.Combine(temp.Path, "authorization-envelope.bin");
        await File.WriteAllTextAsync(path, "{not-json", cancellationToken);
        using var store = CreateStore(path);

        var result = await store.LoadAsync(cancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(AuthorizationEnvelopeErrorCode.Malformed, AuthorizationEnvelopeV2CodecTests.ErrorCode(result.Errors));
    }

    [Fact]
    public async Task LoadAsync_WhenPayloadExceedsLimit_ReturnsTypedFailure()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var temp = new TempDir();
        var path = Path.Combine(temp.Path, "authorization-envelope.bin");
        await File.WriteAllBytesAsync(
            path,
            new byte[TOTP.Core.Security.AuthorizationEnvelopeV2Codec.MaximumPayloadSize + 1],
            cancellationToken);
        using var store = CreateStore(path);

        var result = await store.LoadAsync(cancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(AuthorizationEnvelopeErrorCode.TooLarge, AuthorizationEnvelopeV2CodecTests.ErrorCode(result.Errors));
    }

    [Fact]
    public async Task SaveAsync_WhenStagedFileCannotBeHardened_PreservesExistingEnvelope()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var temp = new TempDir();
        var path = Path.Combine(temp.Path, "authorization-envelope.bin");
        var original = Encoding.UTF8.GetBytes("original");
        await File.WriteAllBytesAsync(path, original, cancellationToken);
        using var store = new AuthorizationEnvelopeStore(
            path,
            NullLogger<AuthorizationEnvelopeStore>.Instance,
            new DelegatingPlatformFileSecurity
            {
                RestrictFile = filePath =>
                {
                    if (filePath.EndsWith(".tmp", StringComparison.Ordinal))
                        throw new UnauthorizedAccessException("denied");
                }
            });

        var result = await store.SaveAsync(AuthorizationEnvelopeV2CodecTests.CreateEnvelope(), cancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(AuthorizationEnvelopeErrorCode.WriteAccessDenied, AuthorizationEnvelopeV2CodecTests.ErrorCode(result.Errors));
        Assert.Equal(original, await File.ReadAllBytesAsync(path, cancellationToken));
        Assert.Empty(Directory.GetFiles(temp.Path, "authorization-envelope.bin.*.tmp"));
    }

    [Fact]
    public async Task SaveAsync_WhenReplacingEnvelope_PreservesExactlyOnePreviousEnvelope()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var temp = new TempDir();
        var path = Path.Combine(temp.Path, "authorization-envelope.bin");
        var backupPath = $"{path}.previous";
        using var store = CreateStore(path);
        var first = CreateEnvelope(1);
        var second = CreateEnvelope(2);
        var third = CreateEnvelope(3);

        Assert.True((await store.SaveAsync(first, cancellationToken)).IsSuccess);
        Assert.False(File.Exists(backupPath));

        Assert.True((await store.SaveAsync(second, cancellationToken)).IsSuccess);
        await AssertEnvelopeCiphertextAsync(path, 2, cancellationToken);
        await AssertEnvelopeCiphertextAsync(backupPath, 1, cancellationToken);

        Assert.True((await store.SaveAsync(third, cancellationToken)).IsSuccess);
        await AssertEnvelopeCiphertextAsync(path, 3, cancellationToken);
        await AssertEnvelopeCiphertextAsync(backupPath, 2, cancellationToken);
        Assert.Single(Directory.GetFiles(temp.Path, "authorization-envelope.bin.previous"));
    }

    [Fact]
    public async Task SaveAsync_WhenPostCommitHardeningFails_RollsBackExistingEnvelope()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var temp = new TempDir();
        var path = Path.Combine(temp.Path, "authorization-envelope.bin");
        var first = CreateEnvelope(1);
        using (var initialStore = CreateStore(path))
        {
            Assert.True((await initialStore.SaveAsync(first, cancellationToken)).IsSuccess);
        }

        using var failingStore = new AuthorizationEnvelopeStore(
            path,
            NullLogger<AuthorizationEnvelopeStore>.Instance,
            new DelegatingPlatformFileSecurity
            {
                RestrictFile = filePath =>
                {
                    if (string.Equals(filePath, path, StringComparison.Ordinal))
                        throw new UnauthorizedAccessException("denied after commit");
                }
            });

        var result = await failingStore.SaveAsync(CreateEnvelope(2), cancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(
            AuthorizationEnvelopeErrorCode.WriteAccessDenied,
            AuthorizationEnvelopeV2CodecTests.ErrorCode(result.Errors));
        await AssertEnvelopeCiphertextAsync(path, 1, cancellationToken);
        await AssertEnvelopeCiphertextAsync($"{path}.previous", 1, cancellationToken);
        Assert.Empty(Directory.GetFiles(temp.Path, "authorization-envelope.bin.*.tmp"));
        Assert.Empty(Directory.GetFiles(temp.Path, "authorization-envelope.bin.*.rollback"));
    }

    [Fact]
    public async Task SaveAsync_WhenFirstCommitHardeningFails_RemovesFailedEnvelope()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var temp = new TempDir();
        var path = Path.Combine(temp.Path, "authorization-envelope.bin");
        using var store = new AuthorizationEnvelopeStore(
            path,
            NullLogger<AuthorizationEnvelopeStore>.Instance,
            new DelegatingPlatformFileSecurity
            {
                RestrictFile = filePath =>
                {
                    if (string.Equals(filePath, path, StringComparison.Ordinal))
                        throw new UnauthorizedAccessException("denied after commit");
                }
            });

        var result = await store.SaveAsync(CreateEnvelope(1), cancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(
            AuthorizationEnvelopeErrorCode.WriteAccessDenied,
            AuthorizationEnvelopeV2CodecTests.ErrorCode(result.Errors));
        Assert.False(File.Exists(path));
        Assert.False(File.Exists($"{path}.previous"));
        Assert.Empty(Directory.GetFiles(temp.Path, "authorization-envelope.bin.*.tmp"));
    }

    private static AuthorizationEnvelopeV2 CreateEnvelope(byte marker)
    {
        var envelope = AuthorizationEnvelopeV2CodecTests.CreateEnvelope();
        return envelope with
        {
            PasswordWrapper = envelope.PasswordWrapper with
            {
                WrappedKey = envelope.PasswordWrapper.WrappedKey with
                {
                    Ciphertext = Enumerable.Repeat(marker, 48).ToArray()
                }
            }
        };
    }

    private static async Task AssertEnvelopeCiphertextAsync(
        string path,
        byte marker,
        CancellationToken cancellationToken)
    {
        using var store = CreateStore(path);
        var loaded = await store.LoadAsync(cancellationToken);

        Assert.True(loaded.IsSuccess);
        Assert.Equal(Enumerable.Repeat(marker, 48), loaded.Value?.PasswordWrapper.WrappedKey.Ciphertext);
    }

    private static AuthorizationEnvelopeStore CreateStore(string path) =>
        new(path, NullLogger<AuthorizationEnvelopeStore>.Instance, NoOpPlatformFileSecurity.Instance);

    private sealed class TempDir : IDisposable
    {
        public TempDir()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"totp-envelope-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
            }
            catch
            {
                // Best-effort test cleanup.
            }
        }
    }
}
