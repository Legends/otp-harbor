using System.Security.Cryptography;
using FluentResults;
using Microsoft.Extensions.Logging;
using TOTP.Core.Backup;
using TOTP.Core.Models;
using TOTP.Core.Security;
using TOTP.Core.Security.Interfaces;
using TOTP.Core.Security.Models;
using TOTP.Core.Services.Interfaces;
using TOTP.Infrastructure.Security;

namespace TOTP.Infrastructure.Services;

public sealed class AutomaticBackupSnapshotService : IAutomaticBackupSnapshotService
{
    private readonly string _vaultPath;
    private readonly IAuthorizationEnvelopeStore _envelopeStore;
    private readonly IMasterPasswordService _passwordService;
    private readonly IVaultSnapshotReader _vaultReader;
    private readonly IPlatformFileSecurity _fileSecurity;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AutomaticBackupSnapshotService> _logger;

    public AutomaticBackupSnapshotService(
        string vaultPath,
        IAuthorizationEnvelopeStore envelopeStore,
        IMasterPasswordService passwordService,
        IVaultSnapshotReader vaultReader,
        IPlatformFileSecurity fileSecurity,
        TimeProvider timeProvider,
        ILogger<AutomaticBackupSnapshotService> logger)
    {
        if (string.IsNullOrWhiteSpace(vaultPath))
            throw new ArgumentException("Path required.", nameof(vaultPath));

        _vaultPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(vaultPath));
        _envelopeStore = envelopeStore ?? throw new ArgumentNullException(nameof(envelopeStore));
        _passwordService = passwordService ?? throw new ArgumentNullException(nameof(passwordService));
        _vaultReader = vaultReader ?? throw new ArgumentNullException(nameof(vaultReader));
        _fileSecurity = fileSecurity ?? throw new ArgumentNullException(nameof(fileSecurity));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<Result<AutomaticBackupSnapshot>> CreateAsync(
        CancellationToken cancellationToken = default)
    {
        byte[]? vault = null;
        byte[]? encodedEnvelope = null;
        AuthorizationEnvelopeV2? envelope = null;
        try
        {
            if (!File.Exists(_vaultPath))
                return Fail<AutomaticBackupSnapshot>(AutomaticBackupErrorCode.VaultUnavailable, "The encrypted vault is not available yet.");

            var info = new FileInfo(_vaultPath);
            if (info.Length <= 0 || info.Length > AutomaticBackupPackageV1Codec.MaximumVaultSize)
                return Fail<AutomaticBackupSnapshot>(AutomaticBackupErrorCode.InvalidVault, "The encrypted vault size is invalid.");

            _fileSecurity.RestrictFileToCurrentUser(_vaultPath);
            vault = await File.ReadAllBytesAsync(_vaultPath, cancellationToken);
            if (vault.Length != info.Length)
                return Fail<AutomaticBackupSnapshot>(AutomaticBackupErrorCode.ReadFailed, "The encrypted vault changed while it was being read.");

            var loadedEnvelope = await _envelopeStore.LoadAsync(cancellationToken);
            if (loadedEnvelope.IsFailed || loadedEnvelope.Value is null)
                return Fail<AutomaticBackupSnapshot>(AutomaticBackupErrorCode.EnvelopeUnavailable, "The password recovery wrapper is unavailable.");

            envelope = loadedEnvelope.Value with { QuickUnlockWrapper = null };
            var serializedEnvelope = AuthorizationEnvelopeV2Codec.Serialize(envelope);
            if (serializedEnvelope.IsFailed)
                return Fail<AutomaticBackupSnapshot>(AutomaticBackupErrorCode.InvalidFormat, "The password recovery wrapper is invalid.");

            encodedEnvelope = serializedEnvelope.Value;
            if (VaultService.TrySplitEncryptedVault(
                    vault,
                    out var encryptedAccounts,
                    out var encryptedProfile))
            {
                try
                {
                    var packageV2ToWrite = new AutomaticBackupPackageV2
                    {
                        BackupId = Guid.NewGuid(),
                        CreatedUtc = _timeProvider.GetUtcNow(),
                        EncryptedAccounts = encryptedAccounts,
                        EncryptedProfile = encryptedProfile,
                        AuthorizationEnvelope = envelope
                    };
                    var encodedV2 = AutomaticBackupPackageV2Codec.Serialize(packageV2ToWrite);
                    if (encodedV2.IsFailed)
                        return Result.Fail<AutomaticBackupSnapshot>(encodedV2.Errors);
                    var fingerprintV2 = AutomaticBackupPackageV2Codec.ComputeFingerprint(
                        encryptedAccounts,
                        encryptedProfile,
                        encodedEnvelope);
                    return Result.Ok(new AutomaticBackupSnapshot(encodedV2.Value, fingerprintV2));
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(encryptedAccounts);
                    CryptographicOperations.ZeroMemory(encryptedProfile);
                }
            }

            var fingerprint = AutomaticBackupPackageV1Codec.ComputeVaultFingerprint(vault, encodedEnvelope);
            var vaultHash = SHA256.HashData(vault);
            string vaultHashText;
            try
            {
                vaultHashText = Convert.ToHexString(vaultHash);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(vaultHash);
            }

            var package = new AutomaticBackupPackageV1
            {
                CreatedUtc = _timeProvider.GetUtcNow(),
                VaultSha256 = vaultHashText,
                EncryptedVault = vault,
                AuthorizationEnvelope = envelope
            };
            var encoded = AutomaticBackupPackageV1Codec.Serialize(package);
            return encoded.IsSuccess
                ? Result.Ok(new AutomaticBackupSnapshot(encoded.Value, fingerprint))
                : Result.Fail<AutomaticBackupSnapshot>(encoded.Errors);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Automatic backup snapshot creation failed.");
            return Fail<AutomaticBackupSnapshot>(AutomaticBackupErrorCode.UnexpectedFailure, "Automatic backup snapshot creation failed.", ex);
        }
        finally
        {
            if (vault is not null) CryptographicOperations.ZeroMemory(vault);
            if (encodedEnvelope is not null) CryptographicOperations.ZeroMemory(encodedEnvelope);
            if (envelope is not null) AuthorizationEnvelopeBufferCleaner.Clear(envelope);
        }
    }

