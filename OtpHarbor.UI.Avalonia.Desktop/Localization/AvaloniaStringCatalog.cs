using System.Globalization;
using System.Resources;

namespace TOTP.Avalonia.Desktop.Localization;

public sealed class AvaloniaStringCatalog
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en");
    private readonly ResourceManager _resources = new(
        "TOTP.Avalonia.Desktop.Localization.Strings",
        typeof(AvaloniaStringCatalog).Assembly);

    public string Get(string key, CultureInfo culture)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(culture);
        var supportedCulture = culture.TwoLetterISOLanguageName.ToLowerInvariant() switch
        {
            "de" => CultureInfo.GetCultureInfo("de"),
            "fr" => CultureInfo.GetCultureInfo("fr"),
            "es" => CultureInfo.GetCultureInfo("es"),
            _ => English
        };
        return _resources.GetString(key, supportedCulture)
            ?? _resources.GetString(key, English)
            ?? key;
    }

    public IReadOnlyList<string> GetMissingKeys(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        var exactCulture = culture.TwoLetterISOLanguageName.Equals("en", StringComparison.OrdinalIgnoreCase)
            ? CultureInfo.InvariantCulture
            : culture;
        var resourceSet = _resources.GetResourceSet(exactCulture, createIfNotExists: true, tryParents: false);
        return AvaloniaStringKeys.All
            .Where(key => resourceSet?.GetObject(key) is not string value || string.IsNullOrWhiteSpace(value))
            .ToArray();
    }
}
