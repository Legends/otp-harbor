using System.Security.Cryptography;
using System.Buffers.Binary;
using System.Globalization;
using OtpNet;
using TOTP.Core.Models;
using TOTP.Core.Services.Interfaces;
using TOTP.Core.Validation;

namespace TOTP.Infrastructure.Services;

public sealed class OtpNetTotpGenerator(TimeProvider? timeProvider = null) : ITotpGenerator
{
    public TotpGenerationResult Generate(
        string base32Secret,
        int periodSeconds = TotpPeriodPolicy.DefaultSeconds,
        string algorithm = TotpConfigurationPolicy.DefaultAlgorithm,
        int digits = TotpConfigurationPolicy.DefaultDigits)
    {
        if (!SecretValidation.IsValidStoredBase32Secret(base32Secret))
        {
            throw new FormatException("Secret is not valid Base32 data.");
        }
        if (!TotpPeriodPolicy.IsSupported(periodSeconds))
        {
            throw new ArgumentOutOfRangeException(nameof(periodSeconds));
        }
        if (!TotpConfigurationPolicy.TryNormalizeAlgorithm(algorithm, out var normalizedAlgorithm))
            throw new ArgumentOutOfRangeException(nameof(algorithm));
        if (digits is not (6 or 8))
            throw new ArgumentOutOfRangeException(nameof(digits));

        var normalized = SecretValidation.NormalizeBase32Secret(base32Secret);
        var secretBytes = Base32Encoding.ToBytes(normalized);
        try
        {
            var unixSeconds = (timeProvider ?? TimeProvider.System).GetUtcNow().ToUnixTimeSeconds();
            if (unixSeconds < 0) throw new ArgumentOutOfRangeException(nameof(timeProvider));
            var counter = unixSeconds / periodSeconds;
            var remainingSeconds = periodSeconds - (int)(unixSeconds % periodSeconds);
            return new TotpGenerationResult(
                ComputeCode(secretBytes, counter, normalizedAlgorithm, digits),
                remainingSeconds,
                periodSeconds,
                ComputeCode(secretBytes, counter + 1, normalizedAlgorithm, digits));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secretBytes);
        }
    }

    private static string ComputeCode(ReadOnlySpan<byte> secret, long counter, string algorithm, int digits)
    {
        Span<byte> movingFactor = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(movingFactor, counter);
        Span<byte> hash = stackalloc byte[64];
        try
        {
            var length = algorithm switch
            {
                "SHA1" => HMACSHA1.HashData(secret, movingFactor, hash),
                "SHA256" => HMACSHA256.HashData(secret, movingFactor, hash),
                "SHA512" => HMACSHA512.HashData(secret, movingFactor, hash),
                _ => throw new ArgumentOutOfRangeException(nameof(algorithm))
            };
            var offset = hash[length - 1] & 0x0f;
            var binary = BinaryPrimitives.ReadUInt32BigEndian(hash.Slice(offset, 4)) & 0x7fffffff;
            var modulus = digits == 6 ? 1_000_000u : 100_000_000u;
            return (binary % modulus).ToString(digits == 6 ? "D6" : "D8", CultureInfo.InvariantCulture);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(hash);
        }
    }
}