    public async Task<Result<IReadOnlyList<Account>>> ReadAccountsAsync(
        Stream source,
        string masterPassword,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (string.IsNullOrWhiteSpace(masterPassword))
            return Fail<IReadOnlyList<Account>>(AutomaticBackupErrorCode.InvalidPassword, "The master password is required.");

        byte[]? payload = null;
        byte[]? vaultKey = null;
        byte[]? combinedVault = null;
        AutomaticBackupPackageV1? package = null;
        AutomaticBackupPackageV2? packageV2 = null;
        try
        {
            payload = await ReadBoundedAsync(
                source,
                AutomaticBackupPackageV1Codec.MaximumPayloadSize,
                cancellationToken);
            if (AutomaticBackupPackageV2Codec.IsContainer(payload))
            {
                var decodedV2 = AutomaticBackupPackageV2Codec.Deserialize(payload);
                if (decodedV2.IsFailed)
                    return Result.Fail<IReadOnlyList<Account>>(decodedV2.Errors);

                packageV2 = decodedV2.Value;
                vaultKey = await _passwordService.UnwrapKeyV2Async(
                    packageV2.AuthorizationEnvelope.PasswordWrapper,
                    masterPassword,
                    cancellationToken);
                if (vaultKey is null)
                    return Fail<IReadOnlyList<Account>>(AutomaticBackupErrorCode.InvalidPassword, "The automatic backup could not be unlocked.");

                combinedVault = VaultService.CombineEncryptedVault(
                    packageV2.EncryptedAccounts,
                    packageV2.EncryptedProfile);
                return _vaultReader.ReadAccounts(combinedVault, vaultKey);
            }

            var decoded = AutomaticBackupPackageV1Codec.Deserialize(payload);
            if (decoded.IsFailed) return Result.Fail<IReadOnlyList<Account>>(decoded.Errors);
            package = decoded.Value;
            vaultKey = await _passwordService.UnwrapKeyV2Async(
                package.AuthorizationEnvelope.PasswordWrapper,
                masterPassword,
                cancellationToken);
            if (vaultKey is null)
                return Fail<IReadOnlyList<Account>>(AutomaticBackupErrorCode.InvalidPassword, "The automatic backup could not be unlocked.");

            return _vaultReader.ReadAccounts(package.EncryptedVault, vaultKey);
        }
        catch (InvalidDataException ex)
        {
            return Fail<IReadOnlyList<Account>>(AutomaticBackupErrorCode.TooLarge, "Automatic backup exceeds the size limit.", ex);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Automatic backup reading failed.");
            return Fail<IReadOnlyList<Account>>(AutomaticBackupErrorCode.ReadFailed, "Automatic backup could not be read.", ex);
        }
        finally
        {
            if (vaultKey is not null) CryptographicOperations.ZeroMemory(vaultKey);
            if (combinedVault is not null) CryptographicOperations.ZeroMemory(combinedVault);
            if (package is not null)
            {
                CryptographicOperations.ZeroMemory(package.EncryptedVault);
                AuthorizationEnvelopeBufferCleaner.Clear(package.AuthorizationEnvelope);
            }
            if (packageV2 is not null)
            {
                CryptographicOperations.ZeroMemory(packageV2.EncryptedAccounts);
                CryptographicOperations.ZeroMemory(packageV2.EncryptedProfile);
                AuthorizationEnvelopeBufferCleaner.Clear(packageV2.AuthorizationEnvelope);
            }
            if (payload is not null) CryptographicOperations.ZeroMemory(payload);
        }
    }

    private static async Task<byte[]> ReadBoundedAsync(
        Stream source,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        if (source.CanSeek && source.Length > maximumBytes)
            throw new InvalidDataException("Input exceeds the size limit.");

        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        try
        {
            while (true)
            {
                var read = await source.ReadAsync(chunk, cancellationToken);
                if (read == 0) break;
                if (buffer.Length + read > maximumBytes)
                    throw new InvalidDataException("Input exceeds the size limit.");
                await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
            }
            return buffer.ToArray();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(chunk);
        }
    }

    private static Result<T> Fail<T>(
        AutomaticBackupErrorCode code,
        string message,
        Exception? exception = null) =>
        Result.Fail<T>(new AutomaticBackupError(code, message, exception));
}
