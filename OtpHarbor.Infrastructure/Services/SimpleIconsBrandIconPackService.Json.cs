using System.Text.Json.Serialization;

namespace TOTP.Infrastructure.Services;

public sealed partial class SimpleIconsBrandIconPackService
{
    [JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
    [JsonSerializable(typeof(CurrentPack))]
    [JsonSerializable(typeof(InstalledPackRegistry))]
    [JsonSerializable(typeof(BrandDisplaySettings))]
    [JsonSerializable(typeof(AccountBrandSettings))]
    [JsonSerializable(typeof(CustomIconIndex))]
    [JsonSerializable(typeof(BrandIndex))]
    [JsonSerializable(typeof(AccountIndexHeader))]
    [JsonSerializable(typeof(IndexBrand))]
    private partial class IconJsonContext : JsonSerializerContext;
}
