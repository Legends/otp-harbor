using System;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using OtpNet;

namespace TOTP.Core.Validation;

public static class SecretValidation
{
    private const int MinimumSecretBytes = 1;

    public static string NormalizeBase32Secret(string secret)
    {
        if (secret is null)
            throw new ArgumentNullException(nameof(secret));

        return secret
            .Trim()
            .Replace(" ", "")
            .Replace("-", "")
            .ToUpperInvariant()
            .TrimEnd('=');
    }

    public static bool IsValidBase32Secret(string? secret)
        => TryGetDecodedLength(secret, out var byteLength)
           && byteLength >= MinimumSecretBytes;

    private static bool TryGetDecodedLength(string? secret, out int byteLength)
    {
        byteLength = 0;
        if (string.IsNullOrWhiteSpace(secret))
            return false;

        var normalized = NormalizeBase32Secret(secret);

        if (!Regex.IsMatch(normalized, "^[A-Z2-7]+$"))
            return false;

        byte[]? bytes = null;
        try
        {
            bytes = Base32Encoding.ToBytes(normalized);
            byteLength = bytes.Length;
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (bytes is not null) CryptographicOperations.ZeroMemory(bytes);
        }
    }
}

