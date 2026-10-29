namespace TOTP.Avalonia.Mobile.Presentation;

public sealed record MobileBrandIconOption(
    string? Id,
    string DisplayName,
    string? FileName = null,
    bool IsCustomPickerAction = false,
    bool IsPendingCustomIcon = false)
{
    public string DisplayNameForDisplay =>
        Shared.Presentation.IconNameDisplayPolicy.Truncate(DisplayName);

    public bool IsPickerAction => Id is null && !IsPendingCustomIcon;
    public bool IsBrandEntry => !IsPickerAction;

    public override string ToString() => DisplayName;
}
