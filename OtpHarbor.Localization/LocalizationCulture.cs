using System.Globalization;

namespace TOTP.Localization;

public static class LocalizationCulture
{
    public static CultureInfo English { get; } = CultureInfo.GetCultureInfo("en");

    public static IReadOnlyList<CultureInfo> Supported { get; } = Array.AsReadOnly(
        new[] { English, CultureInfo.GetCultureInfo("de"), CultureInfo.GetCultureInfo("fr"), CultureInfo.GetCultureInfo("es") });

    public static CultureInfo Resolve(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        return Supported.FirstOrDefault(candidate =>
            candidate.TwoLetterISOLanguageName.Equals(culture.TwoLetterISOLanguageName, StringComparison.OrdinalIgnoreCase)) ?? English;
    }

    public static CultureInfo Resolve(string? cultureName)
    {
        if (string.IsNullOrWhiteSpace(cultureName)) return English;
        try { return Resolve(CultureInfo.GetCultureInfo(cultureName)); }
        catch (CultureNotFoundException) { return English; }
    }
}
