using System.Globalization;
using TOTP.Localization;

namespace TOTP.Avalonia.Desktop.Localization;

public sealed class AvaloniaStringCatalog
{
    private readonly LocalizationCatalog _resources = new(LocalizationScope.Desktop);

    public string Get(string key, CultureInfo culture)
    {
        return _resources.Get(key, culture);
    }

    public IReadOnlyList<string> GetMissingKeys(CultureInfo culture)
    {
        return _resources.GetMissingKeys(AvaloniaStringKeys.All, culture);
    }
}
