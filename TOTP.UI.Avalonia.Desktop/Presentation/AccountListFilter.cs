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
        var terms = (searchText ?? string.Empty)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (terms.Length == 0 && !favoritesOnly && !allAccounts)
            return groupId is null
                ? accounts.Where(account => account.Group is null).ToArray()
                : accounts.Where(account => account.Group?.Id == groupId).ToArray();

        return accounts
            .Where(account =>
                (!favoritesOnly || account.IsFavorite)
                && (favoritesOnly || allAccounts || groupId is null || account.Group?.Id == groupId)
                && terms.All(term =>
                    account.Issuer.Contains(term, StringComparison.OrdinalIgnoreCase)
                    || account.AccountName.Contains(term, StringComparison.OrdinalIgnoreCase)
                    || (account.Group?.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)))
            .ToArray();
    }
}
