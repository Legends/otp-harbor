using System.Text.Json.Serialization;

namespace TOTP.Core.Security.Models;

/// <summary>
/// Optional device-local quick-unlock metadata. This wrapper is never a
/// replacement for <see cref="PasswordKeyWrapperV2"/>.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PlatformQuickUnlockWrapperV2
{
    [JsonPropertyName("provider")]
    public required string Provider { get; init; }

    [JsonRequired]
    [JsonPropertyName("providerVersion")]
    public int ProviderVersion { get; init; }

    [JsonPropertyName("authenticationPolicy")]
    public required string AuthenticationPolicy { get; init; }

    /// <summary>
    /// Opaque, non-secret reference to a platform-managed key or secret.
    /// </summary>
    [JsonPropertyName("keyReference")]
    public required string KeyReference { get; init; }

    [JsonPropertyName("wrappedKey")]
    public required PlatformWrappedKeyV2 WrappedKey { get; init; }
}
