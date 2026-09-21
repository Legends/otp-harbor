using Avalonia.Media;

namespace TOTP.Avalonia.Desktop.Presentation;

public sealed record GroupColorOption(string Hex, string DisplayName)
{
    public IBrush Brush => new SolidColorBrush(Color.Parse(Hex));
}
