using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace TOTP.Avalonia.Desktop.Localization;

public sealed class AvaloniaLanguageFlagProvider : ILanguageFlagProvider, IDisposable
{
    private const string AssetRoot =
        "avares://TOTP.UI.Avalonia.Shared/Assets/flags/";
    private readonly Dictionary<string, Bitmap> _flags = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    public IImage GetFlag(string cultureName)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var normalizedCulture = cultureName.ToLowerInvariant();
        if (normalizedCulture is not ("en" or "de" or "fr" or "es"))
            throw new ArgumentOutOfRangeException(
                nameof(cultureName),
                "Only supported language flags may be loaded.");
        if (_flags.TryGetValue(normalizedCulture, out var cached)) return cached;

        var uri = new Uri($"{AssetRoot}{normalizedCulture}.png");
        using var stream = AssetLoader.Open(uri);
        var flag = new Bitmap(stream);
        _flags.Add(normalizedCulture, flag);
        return flag;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var flag in _flags.Values) flag.Dispose();
        _flags.Clear();
    }
}
