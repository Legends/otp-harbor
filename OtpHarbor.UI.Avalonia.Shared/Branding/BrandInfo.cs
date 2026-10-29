using Avalonia.Media;
using Avalonia.Styling;
using TOTP.Avalonia.Shared.Styles;
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
    BrandIconTransform? SourceTransform = null,
    BrandRuntimeIcon? RuntimeIcon = null,
    BrandRuntimeAppearances? RuntimeAppearances = null)
{
    public bool HasVectorIcon => IconData is not null
        || IconImage is not null
        || IconLayers is { Count: > 0 };

    public bool HasRuntimeIcon => RuntimeIcon is not null || RuntimeAppearances is not null;

    public bool HasIcon => HasRuntimeIcon || HasVectorIcon;

    public BrandRuntimeAppearance? RuntimeAppearanceFor(ThemeVariant? theme)
    {
        if (RuntimeAppearances is null) return null;
        var iconTheme = theme == ThemeVariant.Dark || theme == AvaloniaThemeVariants.HighContrast
            ? BrandIconTheme.Dark
            : BrandIconTheme.Light;
        return RuntimeAppearances.ForTheme(iconTheme);
    }

    public BrandRuntimeIcon? RuntimeIconFor(ThemeVariant? theme) =>
        RuntimeAppearanceFor(theme)?.Asset ?? RuntimeIcon;

    public static BrandInfo Generic(string? issuer)
    {
        var displayName = string.IsNullOrWhiteSpace(issuer) ? string.Empty : issuer.Trim();
        var characters = displayName.Where(char.IsLetterOrDigit).Take(2).ToArray();
        var initials = characters.Length == 0 ? "?" : new string(characters).ToUpperInvariant();
        const string color = "#334155";
        return new BrandInfo(null, displayName, initials, color, new SolidColorBrush(Color.Parse(color)), null);
    }
}
