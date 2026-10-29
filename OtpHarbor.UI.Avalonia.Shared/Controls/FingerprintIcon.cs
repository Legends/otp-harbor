using Avalonia;
using Avalonia.Controls.Primitives;

namespace TOTP.Avalonia.Shared.Controls;

public sealed class FingerprintIcon : TemplatedControl
{
    public static readonly StyledProperty<double> IconSizeProperty =
        AvaloniaProperty.Register<FingerprintIcon, double>(nameof(IconSize), 46d);

    public double IconSize
    {
        get => GetValue(IconSizeProperty);
        set => SetValue(IconSizeProperty, value);
    }
}
