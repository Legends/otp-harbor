using System.Text.Json;
using TOTP.Core.Models;
using TOTP.Core.Validation;

namespace TOTP.Infrastructure.Parser;

internal static class AegisVaultParser
{
    private const int OuterFormatVersion = 1;
    private const int MaximumContentVersion = 3;

    public static List<Account> Parse(JsonElement root)
    {
        RequireVersion(root, "version", OuterFormatVersion, OuterFormatVersion);

        if (!root.TryGetProperty("db", out var database)
            || database.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException("Encrypted or malformed Aegis vaults are not supported.");
        }

        RequireVersion(database, "version", 1, MaximumContentVersion);
        if (!database.TryGetProperty("entries", out var entries)
            || entries.ValueKind != JsonValueKind.Array)
        {
            throw new FormatException("The Aegis vault does not contain an entries array.");
        }

        var accounts = new List<Account>(entries.GetArrayLength());
        foreach (var entry in entries.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object
                || !GetRequiredString(entry, "type").Equals("totp", StringComparison.OrdinalIgnoreCase))
            {
                throw new FormatException("The Aegis vault contains an unsupported OTP type.");
            }

            if (!entry.TryGetProperty("info", out var info)
                || info.ValueKind != JsonValueKind.Object)
            {
                throw new FormatException("An Aegis entry is missing its TOTP information.");
            }

            var algorithm = GetRequiredString(info, "algo");
            var digits = GetRequiredInt32(info, "digits");
            var period = GetRequiredInt32(info, "period");
            if (!algorithm.Equals("SHA1", StringComparison.OrdinalIgnoreCase)
                || digits != 6
                || !TotpPeriodPolicy.IsSupported(period))
            {
                throw new FormatException("The Aegis vault contains unsupported TOTP parameters.");
            }

            var secret = SecretValidation.NormalizeBase32Secret(GetRequiredString(info, "secret"));
            if (!SecretValidation.IsValidBase32Secret(secret))
                throw new FormatException("An Aegis entry contains an invalid Base32 secret.");

            var issuer = GetRequiredString(entry, "issuer").Trim();
            var accountName = GetRequiredString(entry, "name").Trim();
            accounts.Add(new Account(
                Guid.NewGuid(),
                issuer,
                secret,
                accountName.Length == 0 ? null : accountName,
                period));
        }

        return accounts;
    }

    private static void RequireVersion(
        JsonElement element,
        string propertyName,
        int minimum,
        int maximum)
    {
        var version = GetRequiredInt32(element, propertyName);
        if (version < minimum || version > maximum)
            throw new FormatException("The Aegis vault version is not supported.");
    }

    private static string GetRequiredString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.String)
        {
            throw new FormatException($"The Aegis entry is missing '{propertyName}'.");
        }

        return property.GetString() ?? string.Empty;
    }

    private static int GetRequiredInt32(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.Number
            || !property.TryGetInt32(out var value))
        {
            throw new FormatException($"The Aegis vault has an invalid '{propertyName}' value.");
        }

        return value;
    }
}
