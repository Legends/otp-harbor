namespace TOTP.Avalonia.Desktop.Presentation;

public enum AccountSortMode
{
    Issuer,
    IssuerDescending,
    AccountName,
    AccountNameDescending
}

public sealed record AccountSortOption(
    AccountSortMode Mode,
    string DisplayName,
    bool IsSelected);
