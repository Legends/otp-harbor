namespace TOTP.Avalonia.Desktop.Presentation;

public enum AccountSortMode
{
    Issuer,
    AccountName
}

public sealed record AccountSortOption(AccountSortMode Mode, string DisplayName);
