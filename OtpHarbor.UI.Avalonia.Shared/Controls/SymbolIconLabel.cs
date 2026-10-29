using Avalonia;
using Avalonia.Controls.Primitives;

namespace TOTP.Avalonia.Shared.Controls;

public sealed class SymbolIconLabel : TemplatedControl
{
    public static readonly StyledProperty<SymbolIconKind> KindProperty =
        AvaloniaProperty.Register<SymbolIconLabel, SymbolIconKind>(
            nameof(Kind),
            SymbolIconKind.Information);

    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<SymbolIconLabel, string>(nameof(Text), string.Empty);

    public static readonly StyledProperty<double> IconSizeProperty =
        AvaloniaProperty.Register<SymbolIconLabel, double>(nameof(IconSize), 16d);

    public SymbolIconKind Kind
    {
        get => GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public double IconSize
    {
        get => GetValue(IconSizeProperty);
        set => SetValue(IconSizeProperty, value);
    }
}
