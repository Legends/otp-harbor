using System.Security.Cryptography;
using FluentResults;
using TOTP.Core.Backup;
using TOTP.Core.Models;
using TOTP.Core.Security;
using TOTP.Core.Security.Interfaces;
using TOTP.Core.Security.Models;
using TOTP.Core.Services.Interfaces;
using TOTP.Infrastructure.Security;

namespace TOTP.Infrastructure.Storage;

public sealed class LegacyVaultRecoveryService(
    string currentVaultPath,
    string olderVaultPath,
    string olderEnvelopePath,
    IPlatformFileSecurity fileSecurity,
    IMasterPasswordService passwordService,
    IVaultSnapshotReader vaultReader) : ILegacyVaultRecoveryService
{
    private readonly string _currentVaultPath = Path.GetFullPath(currentVaultPath);
    private readonly string _olderVaultPath = Path.GetFullPath(olderVaultPath);
    private readonly string _olderEnvelopePath = Path.GetFullPath(olderEnvelopePath);

    public bool HasConflict() =>
        !string.Equals(_currentVaultPath, _olderVaultPath, StringComparison.OrdinalIgnoreCase)
        && File.Exists(_currentVaultPath)
        && File.Exists(_olderVaultPath);

    public async Task<Result<IReadOnlyList<Account>>> ReadOlderAccountsAsync(
        string masterPassword,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(masterPassword))
            return Result.Fail<IReadOnlyList<Account>>("The older master password is required.");

        byte[]? vault = null;
        byte[]? encodedEnvelope = null;
        byte[]? key = null;
        AuthorizationEnvelopeV2? envelope = null;
        try
        {
            if (!HasConflict() || !File.Exists(_olderEnvelopePath))
                return Result.Fail<IReadOnlyList<Account>>("The older vault or authorization envelope is unavailable.");

            ValidateRegularBoundedFile(_olderVaultPath, AutomaticBackupPackageV1Codec.MaximumVaultSize);
            ValidateRegularBoundedFile(_olderEnvelopePath, AuthorizationEnvelopeV2Codec.MaximumPayloadSize);
            fileSecurity.RestrictFileToCurrentUser(_olderVaultPath);
            fileSecurity.RestrictFileToCurrentUser(_olderEnvelopePath);
            vault = await File.ReadAllBytesAsync(_olderVaultPath, cancellationToken).ConfigureAwait(false);
            encodedEnvelope = await File.ReadAllBytesAsync(_olderEnvelopePath, cancellationToken).ConfigureAwait(false);
            if (vault.Length > AutomaticBackupPackageV1Codec.MaximumVaultSize
                || encodedEnvelope.Length > AuthorizationEnvelopeV2Codec.MaximumPayloadSize
                || !vault.AsSpan().StartsWith("TVLT"u8))
                return Result.Fail<IReadOnlyList<Account>>("The older vault format is invalid.");

            var decoded = AuthorizationEnvelopeV2Codec.Deserialize(encodedEnvelope);
            if (decoded.IsFailed)
                return Result.Fail<IReadOnlyList<Account>>("The older authorization envelope is invalid.");

            envelope = decoded.Value;
            key = await passwordService.UnwrapKeyV2Async(
                envelope.PasswordWrapper, masterPassword, cancellationToken).ConfigureAwait(false);
            if (key is null)
                return Result.Fail<IReadOnlyList<Account>>("The older vault password is incorrect.");

            var accounts = vaultReader.ReadAccounts(vault, key);
            return accounts.IsSuccess
                ? accounts
                : Result.Fail<IReadOnlyList<Account>>("The older vault could not be decrypted.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return Result.Fail<IReadOnlyList<Account>>("The older vault could not be read safely.");
        }
        finally
        {
            if (vault is not null) CryptographicOperations.ZeroMemory(vault);
            if (encodedEnvelope is not null) CryptographicOperations.ZeroMemory(encodedEnvelope);
            if (key is not null) CryptographicOperations.ZeroMemory(key);
            if (envelope is not null) AuthorizationEnvelopeBufferCleaner.Clear(envelope);
        }
    }

    private static void ValidateRegularBoundedFile(string path, int maximumBytes)
    {
        var directory = Path.GetDirectoryName(path)
            ?? throw new DirectoryNotFoundException("The older-vault directory is unavailable.");
        if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("The older-vault directory is a reparse point.");

        var info = new FileInfo(path);
        if ((info.Attributes & FileAttributes.ReparsePoint) != 0
            || info.Length <= 0 || info.Length > maximumBytes)
            throw new InvalidDataException("A sensitive older-vault file is invalid.");
    }
}
