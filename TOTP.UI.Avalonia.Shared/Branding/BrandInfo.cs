using Avalonia.Media;
using TOTP.Core.Services.Models;

namespace TOTP.Avalonia.Shared.Branding;

public sealed record BrandInfo(
    string? Id,
    string DisplayName,
    string Initials,
    string BackgroundColor,
    IBrush BackgroundBrush,
    string? IconData,
    Transform? IconTransform = null,
    IImage? IconImage = null,
    bool IsCustomIcon = false,
    IReadOnlyList<BrandIconLayer>? IconLayers = null,
    BrandIconTransform? SourceTransform = null)
{
    public bool HasIcon => IconData is not null || IconImage is not null || IconLayers is { Count: > 0 };

    public static BrandInfo Generic(string? issuer)
    {
        var displayName = string.IsNullOrWhiteSpace(issuer) ? string.Empty : issuer.Trim();
        var characters = displayName.Where(char.IsLetterOrDigit).Take(2).ToArray();
        var initials = characters.Length == 0 ? "?" : new string(characters).ToUpperInvariant();
        const string color = "#334155";
        return new BrandInfo(null, displayName, initials, color, new SolidColorBrush(Color.Parse(color)), null);
    }
}
