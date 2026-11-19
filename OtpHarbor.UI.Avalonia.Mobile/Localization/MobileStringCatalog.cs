using System.Globalization;
using TOTP.Localization;

namespace TOTP.Avalonia.Mobile.Localization;

public sealed class MobileStringCatalog
{
    private readonly LocalizationCatalog _resources = new(LocalizationScope.Mobile);

    public MobileStringCatalog()
        : this(CultureInfo.CurrentUICulture)
    {
    }

    public MobileStringCatalog(CultureInfo requestedCulture)
    {
        ArgumentNullException.ThrowIfNull(requestedCulture);
        Culture = LocalizationCulture.Resolve(requestedCulture);
    }

    public CultureInfo Culture { get; private set; }

    public void ApplyCulture(string? cultureName)
    {
        Culture = LocalizationCulture.Resolve(cultureName);
    }

    public string Get(string key)
    {
        return _resources.Get(key, Culture);
    }

    public IReadOnlyList<string> GetMissingKeys(CultureInfo culture)
    {
        return _resources.GetMissingKeys(MobileStringKeys.All, culture);
    }
}
