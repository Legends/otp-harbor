using System.Globalization;
using System.Resources;

namespace TOTP.Avalonia.Mobile.Localization;

public sealed class MobileStringCatalog
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en");
    private readonly ResourceManager _resources = new(
        "TOTP.Avalonia.Mobile.Localization.Strings",
        typeof(MobileStringCatalog).Assembly);

    public MobileStringCatalog()
        : this(CultureInfo.CurrentUICulture)
    {
    }

    public MobileStringCatalog(CultureInfo requestedCulture)
    {
        ArgumentNullException.ThrowIfNull(requestedCulture);
        Culture = ResolveCulture(requestedCulture.TwoLetterISOLanguageName);
    }

    public CultureInfo Culture { get; private set; }

    public void ApplyCulture(string? cultureName)
    {
        Culture = ResolveCulture(cultureName);
    }

    public string Get(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return _resources.GetString(key, Culture)
            ?? _resources.GetString(key, English)
            ?? key;
    }

    public IReadOnlyList<string> GetMissingKeys(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        var exactCulture = culture.TwoLetterISOLanguageName.Equals(
            "en",
            StringComparison.OrdinalIgnoreCase)
            ? CultureInfo.InvariantCulture
            : culture;
        var resourceSet = _resources.GetResourceSet(
            exactCulture,
            createIfNotExists: true,
            tryParents: false);
        return MobileStringKeys.All
            .Where(key => resourceSet?.GetObject(key) is not string value
                || string.IsNullOrWhiteSpace(value))
            .ToArray();
    }

    private static CultureInfo ResolveCulture(string? cultureName) =>
        cultureName?.ToLowerInvariant() switch
        {
            "de" => CultureInfo.GetCultureInfo("de"),
            "fr" => CultureInfo.GetCultureInfo("fr"),
            "es" => CultureInfo.GetCultureInfo("es"),
            _ => English
        };
}
