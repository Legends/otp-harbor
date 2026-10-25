using FluentResults;
using TOTP.Core.Models;
using TOTP.Core.Services.Interfaces;

namespace TOTP.Infrastructure.Services;

public sealed class AccountListSnapshotService(
    IAccountDAL accountDal,
    ITotpGenerator totpGenerator) : IAccountListSnapshotService
{
    public async Task<Result<AccountListSnapshot>> LoadAsync()
    {
        var loaded = await accountDal.GetAllAsync().ConfigureAwait(false);
        if (loaded.IsFailed)
            return Result.Fail<AccountListSnapshot>(loaded.Errors);

        return await Task.Run(() => CreateSnapshot(loaded.Value)).ConfigureAwait(false);
    }

    private AccountListSnapshot CreateSnapshot(IReadOnlyCollection<Account> accounts)
    {
        var snapshots = new List<AccountListSnapshotItem>(accounts.Count);
        var failures = new HashSet<Guid>();
        foreach (var account in accounts.OrderBy(
                     value => value.Issuer,
                     StringComparer.OrdinalIgnoreCase))
        {
            TotpGenerationResult? totp = null;
            try
            {
                totp = totpGenerator.Generate(account.Secret, account.PeriodSeconds);
            }
            catch (Exception exception) when (
                exception is FormatException or ArgumentOutOfRangeException)
            {
                failures.Add(account.ID);
            }

            snapshots.Add(new AccountListSnapshotItem(
                account.ID,
                account.Issuer,
                account.AccountName ?? string.Empty,
                account.PeriodSeconds,
                account.IsFavorite,
                account.Groups.ToArray(),
                totp));
        }

        return new AccountListSnapshot(snapshots, failures);
    }
}
