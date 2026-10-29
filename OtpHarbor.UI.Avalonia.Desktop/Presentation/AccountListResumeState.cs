namespace TOTP.Avalonia.Desktop.Presentation;

public sealed record AccountListResumeState(
    AccountListResumeTarget Target,
    Guid? EntityId,
    Guid? SelectedGroupId,
    bool FavoritesFilter,
    bool AllAccountsFilter);

public enum AccountListResumeTarget
{
    None,
    AccountEditor,
    GroupEditor,
    FavoritesEditor
}
