namespace TOTP.Avalonia.Desktop.Presentation;

public sealed record BrandIconOption(string? Id, string DisplayName, string? FileName = null)
{
    public override string ToString() => DisplayName;
}
