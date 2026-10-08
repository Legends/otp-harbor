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
            static (control, _) => control.UpdateIconPseudoClass());
        IconImageProperty.Changed.AddClassHandler<BrandTile>(
            static (control, _) => control.UpdateIconPseudoClass());
        IsCustomIconProperty.Changed.AddClassHandler<BrandTile>(
            static (control, _) => control.PseudoClasses.Set(":custom-icon", control.IsCustomIcon));
    }

    public static readonly StyledProperty<IBrush?> TileBackgroundProperty =
        AvaloniaProperty.Register<BrandTile, IBrush?>(nameof(TileBackground));

    public static readonly StyledProperty<string?> IconDataProperty =
        AvaloniaProperty.Register<BrandTile, string?>(nameof(IconData));

    public static readonly StyledProperty<Transform?> IconTransformProperty =
        AvaloniaProperty.Register<BrandTile, Transform?>(nameof(IconTransform));

    public static readonly StyledProperty<IImage?> IconImageProperty =
        AvaloniaProperty.Register<BrandTile, IImage?>(nameof(IconImage));

    public static readonly StyledProperty<bool> IsCustomIconProperty =
        AvaloniaProperty.Register<BrandTile, bool>(nameof(IsCustomIcon));

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

    public Transform? IconTransform
    {
        get => GetValue(IconTransformProperty);
        set => SetValue(IconTransformProperty, value);
    }

    public IImage? IconImage
    {
        get => GetValue(IconImageProperty);
        set => SetValue(IconImageProperty, value);
    }

    public bool IsCustomIcon
    {
        get => GetValue(IsCustomIconProperty);
        set => SetValue(IsCustomIconProperty, value);
    }

    public string Initials
    {
        get => GetValue(InitialsProperty);
        set => SetValue(InitialsProperty, value ?? "?");
    }

    private void UpdateIconPseudoClass() =>
        PseudoClasses.Set(":has-icon", !string.IsNullOrWhiteSpace(IconData) || IconImage is not null);
}
