using Avalonia.Media;

namespace TOTP.Avalonia.Mobile.Presentation;

public sealed record MobileGroupColorOption(string Hex, string DisplayName)
{
    public IBrush Brush => new SolidColorBrush(Color.Parse(Hex));
}
