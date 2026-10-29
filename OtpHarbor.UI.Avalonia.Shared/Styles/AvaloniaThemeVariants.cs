using Avalonia.Styling;

namespace TOTP.Avalonia.Shared.Styles;

public static class AvaloniaThemeVariants
{
    public static ThemeVariant HighContrast { get; } =
        new("HighContrast", ThemeVariant.Dark);
}
