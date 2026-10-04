using FluentResults;
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

public enum CustomIconImportFailureReason
{
    Empty,
    TooLarge,
    MalformedXml,
    MissingVectorPath,
    UnsafeContent,
    Unreadable
}

public sealed class CustomIconImportError(CustomIconImportFailureReason reason)
    : Error("The selected custom SVG failed validation.")
{
    public CustomIconImportFailureReason Reason { get; } = reason;
}

public sealed record ImportedIcon(
    string Id,
    string Name,
    byte[] SvgData,
    IReadOnlyList<string> Issuers,
    string ProviderId,
    string BackgroundColor = "#334155",
    string? SourceFileName = null);

public sealed record ImportedIconNotice(string FileName, byte[] Data);

public sealed record IconPackImportResult(
    string ProviderId,
    string ProviderDisplayName,
    string Version,
    BrandIconPackFormat Format,
    IReadOnlyList<ImportedIcon> Icons,
    IReadOnlyList<ImportedIconNotice> Notices);
