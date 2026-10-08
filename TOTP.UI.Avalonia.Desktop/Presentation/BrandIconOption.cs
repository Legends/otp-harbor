namespace TOTP.Avalonia.Desktop.Presentation;

public sealed record BrandIconOption(string? Id, string DisplayName, string? FileName = null)
{
    public string DisplayNameForDisplay =>
        Shared.Presentation.IconNameDisplayPolicy.Truncate(DisplayName);

    public override string ToString() => DisplayName;
}
