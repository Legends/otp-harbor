using System.Globalization;
using System.Resources;

namespace TOTP.Localization;

public sealed class LocalizationCatalog
{
    private static readonly ResourceManager Common = CreateResources("Common");
    private static readonly ResourceManager Desktop = CreateResources("Desktop");
    private static readonly ResourceManager Mobile = CreateResources("Mobile");
    private static readonly ResourceManager Updater = CreateResources("Updater");
    private readonly ResourceManager _specific;

    public LocalizationCatalog(LocalizationScope scope = LocalizationScope.Common)
    {
        _specific = scope switch
        {
            LocalizationScope.Common => Common,
            LocalizationScope.Desktop => Desktop,
            LocalizationScope.Mobile => Mobile,
            LocalizationScope.Updater => Updater,
            _ => throw new ArgumentOutOfRangeException(nameof(scope))
        };
    }

    public string Get(string key, CultureInfo culture)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var supported = LocalizationCulture.Resolve(culture);
        return _specific.GetString(key, supported)
            ?? Common.GetString(key, supported)
            ?? _specific.GetString(key, LocalizationCulture.English)
            ?? Common.GetString(key, LocalizationCulture.English)
            ?? key;
    }

    public IReadOnlyList<string> GetMissingKeys(IEnumerable<string> keys, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(keys);
        var supported = LocalizationCulture.Resolve(culture);
        var exact = supported.Equals(LocalizationCulture.English) ? CultureInfo.InvariantCulture : supported;
        var specific = _specific.GetResourceSet(exact, createIfNotExists: true, tryParents: false);
        var common = Common.GetResourceSet(exact, createIfNotExists: true, tryParents: false);
        return keys.Where(key => !HasText(specific, key) && !HasText(common, key)).ToArray();
    }

    private static bool HasText(ResourceSet? resources, string key) =>
        resources?.GetObject(key) is string value && !string.IsNullOrWhiteSpace(value);

    private static ResourceManager CreateResources(string name) =>
        new($"TOTP.Localization.Resources.{name}", typeof(LocalizationCatalog).Assembly);
}
