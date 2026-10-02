namespace TOTP.Core.Services.Models;

public sealed record BrandDefinition(
    string Id,
    string DisplayName,
    string BackgroundColor,
    string IconFileName);

public sealed record BrandIconPackStatus(
    bool IsInstalled,
    string? Version,
    int BrandCount,
    BrandIconPackFormat? Format = null,
    string? ProviderDisplayName = null);

public enum BrandIconPackFormat
{
    SimpleIcons,
    Aegis,
    FilenameIndexed,
    CustomSvg
}

public sealed record BrandIconPackImportResult(
    string Version,
    int BrandCount,
    BrandIconPackFormat Format,
    string ProviderId = "simple-icons",
    string ProviderDisplayName = "Simple Icons");
