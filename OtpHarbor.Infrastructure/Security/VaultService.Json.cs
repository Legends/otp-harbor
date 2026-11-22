using System.Text.Json.Serialization;

namespace TOTP.Infrastructure.Security;

public sealed partial class VaultService
{
    [JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
    [JsonSerializable(typeof(VaultAccountsPayload))]
    [JsonSerializable(typeof(VaultProfilePayload))]
    private partial class VaultJsonContext : JsonSerializerContext;
}
