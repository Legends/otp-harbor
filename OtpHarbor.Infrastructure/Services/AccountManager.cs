using FluentResults;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Runtime.CompilerServices;
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
    IAutomaticBackupCoordinator? automaticBackup = null,
    ILogger<AccountManager>? logger = null) : IAccountManager
{
    private readonly ILogger<AccountManager> _logger = logger ?? NullLogger<AccountManager>.Instance;
    public async Task<Result> AddNewAsync(Account newItem)
    {
        if (!TOTP.Core.Validation.SecretValidation.IsValidBase32Secret(newItem.Secret))
            return Result.Fail("The account secret is invalid.");
        if (!IsSupported(newItem)) return Result.Fail("The TOTP configuration is unsupported.");
        return TrackMutation(await otpDal.AddNewAsync(newItem));
    }

    public async Task<Result> CommitImportAsync(IReadOnlyCollection<Account> accounts)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        if (accounts.Any(account => !IsSupported(account)))
            return Result.Fail("The TOTP configuration is unsupported.");
        return TrackMutation(await otpDal.CommitImportAsync(accounts));
    }

    public async Task<Result> UpdateAsync(Account previous, Account updated)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(updated);
        var isUnchangedSecret = string.Equals(previous.Secret, updated.Secret, StringComparison.Ordinal);
        var isValidSecret = isUnchangedSecret
            ? TOTP.Core.Validation.SecretValidation.IsValidStoredBase32Secret(updated.Secret)
            : TOTP.Core.Validation.SecretValidation.IsValidBase32Secret(updated.Secret);
        if (!isValidSecret) return Result.Fail("The account secret is invalid.");
        if (!IsSupported(updated)) return Result.Fail("The TOTP configuration is unsupported.");
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
        {
            _logger.LogError("Account list could not be loaded.");
            return result.ToResult();
        }

        result.Value.Sort(new Comparison<Account>((a, b) => string.Compare(a.Issuer, b.Issuer, StringComparison.OrdinalIgnoreCase)));

        var allOtps = result.Value ?? [];
        _logger.LogTrace("Account list loaded. Account count: {AccountCount}.", allOtps.Count);
        return Result.Ok<IReadOnlyList<Account>>(allOtps);
    }

    public async Task<Result> DeleteAsync(Account item)
    {
        return TrackMutation(await otpDal.DeleteAsync(item));
    }

    public async Task<Result> BackupOtpEntriesStorageFileAsync()
    {
        var result = await otpDal.BackupOtpEntriesStorageFileAsync();
        _logger.Log(result.IsSuccess ? LogLevel.Information : LogLevel.Error,
            "Recovery checkpoint completed. Success: {Success}.", result.IsSuccess);
        return result;
    }

    private static bool IsSupported(Account account) =>
        TOTP.Core.Validation.TotpConfigurationPolicy.IsSupported(account.Algorithm, account.Digits)
        && TOTP.Core.Validation.TotpPeriodPolicy.IsSupported(account.PeriodSeconds);

    private Result TrackMutation(Result result, [CallerMemberName] string operation = "")
    {
        _logger.Log(result.IsSuccess ? LogLevel.Information : LogLevel.Error,
            "Account operation {Operation} completed. Success: {Success}.", operation, result.IsSuccess);
        if (result.IsSuccess) automaticBackup?.RequestBackup();
        return result;
    }

}
