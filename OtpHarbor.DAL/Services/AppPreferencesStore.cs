using FluentResults;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using TOTP.Core.Models;
using TOTP.Core.Security.Interfaces;
using TOTP.Core.Services.Interfaces;

namespace TOTP.DAL.Services;

public sealed class AppPreferencesStore : IAppPreferencesStore
{
    private readonly string _path;
    private readonly ILogger<AppPreferencesStore> _logger;
    private readonly IPlatformFileSecurity _fileSecurity;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public AppPreferencesStore(
        string storageFilePath,
        ILogger<AppPreferencesStore> logger,
        IPlatformFileSecurity fileSecurity)
    {
        if (string.IsNullOrWhiteSpace(storageFilePath))
            throw new ArgumentException("Path required.", nameof(storageFilePath));

        _path = Path.GetFullPath(Environment.ExpandEnvironmentVariables(storageFilePath));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _fileSecurity = fileSecurity ?? throw new ArgumentNullException(nameof(fileSecurity));
    }

    public async Task<Result<AppPreferencesV1?>> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(_path)) return Result.Ok<AppPreferencesV1?>(null);
            SecureStorageDirectory();
            _fileSecurity.RestrictFileToCurrentUser(_path);

            var payload = new byte[AppPreferencesV1Codec.MaximumPayloadSize + 1];
            try
            {
                var length = await ReadBoundedAsync(payload, cancellationToken);
                if (length > AppPreferencesV1Codec.MaximumPayloadSize)
                {
                    return Result.Fail<AppPreferencesV1?>(new AppPreferencesError(
                        AppPreferencesErrorCode.TooLarge,
                        "Preferences exceed the size limit."));
                }

                var decoded = AppPreferencesV1Codec.Deserialize(payload.AsMemory(0, length));
                return decoded.IsSuccess
                    ? Result.Ok<AppPreferencesV1?>(decoded.Value)
                    : Result.Fail<AppPreferencesV1?>(decoded.Errors);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(payload);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load preferences.");
            var code = ex is UnauthorizedAccessException
                ? AppPreferencesErrorCode.ReadAccessDenied
                : AppPreferencesErrorCode.ReadFailed;
            return Result.Fail<AppPreferencesV1?>(new AppPreferencesError(code, "Failed to load preferences.", ex));
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<Result> SaveAsync(
        AppPreferencesV1 preferences,
        CancellationToken cancellationToken = default)
    {
        var encoded = AppPreferencesV1Codec.Serialize(preferences);
        if (encoded.IsFailed) return Result.Fail(encoded.Errors);

        var lockTaken = false;
        var commitCompleted = false;
        var hadExistingPreferences = false;
        string? tempPath = null;
        string? rollbackPath = null;
        try
        {
            await _lock.WaitAsync(cancellationToken);
            lockTaken = true;
            if (Directory.Exists(_path))
                throw new UnauthorizedAccessException("The preferences path refers to a directory.");

            SecureStorageDirectory();
            tempPath = $"{_path}.{Guid.NewGuid():N}.tmp";
            await using (var stream = new FileStream(
                tempPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(encoded.Value, cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            _fileSecurity.RestrictFileToCurrentUser(tempPath);
            await VerifyFileAsync(tempPath, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            hadExistingPreferences = File.Exists(_path);
            if (hadExistingPreferences)
            {
                rollbackPath = $"{_path}.{Guid.NewGuid():N}.rollback";
                File.Replace(tempPath, _path, rollbackPath, ignoreMetadataErrors: false);
            }
            else
            {
                File.Move(tempPath, _path);
            }

            commitCompleted = true;
            _fileSecurity.RestrictFileToCurrentUser(_path);
            await VerifyFileAsync(_path, CancellationToken.None);
            if (rollbackPath is not null) TryDeleteTemporaryFile(rollbackPath);

            return Result.Ok();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Exception failure = ex;
            if (commitCompleted)
            {
                try
                {
                    await RollBackCommitAsync(hadExistingPreferences, rollbackPath);
                }
                catch (Exception rollbackException)
                {
                    _logger.LogCritical(rollbackException, "Failed to roll back the preferences commit.");
                    failure = new AggregateException(ex, rollbackException);
                }
            }

            _logger.LogError(ex, "Failed to save preferences.");
            var code = ex is UnauthorizedAccessException
                ? AppPreferencesErrorCode.WriteAccessDenied
                : AppPreferencesErrorCode.WriteFailed;
            return Result.Fail(new AppPreferencesError(code, "Failed to save preferences.", failure));
        }
        finally
        {
            if (tempPath is not null) TryDeleteTemporaryFile(tempPath);
            if (rollbackPath is not null) TryDeleteTemporaryFile(rollbackPath);
            CryptographicOperations.ZeroMemory(encoded.Value);
            if (lockTaken) _lock.Release();
        }
    }

    public void Dispose() => _lock.Dispose();

    private Task<int> ReadBoundedAsync(byte[] buffer, CancellationToken cancellationToken)
        => ReadBoundedAsync(_path, buffer, cancellationToken);

    private static async Task<int> ReadBoundedAsync(
        string path,
        byte[] buffer,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            useAsync: true);
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(total), cancellationToken);
            if (read == 0) break;
            total += read;
        }

        return total;
    }

    private static async Task VerifyFileAsync(string path, CancellationToken cancellationToken)
    {
        var payload = new byte[AppPreferencesV1Codec.MaximumPayloadSize + 1];
        try
        {
            var length = await ReadBoundedAsync(path, payload, cancellationToken);
            if (length > AppPreferencesV1Codec.MaximumPayloadSize
                || AppPreferencesV1Codec.Deserialize(payload.AsMemory(0, length)).IsFailed)
            {
                throw new InvalidDataException("Preferences verification failed.");
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payload);
        }
    }

    private async Task RollBackCommitAsync(bool hadExistingPreferences, string? rollbackPath)
    {
        if (!hadExistingPreferences)
        {
            if (File.Exists(_path)) File.Delete(_path);
            if (File.Exists(_path))
                throw new IOException("The failed preferences commit could not be removed.");
            return;
        }

        if (rollbackPath is null || !File.Exists(rollbackPath))
            throw new FileNotFoundException("The previous preferences file is unavailable for rollback.");

        _fileSecurity.RestrictFileToCurrentUser(rollbackPath);
        File.Replace(rollbackPath, _path, destinationBackupFileName: null, ignoreMetadataErrors: false);
        await VerifyFileAsync(_path, CancellationToken.None);
    }

    private void SecureStorageDirectory()
    {
        var directory = Path.GetDirectoryName(_path)
            ?? throw new DirectoryNotFoundException("The preferences directory is unavailable.");
        Directory.CreateDirectory(directory);
        _fileSecurity.RestrictDirectoryToCurrentUser(directory);
    }

    private static void TryDeleteTemporaryFile(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // Best effort after the primary operation has completed or failed.
        }
    }
}
