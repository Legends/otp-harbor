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
    string? SourceFileName = null,
    IconPackSourceReference? SelectedSource = null,
    IReadOnlyList<IconPackSourceReference>? Sources = null);

public sealed record ImportedIconNotice(
    string FileName,
    byte[] Data,
    string? RelativePath = null);

public sealed record IconPackSourceReference(
    string Provider,
    string SourceId,
    IReadOnlyDictionary<string, string?> Metadata);

public sealed record IconPackSourceProvenance(
    string Provider,
    string InputFileName,
    string Sha256,
    string? Version,
    string? Revision,
    string? SourceUrl,
    IReadOnlyDictionary<string, string?> Metadata,
    IReadOnlyList<string> LicenseFiles);

public sealed record IconPackIssuerAlias(string Key, string BrandId);

public sealed record IconPackMetadata(
    int FormatVersion,
    string PackId,
    string Name,
    IReadOnlyList<IconPackSourceProvenance> Sources,
    IReadOnlyList<IconPackIssuerAlias> IssuerAliases,
    string? ArchiveSha256 = null);

public sealed record IconPackImportResult(
    string ProviderId,
    string ProviderDisplayName,
    string Version,
    BrandIconPackFormat Format,
    IReadOnlyList<ImportedIcon> Icons,
    IReadOnlyList<ImportedIconNotice> Notices,
    IconPackMetadata? Metadata = null);
