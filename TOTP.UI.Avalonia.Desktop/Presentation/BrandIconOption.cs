namespace TOTP.Avalonia.Desktop.Presentation;

public sealed record BrandIconOption(
    string? Id,
    string DisplayName,
    string? FileName = null,
    bool IsCustomPickerAction = false,
    bool IsPendingCustomIcon = false)
{
    public string DisplayNameForDisplay =>
        Shared.Presentation.IconNameDisplayPolicy.Truncate(DisplayName);

    public override string ToString() => DisplayName;
}
