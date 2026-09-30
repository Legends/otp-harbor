using FluentResults;
using TOTP.Core.Models;
using TOTP.Core.Services.Interfaces;

namespace TOTP.Infrastructure.Services;

public sealed class AccountTotpService(
    IAccountManager accountManager,
    ITotpGenerator totpGenerator) : IAccountTotpService
{
    public async Task<Result<TotpGenerationResult>> GenerateAsync(Guid accountId)
    {
        if (accountId == Guid.Empty)
            return Result.Fail<TotpGenerationResult>("An account must be selected.");

        var accounts = await accountManager.GetAllOtpEntriesSortedAsync().ConfigureAwait(false);
        if (accounts.IsFailed)
            return Result.Fail<TotpGenerationResult>(accounts.Errors);

        var account = accounts.Value.FirstOrDefault(candidate => candidate.ID == accountId);
        if (account is null)
            return Result.Fail<TotpGenerationResult>("The selected account no longer exists.");

        try
        {
            return Result.Ok(totpGenerator.Generate(account.Secret, account.PeriodSeconds));
        }
        catch (Exception exception) when (
            exception is FormatException or ArgumentOutOfRangeException)
        {
            return Result.Fail<TotpGenerationResult>("The selected account seed is invalid.");
        }
    }

    public async Task<Result<AccountTotpGenerationBatch>> GenerateManyAsync(
        IReadOnlyCollection<Guid> accountIds)
    {
        ArgumentNullException.ThrowIfNull(accountIds);

        var requestedIds = accountIds
            .Where(accountId => accountId != Guid.Empty)
            .ToHashSet();
        if (requestedIds.Count == 0)
        {
            return Result.Ok(new AccountTotpGenerationBatch(
                new Dictionary<Guid, TotpGenerationResult>(),
                new HashSet<Guid>()));
        }

        var accounts = await accountManager.GetAllOtpEntriesSortedAsync().ConfigureAwait(false);
        if (accounts.IsFailed)
            return Result.Fail<AccountTotpGenerationBatch>(accounts.Errors);

        var codes = new Dictionary<Guid, TotpGenerationResult>(requestedIds.Count);
        var failedAccountIds = new HashSet<Guid>(requestedIds);
        foreach (var account in accounts.Value)
        {
            if (!requestedIds.Contains(account.ID)) continue;

            try
            {
                codes[account.ID] = totpGenerator.Generate(
                    account.Secret,
                    account.PeriodSeconds);
                failedAccountIds.Remove(account.ID);
            }
            catch (Exception exception) when (
                exception is FormatException or ArgumentOutOfRangeException)
            {
                // Invalid seed details must not cross the infrastructure boundary.
            }
        }

        return Result.Ok(new AccountTotpGenerationBatch(codes, failedAccountIds));
    }
}
