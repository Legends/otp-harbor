using System.Text.Json.Serialization;

namespace TOTP.Core.Security.Models;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AesGcmWrappedKeyV2
{
    public const string AlgorithmIdentifier = "aes-256-gcm";
    public const string AssociatedDataContext = "totp-manager/authorization-envelope/v2/password-wrapper";

    [JsonRequired]
    [JsonPropertyName("algorithm")]
    public string Algorithm { get; init; } = AlgorithmIdentifier;

    [JsonPropertyName("nonce")]
    public required byte[] Nonce { get; init; }

    /// <summary>
    /// Ciphertext with the 16-byte authentication tag appended.
    /// </summary>
    [JsonPropertyName("ciphertext")]
    public required byte[] Ciphertext { get; init; }
}
