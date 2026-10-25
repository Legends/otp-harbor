namespace TOTP.Avalonia.Shared.Branding;

public interface IBrandIconResolver
{
    event EventHandler? CatalogChanged;

    bool ShowIssuerLogo { get; }

    BrandInfo Resolve(string? issuer, string? explicitBrandId = null);

    BrandInfo ResolveAccount(string? issuer, string? accountName, string? explicitBrandId = null);

    BrandInfo ResolveAccount(Guid accountId, string? issuer, string? accountName);

    BrandInfo ResolveAccountMetadata(Guid accountId, string? issuer, string? accountName) =>
        ResolveAccount(accountId, issuer, accountName);

    BrandInfo Materialize(BrandInfo brand) => brand;

    BrandInfo MaterializeIconData(BrandInfo brand) => Materialize(brand);

    bool TryGetMaterializedIconData(BrandInfo brand, out BrandInfo materialized)
    {
        materialized = brand;
        return brand.HasIcon;
    }
}
