namespace TOTP.Avalonia.Mobile.Presentation;

public sealed record MobileBrandIconOption(
    string? Id,
    string DisplayName,
    string? FileName = null)
{
    public string DisplayNameForDisplay =>
        Shared.Presentation.IconNameDisplayPolicy.Truncate(DisplayName);

    public override string ToString() => DisplayName;
}
