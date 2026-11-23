using System.Text.Json.Serialization;
using TOTP.Core.Backup;
using TOTP.Core.Models;
using TOTP.Core.Security.Models;

namespace TOTP.Core.Serialization;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(AppPreferencesV1))]
[JsonSerializable(typeof(AuthorizationEnvelopeV2))]
[JsonSerializable(typeof(AutomaticBackupConfigurationV1))]
[JsonSerializable(typeof(AutomaticBackupPackageV1))]
internal partial class CoreJsonContext : JsonSerializerContext;
