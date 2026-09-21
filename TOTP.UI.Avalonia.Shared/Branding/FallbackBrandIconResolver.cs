namespace TOTP.Avalonia.Shared.Branding;

public sealed class FallbackBrandIconResolver : IBrandIconResolver
{
    public static FallbackBrandIconResolver Instance { get; } = new();

    private FallbackBrandIconResolver() { }

    public event EventHandler? CatalogChanged
    {
        add { }
        remove { }
    }

    public bool ShowIssuerLogo => true;

    public BrandInfo Resolve(string? issuer, string? explicitBrandId = null) =>
        BrandInfo.Generic(issuer);

    public BrandInfo ResolveAccount(string? issuer, string? accountName, string? explicitBrandId = null) =>
        BrandInfo.Generic(string.IsNullOrWhiteSpace(issuer) ? accountName : issuer);

    public BrandInfo ResolveAccount(Guid accountId, string? issuer, string? accountName) =>
        ResolveAccount(issuer, accountName);
}
