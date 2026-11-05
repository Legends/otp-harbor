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
    string reviewedStatePath,
    IPlatformFileSecurity fileSecurity,
    IMasterPasswordService passwordService,
    IVaultSnapshotReader vaultReader) : ILegacyVaultRecoveryService
{
    private readonly string _currentVaultPath = Path.GetFullPath(currentVaultPath);
    private readonly string _olderVaultPath = Path.GetFullPath(olderVaultPath);
    private readonly string _olderEnvelopePath = Path.GetFullPath(olderEnvelopePath);
    private readonly string _reviewedStatePath = Path.GetFullPath(reviewedStatePath);

    public bool HasConflict() =>
        !string.Equals(_currentVaultPath, _olderVaultPath, StringComparison.OrdinalIgnoreCase)
        && File.Exists(_currentVaultPath)
        && File.Exists(_olderVaultPath);

    public bool HasUnreviewedConflict()
    {
        if (!HasConflict()) return false;

        try
        {
            if (!File.Exists(_reviewedStatePath)) return true;
            var info = new FileInfo(_reviewedStatePath);
            if ((info.Attributes & FileAttributes.ReparsePoint) != 0 || info.Length is <= 0 or > 256)
                return true;

            var reviewedFingerprint = File.ReadAllText(_reviewedStatePath).Trim();
            return !CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.ASCII.GetBytes(reviewedFingerprint),
                System.Text.Encoding.ASCII.GetBytes(CreateConflictFingerprint()));
        }
        catch
        {
            return true;
        }
    }

    public async Task<Result> MarkConflictReviewedAsync(CancellationToken cancellationToken = default)
    {
        if (!HasConflict()) return Result.Fail("No older-vault conflict is available to review.");

        var temporaryPath = $"{_reviewedStatePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            var directory = Path.GetDirectoryName(_reviewedStatePath)
                ?? throw new DirectoryNotFoundException("The recovery-state directory is unavailable.");
            Directory.CreateDirectory(directory);
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("The recovery-state directory is a reparse point.");
            if (File.Exists(_reviewedStatePath)
                && (File.GetAttributes(_reviewedStatePath) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("The recovery-state file is a reparse point.");

            var fingerprint = CreateConflictFingerprint();
            await File.WriteAllTextAsync(temporaryPath, fingerprint, cancellationToken).ConfigureAwait(false);
            fileSecurity.RestrictFileToCurrentUser(temporaryPath);
            File.Move(temporaryPath, _reviewedStatePath, overwrite: true);
            fileSecurity.RestrictFileToCurrentUser(_reviewedStatePath);
            return Result.Ok();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Result.Fail(new ExceptionalError("The older-vault review state could not be saved.", ex));
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
            catch
            {
                // A stale, user-private temporary marker is harmless and can be replaced later.
            }
        }
    }

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

    private string CreateConflictFingerprint()
    {
        ValidateRegularBoundedFile(_olderVaultPath, AutomaticBackupPackageV1Codec.MaximumVaultSize);
        ValidateRegularBoundedFile(_olderEnvelopePath, AuthorizationEnvelopeV2Codec.MaximumPayloadSize);

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendFile(hash, _olderVaultPath);
        AppendFile(hash, _olderEnvelopePath);
        return $"v1:{Convert.ToHexString(hash.GetHashAndReset())}";
    }

    private static void AppendFile(IncrementalHash hash, string path)
    {
        var length = new FileInfo(path).Length;
        hash.AppendData(BitConverter.GetBytes(length));
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.Read, 81920, FileOptions.SequentialScan);
        var buffer = new byte[81920];
        try
        {
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                hash.AppendData(buffer, 0, read);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(buffer);
        }
    }
}
