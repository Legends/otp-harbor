using System.Text.Json;
using TOTP.Core.Models;
using TOTP.Core.Validation;

namespace TOTP.Infrastructure.Parser;

internal static class TwoFasBackupParser
{
    private const int MinimumSchemaVersion = 2;
    private const int MaximumSchemaVersion = 4;

    public static List<Account> Parse(JsonElement root)
    {
        var schemaVersion = GetRequiredInt32(root, "schemaVersion");
        if (schemaVersion is < MinimumSchemaVersion or > MaximumSchemaVersion)
            throw new FormatException("The 2FAS backup schema version is not supported.");

        if (HasNonEmptyString(root, "reference") || HasNonEmptyString(root, "servicesEncrypted"))
            throw new FormatException("Encrypted 2FAS backups are not supported.");

        if (!root.TryGetProperty("services", out var services)
            || services.ValueKind != JsonValueKind.Array)
        {
            throw new FormatException("The 2FAS backup does not contain a services array.");
        }

        var accounts = new List<Account>(services.GetArrayLength());
        foreach (var service in services.EnumerateArray())
        {
            if (service.ValueKind != JsonValueKind.Object
                || !service.TryGetProperty("otp", out var otp)
                || otp.ValueKind != JsonValueKind.Object)
            {
                throw new FormatException("A 2FAS service is missing its OTP information.");
            }

            var tokenType = GetOptionalString(otp, "tokenType") ?? "TOTP";
            var algorithm = GetOptionalString(otp, "algorithm") ?? "SHA1";
            var digits = GetOptionalInt32(otp, "digits") ?? 6;
            var period = GetOptionalInt32(otp, "period") ?? TotpPeriodPolicy.DefaultSeconds;
            if (!tokenType.Equals("TOTP", StringComparison.OrdinalIgnoreCase)
                || !algorithm.Equals("SHA1", StringComparison.OrdinalIgnoreCase)
                || digits != 6
                || !TotpPeriodPolicy.IsSupported(period))
            {
                throw new FormatException("The 2FAS backup contains unsupported OTP parameters.");
            }

            var secret = SecretValidation.NormalizeBase32Secret(GetRequiredString(service, "secret"));
            if (!SecretValidation.IsValidBase32Secret(secret))
                throw new FormatException("A 2FAS service contains an invalid Base32 secret.");

            var serviceName = GetRequiredString(service, "name").Trim();
            var issuer = GetOptionalString(otp, "issuer")?.Trim();
            var accountName = GetOptionalString(otp, "account")?.Trim();
            if (string.IsNullOrEmpty(accountName))
                accountName = GetOptionalString(otp, "label")?.Trim();

            accounts.Add(new Account(
                Guid.NewGuid(),
                string.IsNullOrEmpty(issuer) ? serviceName : issuer,
                secret,
                string.IsNullOrEmpty(accountName) ? null : accountName,
                period));
        }

        return accounts;
    }

    private static bool HasNonEmptyString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property)
        && property.ValueKind == JsonValueKind.String
        && !string.IsNullOrWhiteSpace(property.GetString());

    private static string GetRequiredString(JsonElement element, string propertyName) =>
        GetOptionalString(element, propertyName)
        ?? throw new FormatException($"The 2FAS service is missing '{propertyName}'.");

    private static string? GetOptionalString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property)
            || property.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (property.ValueKind != JsonValueKind.String)
            throw new FormatException($"The 2FAS backup has an invalid '{propertyName}' value.");

        return property.GetString();
    }

    private static int GetRequiredInt32(JsonElement element, string propertyName) =>
        GetOptionalInt32(element, propertyName)
        ?? throw new FormatException($"The 2FAS backup is missing '{propertyName}'.");

    private static int? GetOptionalInt32(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property)
            || property.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (property.ValueKind != JsonValueKind.Number || !property.TryGetInt32(out var value))
            throw new FormatException($"The 2FAS backup has an invalid '{propertyName}' value.");

        return value;
    }
}
