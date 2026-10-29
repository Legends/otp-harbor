using Microsoft.Extensions.Logging.Abstractions;
using TOTP.Core.Models;
using TOTP.DAL.Services;
using TOTP.Tests.Common;
using TOTP.Tests.Models;

namespace TOTP.Tests.Integration;

public sealed class AppPreferencesStoreTests
{
    [Fact]
    public async Task LoadAsync_WhenMissing_ReturnsSuccessfulNull()
    {
        using var temp = new TempDir();
        using var store = CreateStore(Path.Combine(temp.Path, "preferences.json"));

        var result = await store.LoadAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task SaveThenLoad_RoundTripsPreferencesAsJson()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var temp = new TempDir();
        var path = Path.Combine(temp.Path, "preferences.json");
        using var store = CreateStore(path);
        var preferences = AppPreferencesV1CodecTests.CreatePreferences();

        var saved = await store.SaveAsync(preferences, cancellationToken);
        var loaded = await store.LoadAsync(cancellationToken);

        Assert.True(saved.IsSuccess);
        Assert.True(loaded.IsSuccess);
        Assert.Equal(preferences, loaded.Value);
        Assert.StartsWith("{", (await File.ReadAllTextAsync(path, cancellationToken)).TrimStart(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadAsync_WhenOversized_ReturnsTypedFailure()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var temp = new TempDir();
        var path = Path.Combine(temp.Path, "preferences.json");
        await File.WriteAllBytesAsync(path, new byte[AppPreferencesV1Codec.MaximumPayloadSize + 1], cancellationToken);
        using var store = CreateStore(path);

        var result = await store.LoadAsync(cancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(AppPreferencesErrorCode.TooLarge, AppPreferencesV1CodecTests.ErrorCode(result.Errors));
    }

    [Fact]
    public async Task SaveAsync_WhenStagedFileCannotBeHardened_PreservesExistingPreferences()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var temp = new TempDir();
        var path = Path.Combine(temp.Path, "preferences.json");
        var original = AppPreferencesV1Codec.Serialize(AppPreferencesV1CodecTests.CreatePreferences()).Value;
        await File.WriteAllBytesAsync(path, original, cancellationToken);
        using var store = new AppPreferencesStore(
            path,
            NullLogger<AppPreferencesStore>.Instance,
            new DelegatingPlatformFileSecurity
            {
                RestrictFile = filePath =>
                {
                    if (filePath.EndsWith(".tmp", StringComparison.Ordinal))
                        throw new UnauthorizedAccessException("denied");
                }
            });

        var result = await store.SaveAsync(
            AppPreferencesV1CodecTests.CreatePreferences() with { CultureName = "en-US" },
            cancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(AppPreferencesErrorCode.WriteAccessDenied, AppPreferencesV1CodecTests.ErrorCode(result.Errors));
        Assert.Equal(original, await File.ReadAllBytesAsync(path, cancellationToken));
        Assert.Empty(Directory.GetFiles(temp.Path, "preferences.json.*.tmp"));
    }

    [Fact]
    public async Task SaveAsync_WhenStagedPayloadIsTruncated_PreservesExistingPreferences()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var temp = new TempDir();
        var path = Path.Combine(temp.Path, "preferences.json");
        var original = AppPreferencesV1Codec.Serialize(AppPreferencesV1CodecTests.CreatePreferences()).Value;
        await File.WriteAllBytesAsync(path, original, cancellationToken);
        using var store = new AppPreferencesStore(
            path,
            NullLogger<AppPreferencesStore>.Instance,
            new DelegatingPlatformFileSecurity
            {
                RestrictFile = filePath =>
                {
                    if (filePath.EndsWith(".tmp", StringComparison.Ordinal))
                        File.WriteAllText(filePath, "{");
                }
            });

        var result = await store.SaveAsync(
            AppPreferencesV1CodecTests.CreatePreferences() with { CultureName = "en-US" },
            cancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(AppPreferencesErrorCode.WriteFailed, AppPreferencesV1CodecTests.ErrorCode(result.Errors));
        Assert.Equal(original, await File.ReadAllBytesAsync(path, cancellationToken));
        Assert.Empty(Directory.GetFiles(temp.Path, "preferences.json.*.tmp"));
    }

    [Fact]
    public async Task SaveAsync_WhenPostCommitHardeningFails_RollsBackExistingPreferences()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var temp = new TempDir();
        var path = Path.Combine(temp.Path, "preferences.json");
        using (var initialStore = CreateStore(path))
        {
            Assert.True((await initialStore.SaveAsync(
                AppPreferencesV1CodecTests.CreatePreferences(),
                cancellationToken)).IsSuccess);
        }
        var original = await File.ReadAllBytesAsync(path, cancellationToken);
        using var store = new AppPreferencesStore(
            path,
            NullLogger<AppPreferencesStore>.Instance,
            new DelegatingPlatformFileSecurity
            {
                RestrictFile = filePath =>
                {
                    if (string.Equals(filePath, path, StringComparison.Ordinal))
                        throw new UnauthorizedAccessException("denied after commit");
                }
            });

        var result = await store.SaveAsync(
            AppPreferencesV1CodecTests.CreatePreferences() with { CultureName = "en-US" },
            cancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(AppPreferencesErrorCode.WriteAccessDenied, AppPreferencesV1CodecTests.ErrorCode(result.Errors));
        Assert.Equal(original, await File.ReadAllBytesAsync(path, cancellationToken));
        Assert.Empty(Directory.GetFiles(temp.Path, "preferences.json.*.tmp"));
        Assert.Empty(Directory.GetFiles(temp.Path, "preferences.json.*.rollback"));
    }

    [Fact]
    public async Task SaveAsync_WhenFirstCommitHardeningFails_RemovesFailedPreferences()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var temp = new TempDir();
        var path = Path.Combine(temp.Path, "preferences.json");
        using var store = new AppPreferencesStore(
            path,
            NullLogger<AppPreferencesStore>.Instance,
            new DelegatingPlatformFileSecurity
            {
                RestrictFile = filePath =>
                {
                    if (string.Equals(filePath, path, StringComparison.Ordinal))
                        throw new UnauthorizedAccessException("denied after commit");
                }
            });

        var result = await store.SaveAsync(
            AppPreferencesV1CodecTests.CreatePreferences(),
            cancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(AppPreferencesErrorCode.WriteAccessDenied, AppPreferencesV1CodecTests.ErrorCode(result.Errors));
        Assert.False(File.Exists(path));
        Assert.Empty(Directory.GetFiles(temp.Path, "preferences.json.*.tmp"));
        Assert.Empty(Directory.GetFiles(temp.Path, "preferences.json.*.rollback"));
    }

    private static AppPreferencesStore CreateStore(string path) =>
        new(path, NullLogger<AppPreferencesStore>.Instance, NoOpPlatformFileSecurity.Instance);

    private sealed class TempDir : IDisposable
    {
        public TempDir()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"totp-preferences-tests-{Guid.NewGuid():N}");
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
