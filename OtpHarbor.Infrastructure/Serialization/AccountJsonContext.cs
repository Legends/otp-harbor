using System.Text.Json.Serialization;
using TOTP.Core.Models;

namespace TOTP.Infrastructure.Serialization;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(List<Account>))]
[JsonSerializable(typeof(IEnumerable<Account>))]
internal partial class AccountJsonContext : JsonSerializerContext;
