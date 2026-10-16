using System.Security.Cryptography;
using OtpNet;
using TOTP.Core.Models;
using TOTP.Core.Services.Interfaces;
using TOTP.Core.Validation;

namespace TOTP.Infrastructure.Services;

public sealed class OtpNetTotpGenerator : ITotpGenerator
{
    public TotpGenerationResult Generate(
        string base32Secret,
        int periodSeconds = TotpPeriodPolicy.DefaultSeconds)
    {
        if (!SecretValidation.IsValidBase32Secret(base32Secret))
        {
            throw new FormatException("Secret is not valid Base32 data.");
        }
        if (!TotpPeriodPolicy.IsSupported(periodSeconds))
        {
            throw new ArgumentOutOfRangeException(nameof(periodSeconds));
        }

        var normalized = SecretValidation.NormalizeBase32Secret(base32Secret);
        var secretBytes = Base32Encoding.ToBytes(normalized);
        try
        {
            var totp = new Totp(secretBytes, step: periodSeconds);
            var now = DateTime.UtcNow;
            var remainingSeconds = totp.RemainingSeconds(now);
            return new TotpGenerationResult(
                totp.ComputeTotp(now),
                remainingSeconds,
                periodSeconds,
                totp.ComputeTotp(now.AddSeconds(remainingSeconds)));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secretBytes);
        }
    }
}
