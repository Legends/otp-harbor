using FluentResults;
using TOTP.Core.Backup;
using TOTP.Core.Models;
using TOTP.Core.Services.Interfaces;

namespace TOTP.Infrastructure.Services;

/// <summary>
/// Provides methods for managing otp items, including adding, updating, retrieving, and deleting otps.
/// </summary>
/// <param name="otpDal">The data access layer used to persist and retrieve otp information.</param>
public class AccountManager(
    IAccountDAL otpDal,
    IAutomaticBackupCoordinator? automaticBackup = null) : IAccountManager
{
    public async Task<Result> AddNewAsync(Account newItem)
    {
        return TrackMutation(await otpDal.AddNewAsync(newItem));
    }

    public async Task<Result> CommitImportAsync(IReadOnlyCollection<Account> accounts)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        return TrackMutation(await otpDal.CommitImportAsync(accounts));
    }

    public async Task<Result> UpdateAsync(Account previous, Account updated)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(updated);
        return TrackMutation(await otpDal.UpdateAsync(
            updated.WithGroups(updated.Groups.Count > 0 ? updated.Groups : previous.Groups)));
    }

    public async Task<Result> SaveFavoritesAsync(IReadOnlyCollection<Guid> accountIds)
    {
        ArgumentNullException.ThrowIfNull(accountIds);
        if (accountIds.Count == 0)
            return Result.Fail("At least one favorite account is required.");

        return TrackMutation(await otpDal.SaveFavoritesAsync(accountIds));
    }

    public async Task<Result> SaveGroupAsync(AccountGroup group, IReadOnlyCollection<Guid> accountIds)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(accountIds);
        if (!TOTP.Core.Validation.AccountGroupPolicy.TryNormalize(group, out var normalized)
            || normalized is null
            || accountIds.Count == 0)
        {
            return Result.Fail("Account group is invalid.");
        }

        return TrackMutation(await otpDal.SaveGroupAsync(normalized, accountIds));
    }

    public async Task<Result> DeleteGroupAsync(Guid groupId) =>
        TrackMutation(await otpDal.DeleteGroupAsync(groupId));

    public async Task<Result<IReadOnlyList<Account>>> GetAllOtpEntriesSortedAsync()
    {
        var result = await otpDal.GetAllAsync().ConfigureAwait(false);

        if (result.IsFailed)
            return result.ToResult();

        result.Value.Sort(new Comparison<Account>((a, b) => string.Compare(a.Issuer, b.Issuer, StringComparison.OrdinalIgnoreCase)));

        var allOtps = result.Value ?? [];
        return Result.Ok<IReadOnlyList<Account>>(allOtps);
    }

    public async Task<Result> DeleteAsync(Account item)
    {
        return TrackMutation(await otpDal.DeleteAsync(item));
    }

    public async Task<Result> BackupOtpEntriesStorageFileAsync()
    {
        return await otpDal.BackupOtpEntriesStorageFileAsync();
    }

    private Result TrackMutation(Result result)
    {
        if (result.IsSuccess) automaticBackup?.RequestBackup();
        return result;
    }

}
