using System.Text.Json.Serialization;

namespace TOTP.Core.Security.Models;

/// <summary>
/// Parameters and ciphertext required to recover the vault DEK with the
/// master password. All byte arrays are encoded as Base64 in JSON.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PasswordKeyWrapperV2
{
    [JsonPropertyName("kdf")]
    public required Argon2idParametersV2 Kdf { get; init; }

    [JsonPropertyName("wrappedKey")]
    public required AesGcmWrappedKeyV2 WrappedKey { get; init; }
}
