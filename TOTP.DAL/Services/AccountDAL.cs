using FluentResults;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using TOTP.Core.Common;
using TOTP.Core.Models;
using TOTP.Core.Security.Interfaces;
using TOTP.Core.Services.Interfaces;
using TOTP.DAL.Common;

namespace TOTP.DAL.Services;

public sealed class AccountDAL : IAccountDAL
{
    private readonly string _secretsPath;
    private readonly IVaultService _vaultService;
    private readonly ILogger<AccountDAL> _logger;
    private readonly IPlatformFileSecurity _fileSecurity;
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public AccountDAL(
        ILogger<AccountDAL> logger,
        IVaultService vaultService,
        string storageFilePath,
        IPlatformFileSecurity fileSecurity)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _vaultService = vaultService ?? throw new ArgumentNullException(nameof(vaultService));
        _fileSecurity = fileSecurity ?? throw new ArgumentNullException(nameof(fileSecurity));

        if (string.IsNullOrWhiteSpace(storageFilePath))
            throw new ArgumentException("Path required.", nameof(storageFilePath));

        _secretsPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(storageFilePath));
    }

    public async Task<Result<List<Account>>> GetAllAsync()
    {
        await _semaphore.WaitAsync();
        try
        {
            if (!File.Exists(_secretsPath))
            {
                return Result.Ok<List<Account>>(new());
            }

            SecureStorageDirectory();
            _fileSecurity.RestrictFileToCurrentUser(_secretsPath);
            byte[] blob = await File.ReadAllBytesAsync(_secretsPath);
            return Result.Ok(_vaultService.DecryptVault(blob));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load accounts.");
            return Result.Fail(AccountDalErrorMapper.MapReadError(ex));
        }
        finally { _semaphore.Release(); }
    }

    public async Task<Result> ExportEncryptedAsync(string targetPath)
    {
        await _semaphore.WaitAsync();
        byte[]? blob = null;
        try
        {
            targetPath = Path.GetFullPath(targetPath);
            var data = await GetAllInternalAsync();
            blob = _vaultService.EncryptVault(data);
            var targetDirectory = Path.GetDirectoryName(targetPath);
            if (string.IsNullOrWhiteSpace(targetDirectory) || !Directory.Exists(targetDirectory))
            {
                throw new DirectoryNotFoundException("The export directory was not found.");
            }

            await CommitEncryptedBlobAsync(targetPath, blob);

            return Result.Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Export failed to {Path}", targetPath);
            return Result.Fail(AccountDalErrorMapper.MapExportError(ex));
        }
        finally
        {
            if (blob is not null) CryptographicOperations.ZeroMemory(blob);
            _semaphore.Release();
        }
    }

    private async Task<List<Account>> GetAllInternalAsync()
    {
        if (!File.Exists(_secretsPath))
        {
            return [];
        }

        SecureStorageDirectory();
        _fileSecurity.RestrictFileToCurrentUser(_secretsPath);
        byte[] blob = await File.ReadAllBytesAsync(_secretsPath);
        return _vaultService.DecryptVault(blob);
    }

    public async Task<Result> AddNewAsync(Account newItem) =>
        await ExecuteWriteAsync(list => list.Add(newItem), AppErrorCode.OtpCreateFailed, "Failed to create OTP entry.");

    public async Task<Result> UpdateAsync(Account updated) =>
        await ExecuteWriteAsync(list =>
        {
            var idx = list.FindIndex(x => x.ID == updated.ID);
            if (idx != -1)
            {
                list[idx] = updated;
            }
        }, AppErrorCode.OtpUpdateFailed, "Failed to update OTP entry.");

    public async Task<Result> SaveGroupAsync(
        AccountGroup group,
        IReadOnlyCollection<Guid> accountIds)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(accountIds);
        var selectedIds = accountIds.ToHashSet();
        return await ExecuteWriteAsync(list =>
        {
            if (!list.Any(account => selectedIds.Contains(account.ID)))
                throw new InvalidOperationException("No selected group account is available.");

            for (var index = 0; index < list.Count; index++)
            {
                var account = list[index];
                if (selectedIds.Contains(account.ID))
                {
                    list[index] = account.WithGroup(group);
                }
                else if (account.Group?.Id == group.Id)
                {
                    list[index] = account.WithGroup(null);
                }
            }
        }, AppErrorCode.OtpUpdateFailed, "Failed to save account group.");
    }

    public async Task<Result> DeleteGroupAsync(Guid groupId) =>
        await ExecuteWriteAsync(list =>
        {
            for (var index = 0; index < list.Count; index++)
            {
                if (list[index].Group?.Id == groupId)
                    list[index] = list[index].WithGroup(null);
            }
        }, AppErrorCode.OtpUpdateFailed, "Failed to delete account group.");

    public async Task<Result> DeleteAsync(Account account) =>
        await ExecuteWriteAsync(list => list.RemoveAll(x => x.ID == account.ID), AppErrorCode.OtpDeleteFailed, "Failed to delete OTP entry.");

    private async Task<Result> ExecuteWriteAsync(Action<List<Account>> action, AppErrorCode operationCode, string operationMessage)
    {
        await _semaphore.WaitAsync();
        byte[]? blob = null;
        try
        {
            var list = await GetAllInternalAsync();
            action(list);
            blob = _vaultService.EncryptVault(list);

            SecureStorageDirectory();
            await CommitEncryptedBlobAsync(_secretsPath, blob);

            return Result.Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Storage operation failed.");
            return Result.Fail(AccountDalErrorMapper.MapWriteError(ex, operationCode, operationMessage));
        }
        finally
        {
            if (blob is not null) CryptographicOperations.ZeroMemory(blob);
            _semaphore.Release();
        }
    }

    public async Task<Result> ReEncryptStorageAsync() => await ExportEncryptedAsync(_secretsPath);

    public async Task<Result> BackupOtpEntriesStorageFileAsync()
    {
        await _semaphore.WaitAsync();
        try
        {
            if (!File.Exists(_secretsPath))
            {
                return Result.Ok();
            }

            var dir = Path.GetDirectoryName(_secretsPath)
                ?? throw new DirectoryNotFoundException("The OTP backup directory is unavailable.");
            var fileName = Path.GetFileName(_secretsPath);
            var latestBackupPath = Path.Combine(dir, $"{fileName}.bak1");

            SecureStorageDirectory();
            _fileSecurity.RestrictFileToCurrentUser(_secretsPath);

            if (File.Exists(latestBackupPath))
            {
                _fileSecurity.RestrictFileToCurrentUser(latestBackupPath);
                if (await AreFilesIdenticalAsync(_secretsPath, latestBackupPath))
                {
                    _logger.LogInformation("Backup skipped: No changes detected in storage file.");
                    return Result.Ok();
                }
            }

            for (var generation = 5; generation >= 2; generation--)
            {
                var sourcePath = Path.Combine(dir, $"{fileName}.bak{generation - 1}");
                if (!File.Exists(sourcePath)) continue;

                var destinationPath = Path.Combine(dir, $"{fileName}.bak{generation}");
                await CopyEncryptedFileAsync(sourcePath, destinationPath);
            }

            await CopyEncryptedFileAsync(_secretsPath, latestBackupPath);
            return Result.Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, nameof(BackupOtpEntriesStorageFileAsync));
            return Result.Fail(new AppError(
                AppErrorCode.OtpStorageBackupFailed,
                "Failed to create OTP storage backup.",
                ex));
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private static async Task<bool> AreFilesIdenticalAsync(string path1, string path2)
    {
        var hash1 = await ComputeFileHashAsync(path1);
        var hash2 = await ComputeFileHashAsync(path2);
        try
        {
            return CryptographicOperations.FixedTimeEquals(hash1, hash2);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(hash1);
            CryptographicOperations.ZeroMemory(hash2);
        }
    }

    public void Dispose()
    {
        _semaphore.Dispose();
    }

    private void SecureStorageDirectory()
    {
        var directory = Path.GetDirectoryName(_secretsPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new DirectoryNotFoundException("The OTP storage directory is unavailable.");
        }

        Directory.CreateDirectory(directory);
        _fileSecurity.RestrictDirectoryToCurrentUser(directory);
    }

    private static void TryDeleteTemporaryFile(string tempPath)
    {
        try
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
        catch
        {
            // Best effort after the primary failure has already been captured.
        }
    }

    private static string CreateStagingPath(string destinationPath) =>
        $"{destinationPath}.{Guid.NewGuid():N}.tmp";

    private async Task CommitEncryptedBlobAsync(string destinationPath, byte[] encryptedBlob)
    {
        var tempPath = CreateStagingPath(destinationPath);
        var rollbackPath = $"{destinationPath}.{Guid.NewGuid():N}.rollback";
        var hadExistingFile = false;
        var commitCompleted = false;
        byte[]? previousHash = null;
        try
        {
            await WriteStagingFileAsync(tempPath, encryptedBlob);
            _fileSecurity.RestrictFileToCurrentUser(tempPath);
            await VerifyFileBytesAsync(tempPath, encryptedBlob);

            hadExistingFile = File.Exists(destinationPath);
            if (hadExistingFile)
            {
                previousHash = await ComputeFileHashAsync(destinationPath);
                File.Replace(tempPath, destinationPath, rollbackPath, ignoreMetadataErrors: false);
            }
            else
            {
                File.Move(tempPath, destinationPath);
            }

            commitCompleted = true;
            _fileSecurity.RestrictFileToCurrentUser(destinationPath);
            await VerifyFileBytesAsync(destinationPath, encryptedBlob);
            TryDeleteTemporaryFile(rollbackPath);
        }
        catch (Exception ex)
        {
            if (commitCompleted)
            {
                try
                {
                    await RollBackCommitAsync(destinationPath, rollbackPath, hadExistingFile, previousHash);
                }
                catch (Exception rollbackException)
                {
                    _logger.LogCritical(rollbackException, "Failed to roll back an encrypted-vault commit.");
                    throw new AggregateException(ex, rollbackException);
                }
            }

            throw;
        }
        finally
        {
            TryDeleteTemporaryFile(tempPath);
            TryDeleteTemporaryFile(rollbackPath);
            if (previousHash is not null) CryptographicOperations.ZeroMemory(previousHash);
        }
    }

    private async Task CopyEncryptedFileAsync(string sourcePath, string destinationPath)
    {
        _fileSecurity.RestrictFileToCurrentUser(sourcePath);
        var encryptedBlob = await File.ReadAllBytesAsync(sourcePath);
        try
        {
            await CommitEncryptedBlobAsync(destinationPath, encryptedBlob);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(encryptedBlob);
        }
    }

    private async Task RollBackCommitAsync(
        string destinationPath,
        string rollbackPath,
        bool hadExistingFile,
        byte[]? previousHash)
    {
        if (!hadExistingFile)
        {
            if (File.Exists(destinationPath)) File.Delete(destinationPath);
            if (File.Exists(destinationPath))
                throw new IOException("The failed encrypted-vault commit could not be removed.");
            return;
        }

        if (previousHash is null || !File.Exists(rollbackPath))
            throw new FileNotFoundException("The previous encrypted file is unavailable for rollback.");

        _fileSecurity.RestrictFileToCurrentUser(rollbackPath);
        File.Replace(rollbackPath, destinationPath, destinationBackupFileName: null, ignoreMetadataErrors: false);
        var restoredHash = await ComputeFileHashAsync(destinationPath);
        try
        {
            if (!CryptographicOperations.FixedTimeEquals(previousHash, restoredHash))
                throw new InvalidDataException("Encrypted-vault rollback verification failed.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(restoredHash);
        }
    }

    private static async Task WriteStagingFileAsync(string path, byte[] data)
    {
        await using var stream = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        await stream.WriteAsync(data);
        stream.Flush(flushToDisk: true);
    }

    private static async Task VerifyFileBytesAsync(string path, ReadOnlyMemory<byte> expected)
    {
        var info = new FileInfo(path);
        if (info.Length != expected.Length)
            throw new InvalidDataException("Encrypted-vault write verification failed.");

        var actual = new byte[expected.Length];
        try
        {
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                useAsync: true);
            await stream.ReadExactlyAsync(actual);
            if (!CryptographicOperations.FixedTimeEquals(expected.Span, actual))
                throw new InvalidDataException("Encrypted-vault write verification failed.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(actual);
        }
    }

    private static async Task<byte[]> ComputeFileHashAsync(string path)
    {
        using var hash = SHA256.Create();
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            useAsync: true);
        return await hash.ComputeHashAsync(stream);
    }
}
