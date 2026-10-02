using FluentResults;

namespace TOTP.Core.Icons;

/// <summary>
/// Validates and normalizes a single user-selected SVG independently of ZIP packs.
/// </summary>
public interface ICustomIconImporter
{
    Task<Result<ImportedIcon>> ImportAsync(
        CustomIconSource source,
        CancellationToken cancellationToken = default);
}
