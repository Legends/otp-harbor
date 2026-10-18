using FluentResults;
using Microsoft.Extensions.Logging;
using TOTP.Core.Backup;
using TOTP.Core.Services.Interfaces;

namespace TOTP.DAL.Services;

public sealed class AutomaticBackupConfigurationStore : IAutomaticBackupConfigurationStore
{
    public const string FileName = "automatic-backup.json";

    private readonly string _path;
    private readonly ILogger<AutomaticBackupConfigurationStore> _logger;
    private readonly IPlatformFileSecurity _fileSecurity;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public AutomaticBackupConfigurationStore(
        string path,
        ILogger<AutomaticBackupConfigurationStore> logger,
        IPlatformFileSecurity fileSecurity)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Path required.", nameof(path));

        _path = Path.GetFullPath(path);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _fileSecurity = fileSecurity ?? throw new ArgumentNullException(nameof(fileSecurity));
    }

    public async Task<Result<AutomaticBackupConfigurationV1?>> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(_path))
                return Result.Ok<AutomaticBackupConfigurationV1?>(null);

            SecureDirectory();
            _fileSecurity.RestrictFileToCurrentUser(_path);
            var info = new FileInfo(_path);
            if (info.Length > AutomaticBackupConfigurationV1Codec.MaximumPayloadSize)
                return Fail<AutomaticBackupConfigurationV1?>(AutomaticBackupErrorCode.TooLarge, "Automatic backup configuration exceeds the size limit.");

            var payload = await File.ReadAllBytesAsync(_path, cancellationToken);
            var decoded = AutomaticBackupConfigurationV1Codec.Deserialize(payload);
            return decoded.IsSuccess
                ? Result.Ok<AutomaticBackupConfigurationV1?>(decoded.Value)
                : Result.Fail<AutomaticBackupConfigurationV1?>(decoded.Errors);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load automatic-backup configuration.");
            return Fail<AutomaticBackupConfigurationV1?>(AutomaticBackupErrorCode.ReadFailed, "Automatic backup configuration could not be loaded.", ex);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<Result> SaveAsync(
        AutomaticBackupConfigurationV1 configuration,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var encoded = AutomaticBackupConfigurationV1Codec.Serialize(configuration);
        if (encoded.IsFailed) return Result.Fail(encoded.Errors);

        await _lock.WaitAsync(cancellationToken);
        var payload = encoded.Value;
        var temporaryPath = $"{_path}.{Guid.NewGuid():N}.tmp";
        try
        {
            SecureDirectory();
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(payload, cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            _fileSecurity.RestrictFileToCurrentUser(temporaryPath);
            var staged = await File.ReadAllBytesAsync(temporaryPath, cancellationToken);
            try
            {
                if (AutomaticBackupConfigurationV1Codec.Deserialize(staged).IsFailed)
                    throw new InvalidDataException("Staged automatic-backup configuration is invalid.");
            }
            finally
            {
                Array.Clear(staged, 0, staged.Length);
            }

            if (File.Exists(_path))
                File.Replace(temporaryPath, _path, destinationBackupFileName: null, ignoreMetadataErrors: false);
            else
                File.Move(temporaryPath, _path);

            _fileSecurity.RestrictFileToCurrentUser(_path);
            return Result.Ok();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save automatic-backup configuration.");
            return Fail(AutomaticBackupErrorCode.ConfigurationFailed, "Automatic backup configuration could not be saved.", ex);
        }
        finally
        {
            Array.Clear(payload, 0, payload.Length);
            TryDelete(temporaryPath);
            _lock.Release();
        }
    }

    public async Task<Result> DeleteAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(_path)) File.Delete(_path);
            return Result.Ok();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove automatic-backup configuration.");
            return Fail(AutomaticBackupErrorCode.ConfigurationFailed, "Automatic backup configuration could not be removed.", ex);
        }
        finally
        {
            _lock.Release();
        }
    }

    private void SecureDirectory()
    {
        var directory = Path.GetDirectoryName(_path)
            ?? throw new DirectoryNotFoundException("Automatic backup configuration directory is unavailable.");
        Directory.CreateDirectory(directory);
        _fileSecurity.RestrictDirectoryToCurrentUser(directory);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
        }
    }

    private static Result Fail(AutomaticBackupErrorCode code, string message, Exception? exception = null) =>
        Result.Fail(new AutomaticBackupError(code, message, exception));

    private static Result<T> Fail<T>(AutomaticBackupErrorCode code, string message, Exception? exception = null) =>
        Result.Fail<T>(new AutomaticBackupError(code, message, exception));
}
