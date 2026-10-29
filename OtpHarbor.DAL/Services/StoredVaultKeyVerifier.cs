using System.Security.Cryptography;
using FluentResults;
using Microsoft.Extensions.Logging;
using TOTP.Core.Security.Interfaces;
using TOTP.Core.Security.Models;
using TOTP.Core.Services.Interfaces;

namespace TOTP.DAL.Services;

public sealed class StoredVaultKeyVerifier : IStoredVaultKeyVerifier
{
    public const int MaximumVaultSize = 16 * 1024 * 1024;

    private readonly string _path;
    private readonly IVaultKeyVerifier _verifier;
    private readonly ILogger<StoredVaultKeyVerifier> _logger;
    private readonly IPlatformFileSecurity _fileSecurity;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public StoredVaultKeyVerifier(
        string storageFilePath,
        IVaultKeyVerifier verifier,
        ILogger<StoredVaultKeyVerifier> logger,
        IPlatformFileSecurity fileSecurity)
    {
        if (string.IsNullOrWhiteSpace(storageFilePath))
            throw new ArgumentException("Path required.", nameof(storageFilePath));

        _path = Path.GetFullPath(Environment.ExpandEnvironmentVariables(storageFilePath));
        _verifier = verifier ?? throw new ArgumentNullException(nameof(verifier));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _fileSecurity = fileSecurity ?? throw new ArgumentNullException(nameof(fileSecurity));
    }

    public async Task<Result<VaultKeyVerificationStatus>> VerifyAsync(
        ReadOnlyMemory<byte> candidateKey,
        CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            SecureStorageDirectory();
            FileStream stream;
            try
            {
                stream = new FileStream(
                    _path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    bufferSize: 4096,
                    useAsync: true);
            }
            catch (FileNotFoundException)
            {
                return Result.Ok(VaultKeyVerificationStatus.VaultNotFound);
            }

            await using (stream)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _fileSecurity.RestrictFileToCurrentUser(_path);
                if (stream.Length > MaximumVaultSize)
                {
                    return Result.Fail<VaultKeyVerificationStatus>(new StoredVaultVerificationError(
                        StoredVaultVerificationErrorCode.TooLarge,
                        "The vault exceeds the verification size limit."));
                }

                var payload = new byte[checked((int)stream.Length)];
                try
                {
                    await stream.ReadExactlyAsync(payload, cancellationToken);
                    if (stream.Length != payload.Length)
                        throw new IOException("The vault changed while it was being read.");
                    return Result.Ok(_verifier.Verify(payload, candidateKey.Span));
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(payload);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read the vault for candidate-key verification.");
            var code = ex is UnauthorizedAccessException
                ? StoredVaultVerificationErrorCode.ReadAccessDenied
                : StoredVaultVerificationErrorCode.ReadFailed;
            return Result.Fail<VaultKeyVerificationStatus>(new StoredVaultVerificationError(
                code,
                "Failed to read the vault for candidate-key verification.",
                ex));
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Dispose() => _lock.Dispose();

    private void SecureStorageDirectory()
    {
        var directory = Path.GetDirectoryName(_path)
            ?? throw new DirectoryNotFoundException("The vault directory is unavailable.");
        Directory.CreateDirectory(directory);
        _fileSecurity.RestrictDirectoryToCurrentUser(directory);
    }
}
