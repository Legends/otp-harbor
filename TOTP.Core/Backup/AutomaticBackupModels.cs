using System.Text.Json.Serialization;
using TOTP.Core.Security.Models;

namespace TOTP.Core.Backup;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AutomaticBackupPackageV1
{
    public const string FormatIdentifier = "otp-harbor-automatic-backup";
    public const int CurrentVersion = 1;

    [JsonRequired]
    [JsonPropertyName("format")]
    public string Format { get; init; } = FormatIdentifier;

    [JsonRequired]
    [JsonPropertyName("version")]
    public int Version { get; init; } = CurrentVersion;

    [JsonRequired]
    [JsonPropertyName("createdUtc")]
    public DateTimeOffset CreatedUtc { get; init; }

    [JsonRequired]
    [JsonPropertyName("vaultSha256")]
    public required string VaultSha256 { get; init; }

    [JsonRequired]
    [JsonPropertyName("encryptedVault")]
    public required byte[] EncryptedVault { get; init; }

    [JsonRequired]
    [JsonPropertyName("authorizationEnvelope")]
    public required AuthorizationEnvelopeV2 AuthorizationEnvelope { get; init; }
}

public sealed record AutomaticBackupPackageV2
{
    public required Guid BackupId { get; init; }
    public required DateTimeOffset CreatedUtc { get; init; }
    public required byte[] EncryptedAccounts { get; init; }
    public required byte[] EncryptedProfile { get; init; }
    public required AuthorizationEnvelopeV2 AuthorizationEnvelope { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AutomaticBackupConfigurationV1
{
    public const string FormatIdentifier = "otp-harbor-automatic-backup-settings";
    public const int CurrentVersion = 1;

    [JsonRequired]
    [JsonPropertyName("format")]
    public string Format { get; init; } = FormatIdentifier;

    [JsonRequired]
    [JsonPropertyName("version")]
    public int Version { get; init; } = CurrentVersion;

    [JsonRequired]
    [JsonPropertyName("destinationKind")]
    public required string DestinationKind { get; init; }

    [JsonRequired]
    [JsonPropertyName("destinationLocation")]
    public required string DestinationLocation { get; init; }

    [JsonRequired]
    [JsonPropertyName("destinationDisplayName")]
    public required string DestinationDisplayName { get; init; }

    [JsonPropertyName("lastFingerprint")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LastFingerprint { get; init; }

    [JsonPropertyName("lastSuccessfulUtc")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? LastSuccessfulUtc { get; init; }

    [JsonPropertyName("lastSuccessfulFileName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LastSuccessfulFileName { get; init; }

    [JsonPropertyName("destinationAccess")]
    public AutomaticBackupDestinationAccess DestinationAccess { get; init; }

    [JsonPropertyName("lastFailureKind")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AutomaticBackupFailureKind? LastFailureKind { get; init; }

    [JsonPropertyName("lastFailureUtc")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? LastFailureUtc { get; init; }
}

public sealed record AutomaticBackupDestination(
    string Kind,
    string Location,
    string DisplayName);

public sealed record AutomaticBackupStoredFile(
    string Identifier,
    string Name,
    DateTimeOffset CreatedUtc);

public sealed record AutomaticBackupStatus(
    bool IsConfigured,
    bool IsBusy,
    string DestinationDisplayName,
    DateTimeOffset? LastSuccessfulUtc,
    AutomaticBackupStatusKind Kind,
    AutomaticBackupDestinationAccess DestinationAccess = AutomaticBackupDestinationAccess.Unknown,
    AutomaticBackupFailureKind? LastFailureKind = null,
    DateTimeOffset? LastFailureUtc = null)
{
    public static AutomaticBackupStatus NotConfigured { get; } = new(
        false,
        false,
        string.Empty,
        null,
        AutomaticBackupStatusKind.NotConfigured);
}

public enum AutomaticBackupStatusKind
{
    NotConfigured,
    Ready,
    BackingUp,
    UpToDate,
    Succeeded,
    Failed,
    DestinationUnavailable
}

public enum AutomaticBackupDestinationAccess
{
    Unknown,
    Accessible,
    Unavailable
}

public enum AutomaticBackupFailureKind
{
    DestinationUnavailable,
    VaultUnavailable,
    WriteFailed,
    ConfigurationFailed,
    RecoveryPointMissing,
    UnexpectedFailure
}

public enum AutomaticBackupRunStatus
{
    Created,
    UpToDate,
    NotConfigured,
    Failed
}

public sealed record AutomaticBackupRunResult(
    AutomaticBackupRunStatus Status,
    DateTimeOffset? CreatedUtc = null);

public sealed class AutomaticBackupSnapshot(byte[] payload, string fingerprint) : IDisposable
{
    private byte[]? _payload = payload ?? throw new ArgumentNullException(nameof(payload));

    public ReadOnlyMemory<byte> Payload => _payload
        ?? throw new ObjectDisposedException(nameof(AutomaticBackupSnapshot));

    public string Fingerprint { get; } = string.IsNullOrWhiteSpace(fingerprint)
        ? throw new ArgumentException("Fingerprint required.", nameof(fingerprint))
        : fingerprint;

    public void Dispose()
    {
        var payload = Interlocked.Exchange(ref _payload, null);
        if (payload is not null) System.Security.Cryptography.CryptographicOperations.ZeroMemory(payload);
    }
}
