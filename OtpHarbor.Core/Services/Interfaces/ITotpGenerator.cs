using TOTP.Core.Models;
using TOTP.Core.Validation;

namespace TOTP.Core.Services.Interfaces;

public interface ITotpGenerator
{
    TotpGenerationResult Generate(
        string base32Secret,
        int periodSeconds = TotpPeriodPolicy.DefaultSeconds);
}
