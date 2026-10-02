using System;
using System.Collections.Generic;
using System.Net;
using TOTP.Core.Validation;

namespace TOTP.Infrastructure.Parser
{
    public static class OtpauthParser
    {
        public static string NormalizeBase32SecretForUri(string secret)
        {
            return SecretValidation.NormalizeBase32Secret(secret);
        }

        public sealed class TOTPData
        {
            /// <summary>
            /// aka Token
            /// </summary>
            public string Label { get; init; } = "";
            public string? Issuer { get; init; }
            public string SecretBase32 { get; init; } = "";
            public string Algorithm { get; init; } = "SHA1";
            public int Digits { get; init; } = 6;
            public int Period { get; init; } = 30;
        }

        public static TOTPData Parse(string otpauthUri)
        {
            // Basic checks
            if (string.IsNullOrWhiteSpace(otpauthUri) || !otpauthUri.StartsWith("otpauth://", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Not an otpauth URI.");

            var uri = new Uri(otpauthUri);
            if (!string.Equals(uri.Host, "totp", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Only TOTP is supported in this parser.");

            // The 'path' is like "/Issuer:Label" or "/Label"
            var path = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/'));
            string label = path;
            string? issuerFromPath = null;
            var colonIdx = path.IndexOf(':');
            if (colonIdx >= 0)
            {
                issuerFromPath = path.Substring(0, colonIdx);
                label = path.Substring(colonIdx + 1);
            }

            var query = ParseQuery(uri.Query);
            if (!query.TryGetValue("secret", out var secret) || string.IsNullOrWhiteSpace(secret))
                throw new ArgumentException("Missing 'secret' parameter.");

            var normalizedSecret = NormalizeBase32SecretForUri(secret);
            if (!SecretValidation.IsValidBase32Secret(normalizedSecret))
                throw new ArgumentException("Invalid Base32 secret.");

            query.TryGetValue("issuer", out var issuerParam);
            query.TryGetValue("algorithm", out var alg);
            query.TryGetValue("digits", out var digitsStr);
            query.TryGetValue("period", out var periodStr);

            var issuer = issuerParam ?? issuerFromPath;
            var digits = 6;
            if (digitsStr is not null && !int.TryParse(digitsStr, out digits))
                throw new ArgumentException("Invalid 'digits' parameter.");
            var period = TotpPeriodPolicy.DefaultSeconds;
            if (periodStr is not null && !int.TryParse(periodStr, out period))
                throw new ArgumentException("Invalid 'period' parameter.");

            return new TOTPData
            {
                Label = label,
                Issuer = issuer,
                SecretBase32 = normalizedSecret,
                Algorithm = string.IsNullOrWhiteSpace(alg) ? "SHA1" : alg!,
                Digits = digits,
                Period = period
            };
        }

        private static Dictionary<string, string> ParseQuery(string query)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(query)) return dict;

            foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = part.Split('=', 2);
                var key = WebUtility.UrlDecode(kv[0]);
                var val = kv.Length > 1 ? WebUtility.UrlDecode(kv[1]) : "";
                dict[key] = val;
            }

            return dict;
        }
    }
}
