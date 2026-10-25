using TOTP.Avalonia.Shared.Presentation;

namespace TOTP.Avalonia.Desktop.Presentation;

internal static class AccountListFilter
{
    public static IReadOnlyList<AccountListItemViewModel> Apply(
        IReadOnlyList<AccountListItemViewModel> accounts,
        string? searchText,
        Guid? groupId = null,
        bool favoritesOnly = false,
        bool allAccounts = false)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        var candidates = accounts
            .Where(account =>
                (!favoritesOnly || account.IsFavorite)
                && (favoritesOnly || groupId is null
                    || account.Groups.Any(group => group.Id == groupId)))
            .ToArray();

        return RankedTextFilter.ApplyTerms(
            candidates,
            searchText,
            static account => 2 + account.Groups.Count,
            static (account, index) => index switch
            {
                0 => account.Issuer,
                1 => account.AccountName,
                _ => account.Groups[index - 2].Name
            });
    }
}
