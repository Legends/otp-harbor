using FluentResults;
using TOTP.Core.Models;

namespace TOTP.Core.Services.Interfaces;

public interface IAccountTotpService
{
    Task<Result<TotpGenerationResult>> GenerateAsync(Guid accountId);

    Task<Result<AccountTotpGenerationBatch>> GenerateManyAsync(
        IReadOnlyCollection<Guid> accountIds);
}
