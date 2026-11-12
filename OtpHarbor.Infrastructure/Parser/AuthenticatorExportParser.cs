using System.Text.Json;
using FluentResults;
using TOTP.Core.Common;
using TOTP.Core.Models;
using TOTP.Core.Services.Models;
using TOTP.Core.Validation;

namespace TOTP.Infrastructure.Parser;

internal static class AuthenticatorExportParser
{
    public static Result<List<Account>> ParseBitwarden(JsonElement root)
    {
        if (!HasUniqueProperties(root)) return Rejected(0, 1, 0, 0);
        if (!root.TryGetProperty("encrypted", out var encrypted) || encrypted.ValueKind != JsonValueKind.False)
            return Rejected(0, 0, 1, 0);
        var items = root.GetProperty("items");
        if (items.ValueKind != JsonValueKind.Array) return Rejected(0, 1, 0, 0);
        return Read(items.EnumerateArray(), item =>
        {
            if (item.ValueKind != JsonValueKind.Object) throw new FormatException("Invalid authenticator item.");
            RequireUniqueProperties(item);
            if (!item.TryGetProperty("type", out var type) || !type.TryGetInt32(out var number))
                throw new FormatException("Invalid authenticator item type.");
            if (number != 1) return null;
            if (!item.TryGetProperty("login", out var login) || login.ValueKind == JsonValueKind.Null) return null;
            RequireUniqueProperties(login);
            var totp = OptionalString(login, "totp");
            if (string.IsNullOrWhiteSpace(totp)) return null;
            return FromValue(totp, RequiredString(item, "name"), OptionalString(login, "username"),
                item.TryGetProperty("favorite", out var favorite) && favorite.ValueKind == JsonValueKind.True);
        });
    }

    public static Result<List<Account>> ParseProton(JsonElement root)
    {
        if (!HasUniqueProperties(root)) return Rejected(0, 1, 0, 0);
        if (!root.TryGetProperty("version", out var version) || !version.TryGetInt32(out var number) || number != 1
            || root.TryGetProperty("salt", out _) || root.TryGetProperty("encryptedData", out _))
            return Rejected(0, 0, 1, 0);
        var entries = root.GetProperty("entries");
        if (entries.ValueKind != JsonValueKind.Array) return Rejected(0, 1, 0, 0);
        return Read(entries.EnumerateArray(), entry =>
        {
            RequireUniqueProperties(entry);
            var content = entry.GetProperty("content");
            RequireUniqueProperties(content);
            if (RequiredString(content, "entry_type") != "Totp") throw new UnsupportedOtpException();
            return FromValue(RequiredString(content, "uri"), null, null, false, requireUri: true);
        });
    }

    public static Result<List<Account>> ParseUris(IEnumerable<string> lines) =>
        Read(lines, line => FromValue(line, null, null, false, requireUri: true));

    public static Result<List<Account>> ParseBitwardenCsv(IReadOnlyList<List<string>> rows)
    {
        var header = rows[0];
        if (header.Distinct(StringComparer.OrdinalIgnoreCase).Count() != header.Count)
            return Rejected(0, 1, 0, 0);
        var columns = header.Select((name, index) => (name, index)).ToDictionary(value => value.name,
            value => value.index, StringComparer.OrdinalIgnoreCase);
        if (!columns.ContainsKey("name") || !columns.ContainsKey("login_totp") || !columns.ContainsKey("type"))
            return Rejected(0, 1, 0, 0);
        return Read(rows.Skip(1), row =>
        {
            if (row.Count != header.Count) throw new FormatException("Invalid authenticator CSV row.");
            string Value(string key) => columns.TryGetValue(key, out var index) ? row[index] : string.Empty;
            if (Value("type") != "login" || string.IsNullOrWhiteSpace(Value("login_totp"))) return null;
            return FromValue(Value("login_totp"), Value("name"), Value("login_username"), Value("favorite") == "1");
        });
    }

