using System.Security.Cryptography;
using System.Text.Json;
using FluentResults;
using TOTP.Core.Security;

namespace TOTP.Core.Backup;

public static class AutomaticBackupPackageV1Codec
{
    public const int MaximumVaultSize = 16 * 1024 * 1024;
    public const int MaximumPayloadSize = 24 * 1024 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        MaxDepth = 24,
        WriteIndented = false
    };

    public static Result<byte[]> Serialize(AutomaticBackupPackageV1 package)
    {
        ArgumentNullException.ThrowIfNull(package);
        var validation = Validate(package);
        if (validation.IsFailed) return Result.Fail<byte[]>(validation.Errors);

        var payload = JsonSerializer.SerializeToUtf8Bytes(package, JsonOptions);
        if (payload.Length <= MaximumPayloadSize) return Result.Ok(payload);

        CryptographicOperations.ZeroMemory(payload);
        return Fail<byte[]>(AutomaticBackupErrorCode.TooLarge, "Automatic backup exceeds the size limit.");
    }

    public static Result<AutomaticBackupPackageV1> Deserialize(ReadOnlyMemory<byte> payload)
    {
        if (payload.IsEmpty)
            return Fail<AutomaticBackupPackageV1>(AutomaticBackupErrorCode.InvalidFormat, "Automatic backup is empty.");
        if (payload.Length > MaximumPayloadSize)
            return Fail<AutomaticBackupPackageV1>(AutomaticBackupErrorCode.TooLarge, "Automatic backup exceeds the size limit.");

        try
        {
            using var document = JsonDocument.Parse(payload, new JsonDocumentOptions { MaxDepth = 24 });
            if (HasDuplicateProperty(document.RootElement))
                return Fail<AutomaticBackupPackageV1>(AutomaticBackupErrorCode.InvalidFormat, "Automatic backup contains duplicate properties.");

            var package = JsonSerializer.Deserialize<AutomaticBackupPackageV1>(payload.Span, JsonOptions);
            if (package is null)
                return Fail<AutomaticBackupPackageV1>(AutomaticBackupErrorCode.InvalidFormat, "Automatic backup is null.");

            var validation = Validate(package);
            return validation.IsSuccess
                ? Result.Ok(package)
                : Result.Fail<AutomaticBackupPackageV1>(validation.Errors);
        }
        catch (JsonException)
        {
            return Fail<AutomaticBackupPackageV1>(AutomaticBackupErrorCode.InvalidFormat, "Automatic backup JSON is invalid.");
        }
    }

    public static string ComputeVaultFingerprint(
        ReadOnlySpan<byte> encryptedVault,
        ReadOnlySpan<byte> encodedPortableEnvelope)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(encryptedVault);
        hash.AppendData(encodedPortableEnvelope);
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static Result Validate(AutomaticBackupPackageV1 package)
    {
        if (!string.Equals(package.Format, AutomaticBackupPackageV1.FormatIdentifier, StringComparison.Ordinal)
            || package.Version != AutomaticBackupPackageV1.CurrentVersion
            || package.CreatedUtc == default)
        {
            return Fail(AutomaticBackupErrorCode.InvalidFormat, "Automatic backup header is invalid.");
        }

        if (package.EncryptedVault is not { Length: > 0 }
            || package.EncryptedVault.Length > MaximumVaultSize)
        {
            return Fail(AutomaticBackupErrorCode.InvalidVault, "Automatic backup vault is invalid.");
        }

        if (package.AuthorizationEnvelope is null
            || package.AuthorizationEnvelope.QuickUnlockWrapper is not null)
        {
            return Fail(AutomaticBackupErrorCode.InvalidFormat, "Automatic backup contains a non-portable authorization wrapper.");
        }

        var envelope = AuthorizationEnvelopeV2Codec.Serialize(package.AuthorizationEnvelope);
        if (envelope.IsFailed)
            return Fail(AutomaticBackupErrorCode.InvalidFormat, "Automatic backup authorization envelope is invalid.");

        CryptographicOperations.ZeroMemory(envelope.Value);

        if (package.VaultSha256 is not { Length: 64 })
            return Fail(AutomaticBackupErrorCode.InvalidVault, "Automatic backup vault fingerprint is invalid.");

        byte[]? expected = null;
        try
        {
            expected = Convert.FromHexString(package.VaultSha256);
            var actual = SHA256.HashData(package.EncryptedVault);
            try
            {
                return CryptographicOperations.FixedTimeEquals(expected, actual)
                    ? Result.Ok()
                    : Fail(AutomaticBackupErrorCode.InvalidVault, "Automatic backup vault fingerprint does not match.");
            }
            finally
            {
                CryptographicOperations.ZeroMemory(actual);
            }
        }
        catch (FormatException)
        {
            return Fail(AutomaticBackupErrorCode.InvalidVault, "Automatic backup vault fingerprint is invalid.");
        }
        finally
        {
            if (expected is not null) CryptographicOperations.ZeroMemory(expected);
        }
    }

    private static bool HasDuplicateProperty(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name) || HasDuplicateProperty(property.Value)) return true;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            return element.EnumerateArray().Any(HasDuplicateProperty);
        }

        return false;
    }

    private static Result Fail(AutomaticBackupErrorCode code, string message) =>
        Result.Fail(new AutomaticBackupError(code, message));

    private static Result<T> Fail<T>(AutomaticBackupErrorCode code, string message) =>
        Result.Fail<T>(new AutomaticBackupError(code, message));
}
