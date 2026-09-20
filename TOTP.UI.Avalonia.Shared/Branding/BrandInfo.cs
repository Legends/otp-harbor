using Avalonia.Media;

namespace TOTP.Avalonia.Shared.Branding;

public sealed record BrandInfo(
    string? Id,
    string DisplayName,
    string Initials,
    string BackgroundColor,
    IBrush BackgroundBrush,
    string? IconData)
{
    public bool HasIcon => IconData is not null;

    public static BrandInfo Generic(string? issuer)
    {
        var displayName = string.IsNullOrWhiteSpace(issuer) ? string.Empty : issuer.Trim();
        var characters = displayName.Where(char.IsLetterOrDigit).Take(2).ToArray();
        var initials = characters.Length == 0 ? "?" : new string(characters).ToUpperInvariant();
        const string color = "#334155";
        return new BrandInfo(null, displayName, initials, color, new SolidColorBrush(Color.Parse(color)), null);
    }
}
