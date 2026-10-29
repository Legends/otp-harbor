using System.Text.Json.Serialization;

namespace TOTP.Core.Security.Models;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record Argon2idParametersV2
{
    public const string AlgorithmIdentifier = "argon2id";
    public const int CurrentAlgorithmVersion = 19;

    [JsonRequired]
    [JsonPropertyName("algorithm")]
    public string Algorithm { get; init; } = AlgorithmIdentifier;

    [JsonRequired]
    [JsonPropertyName("version")]
    public int Version { get; init; } = CurrentAlgorithmVersion;

    [JsonPropertyName("salt")]
    public required byte[] Salt { get; init; }

    [JsonRequired]
    [JsonPropertyName("passes")]
    public int Passes { get; init; }

    [JsonRequired]
    [JsonPropertyName("memoryKiB")]
    public int MemoryKiB { get; init; }

    [JsonRequired]
    [JsonPropertyName("parallelism")]
    public int Parallelism { get; init; }
}
