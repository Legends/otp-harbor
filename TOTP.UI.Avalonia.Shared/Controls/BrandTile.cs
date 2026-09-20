using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

namespace TOTP.Avalonia.Shared.Controls;

public sealed class BrandTile : TemplatedControl
{
    static BrandTile()
    {
        IconDataProperty.Changed.AddClassHandler<BrandTile>(
            static (control, _) => control.PseudoClasses.Set(":has-icon", !string.IsNullOrWhiteSpace(control.IconData)));
    }

    public static readonly StyledProperty<IBrush?> TileBackgroundProperty =
        AvaloniaProperty.Register<BrandTile, IBrush?>(nameof(TileBackground));

    public static readonly StyledProperty<string?> IconDataProperty =
        AvaloniaProperty.Register<BrandTile, string?>(nameof(IconData));

    public static readonly StyledProperty<string> InitialsProperty =
        AvaloniaProperty.Register<BrandTile, string>(nameof(Initials), "?");

    public IBrush? TileBackground
    {
        get => GetValue(TileBackgroundProperty);
        set => SetValue(TileBackgroundProperty, value);
    }

    public string? IconData
    {
        get => GetValue(IconDataProperty);
        set => SetValue(IconDataProperty, value);
    }

    public string Initials
    {
        get => GetValue(InitialsProperty);
        set => SetValue(InitialsProperty, value ?? "?");
    }
}
