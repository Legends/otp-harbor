namespace TOTP.Avalonia.Shared.Branding;

public interface IBrandIconResolver
{
    event EventHandler? CatalogChanged;

    bool ShowIssuerLogo { get; }

    BrandInfo Resolve(string? issuer, string? explicitBrandId = null);

    BrandInfo ResolveAccount(string? issuer, string? accountName, string? explicitBrandId = null);

    BrandInfo ResolveAccount(Guid accountId, string? issuer, string? accountName);
}
