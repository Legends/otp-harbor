namespace TOTP.Core.Services.Models;

public sealed record BrandDefinition(
    string Id,
    string DisplayName,
    string BackgroundColor,
    string IconFileName,
    string? SourceFileName = null);

public sealed record BrandIconTransform(
    double M11,
    double M12,
    double M21,
    double M22,
    double M31,
    double M32);

public sealed record BrandIconViewport(
    double X,
    double Y,
    double Width,
    double Height);

public sealed record BrandIconStroke(
    string Color,
    double Width);

public sealed record BrandIconLayer(
    string PathData,
    string? FillColor = null,
    BrandIconTransform? Transform = null,
    BrandIconViewport? Viewport = null,
    BrandIconStroke? Stroke = null);

public sealed record BrandIconPackStatus(
    bool IsInstalled,
    string? Version,
    int BrandCount,
    BrandIconPackFormat? Format = null,
    string? ProviderDisplayName = null,
    IReadOnlyList<BrandIconPackInstallation>? InstalledPacks = null);

public sealed record BrandIconPackInstallation(
    string ProviderId,
    string ProviderDisplayName,
    string Version,
    int BrandCount,
    BrandIconPackFormat Format,
    int Priority);

public enum BrandIconPackFormat
{
    OtpHarbor,
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
