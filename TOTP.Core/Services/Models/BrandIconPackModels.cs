namespace TOTP.Core.Services.Models;

public sealed record BrandDefinition(
    string Id,
    string DisplayName,
    string BackgroundColor,
    string? IconFileName,
    string? SourceFileName = null,
    BrandRuntimeIcon? RuntimeIcon = null,
    BrandRuntimeAppearances? RuntimeAppearances = null);

public sealed record BrandRuntimeIcon(
    string CacheKey,
    string? FilePath,
    string Sha256,
    int Width,
    int Height,
    string? ArchivePath = null,
    string? ArchiveEntryPath = null,
    string? BrandId = null,
    BrandIconTheme? Theme = null)
{
    public bool IsArchiveBacked => ArchivePath is not null && ArchiveEntryPath is not null;
}

public enum BrandIconTheme
{
    Light,
    Dark
}

public enum BrandIconAssetRole
{
    Foreground,
    CompleteIcon
}

public enum BrandIconBackgroundMode
{
    Solid,
    Embedded
}

public enum BrandIconAppearanceSelection
{
    NativeThemeVariant,
    BrandColorForeground,
    CompleteSourceArtwork,
    SourceDefault,
    ReviewedBadgeComposition
}

public sealed record BrandIconAppearanceSource(
    string Provider,
    string SourceId,
    string AssetPath);

public sealed record BrandRuntimeAppearance(
    BrandRuntimeIcon Asset,
    BrandIconAssetRole AssetRole,
    BrandIconBackgroundMode BackgroundMode,
    string? BackgroundColor,
    BrandIconAppearanceSelection Selection,
    BrandIconAppearanceSource Source,
    string AppearanceSelectionReason);

public sealed record BrandRuntimeAppearances(
    BrandRuntimeAppearance LightTheme,
    BrandRuntimeAppearance DarkTheme)
{
    public BrandRuntimeAppearance ForTheme(BrandIconTheme theme) =>
        theme == BrandIconTheme.Dark ? DarkTheme : LightTheme;
}

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
