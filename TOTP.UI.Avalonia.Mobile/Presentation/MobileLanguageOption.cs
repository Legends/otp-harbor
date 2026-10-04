namespace TOTP.Avalonia.Mobile.Presentation;

public sealed record MobileLanguageOption(
    string CultureName,
    string DisplayName)
{
    public override string ToString() => DisplayName;
}
