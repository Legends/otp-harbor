using System.Text.RegularExpressions;

namespace TOTP.Infrastructure.Logging;

public static partial class SensitiveTextRedactor
{
    private const string Redacted = "[REDACTED]";

    [GeneratedRegex(@"(?i)\b(password|passwd|pwd|secret|seed|token|credential|apikey|api_key|client_secret|masterpassword|pfxpassword|wrappeddek|dek)\s*[:=]\s*([^\s,;""'&]+)")]
    private static partial Regex KeyValuePattern();

    [GeneratedRegex(@"(?i)([?&](secret|password|token|apikey|api_key|client_secret)=)([^&\s]+)")]
    private static partial Regex QueryParameterPattern();

    [GeneratedRegex(@"(?i)\bBearer\s+[A-Za-z0-9\-._~+/]+=*")]
    private static partial Regex BearerPattern();

    [GeneratedRegex(@"(?i)([\""'](?:password|passwd|pwd|secret|seed|token|credential|apikey|api_key|client_secret|masterpassword|pfxpassword|wrappeddek|dek)[\""']\s*:\s*[\""'])([^\""']*)([\""'])")]
    private static partial Regex QuotedPropertyPattern();

    [GeneratedRegex(@"(?i)\botpauth://[^\s\""']+")]
    private static partial Regex OtpAuthUriPattern();

    public static string Sanitize(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var sanitized = QuotedPropertyPattern().Replace(
            text,
            m => $"{m.Groups[1].Value}{Redacted}{m.Groups[3].Value}");
        sanitized = OtpAuthUriPattern().Replace(sanitized, $"otpauth://{Redacted}");
        sanitized = KeyValuePattern().Replace(sanitized, m => $"{m.Groups[1].Value}={Redacted}");
        sanitized = QueryParameterPattern().Replace(sanitized, m => $"{m.Groups[1].Value}{Redacted}");
        sanitized = BearerPattern().Replace(sanitized, $"Bearer {Redacted}");
        return sanitized;
    }
}
