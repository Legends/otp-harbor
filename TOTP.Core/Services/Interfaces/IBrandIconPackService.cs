using FluentResults;
using TOTP.Core.Services.Models;

namespace TOTP.Core.Services.Interfaces;

/// <summary>
/// Imports and resolves a user-supplied local brand-icon archive.
/// The service never receives OTP secrets or account names.
/// </summary>
public interface IBrandIconPackService
{
    event EventHandler? CatalogChanged;

    BrandIconPackStatus Status { get; }

    bool ShowIssuerLogo { get; }

    IReadOnlyList<BrandDefinition> AvailableBrands { get; }

    string? GetAccountBrandId(Guid accountId);

    BrandDefinition? Resolve(string? issuer, string? explicitBrandId = null);

    /// <summary>Resolves an account using its issuer, falling back to its label only when the issuer is missing.</summary>
    BrandDefinition? ResolveAccount(string? issuer, string? accountName, string? explicitBrandId = null);

    bool TryGetIconPathData(string brandId, out string pathData);

    Task<Result<BrandIconPackImportResult>> ImportAsync(
        Stream zipStream,
        CancellationToken cancellationToken = default);

    Task<Result> ResetAsync(CancellationToken cancellationToken = default);

    Task<Result> SetShowIssuerLogoAsync(
        bool showIssuerLogo,
        CancellationToken cancellationToken = default);

    Task<Result> SetAccountBrandIdAsync(
        Guid accountId,
        string? brandId,
        CancellationToken cancellationToken = default);
}
