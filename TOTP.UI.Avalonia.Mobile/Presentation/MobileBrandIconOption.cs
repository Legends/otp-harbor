namespace TOTP.Avalonia.Mobile.Presentation;

public sealed record MobileBrandIconOption(
    string? Id,
    string DisplayName,
    string? FileName = null)
{
    public override string ToString() => DisplayName;
}
