using TOTP.Core.Services.Models;

namespace TOTP.Core.Icons;

public sealed class IconPackSource
{
    public required Stream Stream { get; init; }

    public string? FileName { get; init; }
}

public sealed class CustomIconSource
{
    public required Stream Stream { get; init; }

    public string? FileName { get; init; }

    public string? DisplayName { get; init; }

    public IReadOnlyList<string> Issuers { get; init; } = [];
}

public sealed record ImportedIcon(
    string Id,
    string Name,
    byte[] SvgData,
    IReadOnlyList<string> Issuers,
    string ProviderId,
    string BackgroundColor = "#334155");

public sealed record ImportedIconNotice(string FileName, byte[] Data);

public sealed record IconPackImportResult(
    string ProviderId,
    string ProviderDisplayName,
    string Version,
    BrandIconPackFormat Format,
    IReadOnlyList<ImportedIcon> Icons,
    IReadOnlyList<ImportedIconNotice> Notices);
