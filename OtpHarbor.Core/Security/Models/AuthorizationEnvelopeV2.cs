using System.Text.Json.Serialization;

namespace TOTP.Core.Security.Models;

/// <summary>
/// Portable wire contract for the password-recovery authorization envelope.
/// Platform quick-unlock wrappers are added separately and must never replace
/// the password wrapper as the recovery path.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AuthorizationEnvelopeV2
{
    public const string FormatIdentifier = "totp-authorization-envelope";
    public const int CurrentVersion = 2;

    [JsonRequired]
    [JsonPropertyName("format")]
    public string Format { get; init; } = FormatIdentifier;

    [JsonRequired]
    [JsonPropertyName("version")]
    public int Version { get; init; } = CurrentVersion;

    [JsonPropertyName("passwordWrapper")]
    public required PasswordKeyWrapperV2 PasswordWrapper { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("quickUnlockWrapper")]
    public PlatformQuickUnlockWrapperV2? QuickUnlockWrapper { get; init; }
}
