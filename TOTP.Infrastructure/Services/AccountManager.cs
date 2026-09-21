using FluentResults;
using TOTP.Core.Models;
using TOTP.Core.Services.Interfaces;

namespace TOTP.Infrastructure.Services;

/// <summary>
/// Provides methods for managing otp items, including adding, updating, retrieving, and deleting otps.
/// </summary>
/// <param name="otpDal">The data access layer used to persist and retrieve otp information.</param>
public class AccountManager(
    IAccountDAL otpDal) : IAccountManager
{
    public async Task<Result> AddNewAsync(Account newItem)
    {
        return await otpDal.AddNewAsync(newItem);
    }

    public async Task<Result> UpdateAsync(Account previous, Account updated)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(updated);
        return await otpDal.UpdateAsync(updated.WithGroup(updated.Group ?? previous.Group));
    }

    public Task<Result> SaveGroupAsync(AccountGroup group, IReadOnlyCollection<Guid> accountIds)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(accountIds);
        if (!TOTP.Core.Validation.AccountGroupPolicy.TryNormalize(group, out var normalized)
            || normalized is null
            || accountIds.Count == 0)
        {
            return Task.FromResult(Result.Fail("Account group is invalid."));
        }

        return otpDal.SaveGroupAsync(normalized, accountIds);
    }

    public Task<Result> DeleteGroupAsync(Guid groupId) => otpDal.DeleteGroupAsync(groupId);

    public async Task<Result<IReadOnlyList<Account>>> GetAllOtpEntriesSortedAsync()
    {
        var result = await otpDal.GetAllAsync();

        if (result.IsFailed)
            return result.ToResult();

        result.Value.Sort(new Comparison<Account>((a, b) => string.Compare(a.Issuer, b.Issuer, StringComparison.OrdinalIgnoreCase)));

        var allOtps = result.Value ?? [];
        return Result.Ok<IReadOnlyList<Account>>(allOtps);
    }

    public async Task<Result> DeleteAsync(Account item)
    {
        return await otpDal.DeleteAsync(item);
    }

    public async Task<Result> BackupOtpEntriesStorageFileAsync()
    {
        return await otpDal.BackupOtpEntriesStorageFileAsync();
    }

}
