using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace TOTP.Avalonia.Shared.Controls;

public sealed class LanguageFlagImage : Image
{
    private static readonly object CacheGate = new();
    private static readonly Dictionary<string, Bitmap> Cache =
        new(StringComparer.OrdinalIgnoreCase);

    public static readonly StyledProperty<string?> CultureNameProperty =
        AvaloniaProperty.Register<LanguageFlagImage, string?>(nameof(CultureName));

    static LanguageFlagImage()
    {
        CultureNameProperty.Changed.AddClassHandler<LanguageFlagImage>(
            static (control, _) => control.UpdateSource());
    }

    public string? CultureName
    {
        get => GetValue(CultureNameProperty);
        set => SetValue(CultureNameProperty, value);
    }

    private void UpdateSource()
    {
        var cultureName = CultureName?.Trim().ToLowerInvariant();
        if (cultureName is not ("en" or "de" or "fr" or "es"))
        {
            Source = null;
            return;
        }

        lock (CacheGate)
        {
            if (!Cache.TryGetValue(cultureName, out var bitmap))
            {
                var uri = new Uri(
                    $"avares://TOTP.UI.Avalonia.Shared/Assets/flags/{cultureName}.png");
                using var stream = AssetLoader.Open(uri);
                bitmap = new Bitmap(stream);
                Cache.Add(cultureName, bitmap);
            }

            Source = bitmap;
        }
    }
}
