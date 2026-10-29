using System.Text.Json.Serialization;

namespace TOTP.Core.Security.Models;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PlatformWrappedKeyV2
{
    [JsonPropertyName("algorithm")]
    public required string Algorithm { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("nonce")]
    public byte[]? Nonce { get; init; }

    [JsonPropertyName("ciphertext")]
    public required byte[] Ciphertext { get; init; }
}
