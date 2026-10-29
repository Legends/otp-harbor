using System.Globalization;
using System.Text;

namespace TOTP.Infrastructure.Branding;

internal static class OtpHarborIssuerNormalizer
{
    internal static string Normalize(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var normalized = value.Trim().Normalize(NormalizationForm.FormKC).ToLowerInvariant();
        var result = new StringBuilder(normalized.Length);
        var pendingSeparator = false;

        foreach (var rune in normalized.EnumerateRunes())
        {
            var category = Rune.GetUnicodeCategory(rune);
            if (category is UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter
                or UnicodeCategory.TitlecaseLetter or UnicodeCategory.ModifierLetter
                or UnicodeCategory.OtherLetter or UnicodeCategory.DecimalDigitNumber)
            {
                AppendRetained(result, rune, ref pendingSeparator);
            }
            else if (rune.Value == '&')
            {
                AppendRetained(result, rune, ref pendingSeparator);
            }
            else if (Rune.IsWhiteSpace(rune)
                     || IsSafeSeparator(rune)
                     || category is UnicodeCategory.ConnectorPunctuation
                         or UnicodeCategory.DashPunctuation
                         or UnicodeCategory.OpenPunctuation
                         or UnicodeCategory.ClosePunctuation
                         or UnicodeCategory.InitialQuotePunctuation
                         or UnicodeCategory.FinalQuotePunctuation
                         or UnicodeCategory.OtherPunctuation)
            {
                pendingSeparator = true;
            }
            else if (category is UnicodeCategory.MathSymbol
                     or UnicodeCategory.CurrencySymbol
                     or UnicodeCategory.OtherSymbol)
            {
                AppendRetained(result, rune, ref pendingSeparator);
            }
        }

        return result.ToString();
    }

    private static void AppendRetained(StringBuilder result, Rune rune, ref bool pendingSeparator)
    {
        if (pendingSeparator && result.Length > 0) result.Append(' ');
        result.Append(rune);
        pendingSeparator = false;
    }

    private static bool IsSafeSeparator(Rune rune) =>
        rune.Value is '-' or '_' or '.' or '/' or '\\' or ':';
}