    private static Result<List<Account>> Read<T>(IEnumerable<T> entries, Func<T, Account?> parse)
    {
        var accounts = new List<Account>();
        var invalid = 0;
        var unsupported = 0;
        var nonOtp = 0;
        foreach (var entry in entries)
        {
            try
            {
                var account = parse(entry);
                if (account is null) nonOtp++;
                else accounts.Add(account);
            }
            catch (UnsupportedOtpException) { unsupported++; }
            catch (Exception exception) when (exception is ArgumentException or FormatException or InvalidOperationException or KeyNotFoundException)
            {
                invalid++;
            }
        }
        return invalid + unsupported > 0
            ? Rejected(accounts.Count, invalid, unsupported, nonOtp)
            : Result.Ok(accounts).WithSuccess(new AccountImportReadSummary(accounts.Count, nonOtpCount: nonOtp));
    }

    private static Account FromValue(string value, string? issuer, string? accountName, bool favorite, bool requireUri = false)
    {
        value = value.Trim();
        if (value.StartsWith("otpauth://", StringComparison.OrdinalIgnoreCase))
        {
            var uri = new Uri(value);
            if (!uri.Host.Equals("totp", StringComparison.OrdinalIgnoreCase)) throw new UnsupportedOtpException();
            // Classify supported syntax with unsupported algorithm/digits/period separately from malformed entries.
            var query = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Select(part => part.Split('=', 2)).ToArray();
            string? Parameter(string name) => query.LastOrDefault(part => Uri.UnescapeDataString(part[0]).Equals(name, StringComparison.OrdinalIgnoreCase)) is { Length: 2 } pair
                ? Uri.UnescapeDataString(pair[1]) : null;
            var algorithm = Parameter("algorithm") ?? "SHA1";
            var digits = Parameter("digits");
            var period = Parameter("period");
            if (!TotpConfigurationPolicy.TryNormalizeAlgorithm(algorithm, out var normalizedAlgorithm)
                || digits is not null && int.TryParse(digits, out var digitCount) && !TotpConfigurationPolicy.IsSupported(normalizedAlgorithm, digitCount)
                || period is not null && int.TryParse(period, out var periodSeconds) && !TotpPeriodPolicy.IsSupported(periodSeconds))
                throw new UnsupportedOtpException();
            var parsed = OtpauthParser.Parse(value);
            if (!OtpAuthSupportPolicy.TryResolveSupportedIdentity(parsed, out var uriIssuer, out var uriAccount))
                throw new FormatException("Invalid TOTP identity.");
            if (string.IsNullOrWhiteSpace(uriAccount) && (accountName?.Length ?? 0) > 256)
                throw new FormatException("Invalid TOTP account name.");
            return new Account(Guid.NewGuid(), uriIssuer, parsed.SecretBase32,
                string.IsNullOrWhiteSpace(uriAccount) ? accountName : uriAccount,
                parsed.Period, isFavorite: favorite, algorithm: parsed.Algorithm, digits: parsed.Digits);
        }
        if (requireUri) throw new FormatException("Expected a TOTP URI.");
        var secret = SecretValidation.NormalizeBase32Secret(value);
        if (!SecretValidation.IsValidBase32Secret(secret) || string.IsNullOrWhiteSpace(issuer) || issuer.Trim().Length > 256
            || (accountName?.Length ?? 0) > 256)
            throw new FormatException("Invalid TOTP identity or secret.");
        return new Account(Guid.NewGuid(), issuer.Trim(), secret, accountName, isFavorite: favorite);
    }

    private static string RequiredString(JsonElement element, string name) =>
        OptionalString(element, name) is { Length: > 0 } value ? value : throw new FormatException("Required authenticator field missing.");

    private static string? OptionalString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null) return null;
        return value.ValueKind == JsonValueKind.String ? value.GetString() : throw new FormatException("Invalid authenticator field.");
    }

    private static bool HasUniqueProperties(JsonElement element) => element.ValueKind == JsonValueKind.Object
        && element.EnumerateObject().Select(property => property.Name).Distinct(StringComparer.Ordinal).Count()
            == element.EnumerateObject().Count();

    private static void RequireUniqueProperties(JsonElement element)
    {
        if (!HasUniqueProperties(element)) throw new FormatException("Invalid or ambiguous authenticator object.");
    }

    private static Result<List<Account>> Rejected(int supported, int invalid, int unsupported, int nonOtp) =>
        Result.Fail<List<Account>>(new AccountImportReadError(new AccountImportReadSummary(supported, invalid, unsupported, nonOtp)))
            .WithError(new AppError(AppErrorCode.ImportInvalidPayload, "Authenticator import validation failed."));

    private sealed class UnsupportedOtpException : Exception { }
}
