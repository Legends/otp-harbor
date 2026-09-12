using System.Security.Cryptography;
using System.Text;

namespace TOTP.Infrastructure.Parser;

internal static class GoogleAuthenticatorMigrationParser
{
    private const int MaximumDecodedPayloadBytes = 4096;
    private const int MaximumAccounts = 100;
    private const int MaximumSecretBytes = 128;
    private const int MaximumTextBytes = 1024;
    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal sealed record MigrationBatch(
        IReadOnlyList<OtpauthParser.TOTPData> Accounts,
        int BatchSize,
        int BatchIndex,
        int BatchId);

    public static bool IsMigrationPayload(string payload) =>
        payload.StartsWith("otpauth-migration://", StringComparison.OrdinalIgnoreCase);

    public static MigrationBatch Parse(string payload)
    {
        if (!Uri.TryCreate(payload, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, "otpauth-migration", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(uri.Host, "offline", StringComparison.OrdinalIgnoreCase))
        {
            throw new FormatException("The Google Authenticator migration URI is invalid.");
        }

        var encodedData = ReadDataParameter(uri.Query);
        var protobuf = DecodeBase64(encodedData);
        try
        {
            if (protobuf.Length is 0 or > MaximumDecodedPayloadBytes)
                throw new FormatException("The Google Authenticator migration payload size is invalid.");

            return ParseProtobuf(protobuf);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(protobuf);
        }
    }

    private static MigrationBatch ParseProtobuf(ReadOnlySpan<byte> payload)
    {
        var accounts = new List<OtpauthParser.TOTPData>();
        var offset = 0;
        var version = -1;
        var batchSize = 1;
        var batchIndex = 0;
        var batchId = 0;

        while (offset < payload.Length)
        {
            var tag = ReadVarint(payload, ref offset);
            var field = checked((int)(tag >> 3));
            var wireType = checked((int)(tag & 7));
            if (field == 0) throw new FormatException("The protobuf field number is invalid.");

            switch (field)
            {
                case 1 when wireType == 2:
                    if (accounts.Count >= MaximumAccounts)
                        throw new FormatException("The migration payload contains too many accounts.");
                    accounts.Add(ParseAccount(ReadLengthDelimited(payload, ref offset)));
                    break;
                case 2 when wireType == 0:
                    version = ToInt32(ReadVarint(payload, ref offset));
                    break;
                case 3 when wireType == 0:
                    batchSize = ToInt32(ReadVarint(payload, ref offset));
                    break;
                case 4 when wireType == 0:
                    batchIndex = ToInt32(ReadVarint(payload, ref offset));
                    break;
                case 5 when wireType == 0:
                    batchId = unchecked((int)ReadVarint(payload, ref offset));
                    break;
                default:
                    SkipField(payload, ref offset, wireType);
                    break;
            }
        }

        if (version != 1
            || accounts.Count == 0
            || batchSize is < 1 or > MaximumAccounts
            || batchIndex < 0
            || batchIndex >= batchSize)
        {
            throw new FormatException("The Google Authenticator migration metadata is unsupported.");
        }

        return new MigrationBatch(accounts, batchSize, batchIndex, batchId);
    }

    private static OtpauthParser.TOTPData ParseAccount(ReadOnlySpan<byte> payload)
    {
        var offset = 0;
        string? secret = null;
        var name = string.Empty;
        var issuer = string.Empty;
        var algorithm = 0;
        var digits = 0;
        var type = 0;

        while (offset < payload.Length)
        {
            var tag = ReadVarint(payload, ref offset);
            var field = checked((int)(tag >> 3));
            var wireType = checked((int)(tag & 7));
            if (field == 0) throw new FormatException("The protobuf field number is invalid.");

            switch (field)
            {
                case 1 when wireType == 2:
                    var secretBytes = ReadLengthDelimited(payload, ref offset);
                    if (secretBytes.Length is < 1 or > MaximumSecretBytes)
                        throw new FormatException("The migration secret length is invalid.");
                    secret = EncodeBase32(secretBytes);
                    break;
                case 2 when wireType == 2:
                    name = ReadString(payload, ref offset);
                    break;
                case 3 when wireType == 2:
                    issuer = ReadString(payload, ref offset);
                    break;
                case 4 when wireType == 0:
                    algorithm = ToInt32(ReadVarint(payload, ref offset));
                    break;
                case 5 when wireType == 0:
                    digits = ToInt32(ReadVarint(payload, ref offset));
                    break;
                case 6 when wireType == 0:
                    type = ToInt32(ReadVarint(payload, ref offset));
                    break;
                default:
                    SkipField(payload, ref offset, wireType);
                    break;
            }
        }

        if (secret is null || algorithm != 1 || digits != 1 || type != 2)
            throw new FormatException("The migration account uses unsupported OTP parameters.");

        (issuer, name) = NormalizeIdentity(issuer, name);
        if (issuer.Length is 0 or > 256 || name.Length > 256)
            throw new FormatException("The migration account identity is invalid.");

        return new OtpauthParser.TOTPData
        {
            Issuer = issuer,
            Label = name,
            SecretBase32 = secret,
            Algorithm = "SHA1",
            Digits = 6,
            Period = 30
        };
    }

    private static (string Issuer, string AccountName) NormalizeIdentity(
        string issuer,
        string name)
    {
        issuer = issuer.Trim();
        name = name.Trim();
        var separator = name.IndexOf(':');
        if (separator > 0)
        {
            var nameIssuer = name[..separator].Trim();
            var accountName = name[(separator + 1)..].Trim();
            if (issuer.Length == 0) issuer = nameIssuer;
            if (string.Equals(issuer, nameIssuer, StringComparison.OrdinalIgnoreCase))
                name = accountName;
        }

        if (issuer.Length == 0 && name.Length > 0)
        {
            issuer = name;
            name = string.Empty;
        }

        return (issuer, name);
    }

    private static string ReadDataParameter(string query)
    {
        foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var keyValue = part.Split('=', 2);
            if (!string.Equals(
                    Uri.UnescapeDataString(keyValue[0]),
                    "data",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (keyValue.Length != 2 || keyValue[1].Length == 0)
                break;
            return Uri.UnescapeDataString(keyValue[1]);
        }

        throw new FormatException("The Google Authenticator migration data is missing.");
    }

    private static byte[] DecodeBase64(string value)
    {
        var normalized = value.Replace('-', '+').Replace('_', '/');
        var padding = normalized.Length % 4;
        if (padding != 0) normalized = normalized.PadRight(normalized.Length + 4 - padding, '=');

        try
        {
            return Convert.FromBase64String(normalized);
        }
        catch (FormatException exception)
        {
            throw new FormatException("The Google Authenticator migration data is invalid.", exception);
        }
    }

    private static string ReadString(ReadOnlySpan<byte> payload, ref int offset)
    {
        var value = ReadLengthDelimited(payload, ref offset);
        if (value.Length > MaximumTextBytes)
            throw new FormatException("The migration text field is too long.");
        return StrictUtf8.GetString(value);
    }

    private static ReadOnlySpan<byte> ReadLengthDelimited(ReadOnlySpan<byte> payload, ref int offset)
    {
        var lengthValue = ReadVarint(payload, ref offset);
        if (lengthValue > int.MaxValue)
            throw new FormatException("The protobuf field length is invalid.");
        var length = (int)lengthValue;
        if (length < 0 || offset > payload.Length - length)
            throw new FormatException("The protobuf field is truncated.");

        var value = payload.Slice(offset, length);
        offset += length;
        return value;
    }

    private static ulong ReadVarint(ReadOnlySpan<byte> payload, ref int offset)
    {
        ulong value = 0;
        for (var shift = 0; shift < 64; shift += 7)
        {
            if (offset >= payload.Length)
                throw new FormatException("The protobuf varint is truncated.");
            var current = payload[offset++];
            if (shift == 63 && current > 1)
                throw new FormatException("The protobuf varint is invalid.");
            value |= (ulong)(current & 0x7f) << shift;
            if ((current & 0x80) == 0) return value;
        }

        throw new FormatException("The protobuf varint is invalid.");
    }

    private static void SkipField(ReadOnlySpan<byte> payload, ref int offset, int wireType)
    {
        switch (wireType)
        {
            case 0:
                _ = ReadVarint(payload, ref offset);
                return;
            case 1:
                Advance(payload, ref offset, 8);
                return;
            case 2:
                _ = ReadLengthDelimited(payload, ref offset);
                return;
            case 5:
                Advance(payload, ref offset, 4);
                return;
            default:
                throw new FormatException("The protobuf wire type is unsupported.");
        }
    }

    private static void Advance(ReadOnlySpan<byte> payload, ref int offset, int count)
    {
        if (offset > payload.Length - count)
            throw new FormatException("The protobuf field is truncated.");
        offset += count;
    }

    private static int ToInt32(ulong value)
    {
        if (value > int.MaxValue) throw new FormatException("The protobuf value is out of range.");
        return (int)value;
    }

    private static string EncodeBase32(ReadOnlySpan<byte> bytes)
    {
        var result = new StringBuilder((bytes.Length * 8 + 4) / 5);
        var buffer = 0;
        var bitsInBuffer = 0;
        foreach (var value in bytes)
        {
            buffer = (buffer << 8) | value;
            bitsInBuffer += 8;
            while (bitsInBuffer >= 5)
            {
                bitsInBuffer -= 5;
                result.Append(Base32Alphabet[(buffer >> bitsInBuffer) & 31]);
            }
        }

        if (bitsInBuffer > 0)
            result.Append(Base32Alphabet[(buffer << (5 - bitsInBuffer)) & 31]);
        return result.ToString();
    }
}
