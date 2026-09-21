namespace TOTP.Avalonia.Desktop.Presentation;

public sealed record BrandIconOption(string? Id, string DisplayName)
{
    public override string ToString() => DisplayName;
}
