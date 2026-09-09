using FluentResults;
using TOTP.Core.Models;
using TOTP.Core.Services.Interfaces;
using TOTP.Core.Services.Models;
using TOTP.Core.Validation;

namespace TOTP.Infrastructure.Services;

public sealed class AccountImportService(IAccountManager accountManager) : IAccountImportService
{
    private const int MaximumImportAccounts = 10_000;

    public async Task<Result<AccountImportOutcome>> ImportAsync(
        IReadOnlyList<Account> importedAccounts,
        ImportConflictStrategy conflictStrategy,
        Func<AccountImportPreview, CancellationToken, Task<bool>> confirmAsync,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(importedAccounts);
        ArgumentNullException.ThrowIfNull(confirmAsync);

        try
        {
            if (!TryValidate(importedAccounts, out var validated))
                return Result.Ok(new AccountImportOutcome(AccountImportStatus.InvalidTargets));

            var currentResult = await accountManager.GetAllOtpEntriesSortedAsync();
            if (currentResult.IsFailed)
                return Result.Ok(new AccountImportOutcome(AccountImportStatus.ExistingAccountsUnavailable));

            var conflicts = validated.Count(account => FindMatch(account, currentResult.Value) is not null);
            if (conflictStrategy == ImportConflictStrategy.SkipExisting
                && conflicts == validated.Count)
            {
                return Result.Ok(new AccountImportOutcome(
                    AccountImportStatus.Completed,
                    Skipped: validated.Count));
            }

            var confirmed = await confirmAsync(
                new AccountImportPreview(validated.Count, conflicts, conflictStrategy),
                cancellationToken);
            if (!confirmed)
                return Result.Ok(new AccountImportOutcome(AccountImportStatus.Cancelled));

            var backup = await accountManager.BackupOtpEntriesStorageFileAsync();
            if (backup.IsFailed)
                return Result.Ok(new AccountImportOutcome(AccountImportStatus.RecoveryBackupFailed));

            return Result.Ok(await ApplyAsync(
                validated,
                currentResult.Value,
                conflictStrategy,
                cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return Result.Fail("The account import workflow failed safely.");
        }
    }

    private async Task<AccountImportOutcome> ApplyAsync(
        IReadOnlyList<Account> imported,
        IReadOnlyList<Account> existing,
        ImportConflictStrategy strategy,
        CancellationToken cancellationToken)
    {
        var working = existing.ToList();
        var addedCount = 0;
        var replacedCount = 0;
        var skippedCount = 0;
        var failedCount = 0;
        foreach (var incoming in imported)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var match = FindMatch(incoming, working);
            if (match is not null && (SamePayload(match, incoming)
                                      || strategy == ImportConflictStrategy.SkipExisting))
            {
                skippedCount++;
                continue;
            }

            Result write;
            if (match is not null && strategy == ImportConflictStrategy.ReplaceExisting)
            {
                var replacement = new Account(
                    match.ID,
                    incoming.Issuer,
                    incoming.Secret,
                    incoming.AccountName,
                    incoming.PeriodSeconds);
                write = await accountManager.UpdateAsync(match, replacement);
                if (write.IsSuccess)
                {
                    working.Remove(match);
                    working.Add(replacement);
                    replacedCount++;
                }
            }
            else
            {
                var added = match is null
                    ? new Account(
                        working.Any(value => value.ID == incoming.ID) ? Guid.NewGuid() : incoming.ID,
                        incoming.Issuer,
                        incoming.Secret,
                        incoming.AccountName,
                        incoming.PeriodSeconds)
                    : CreateKeepBoth(incoming, working);
                write = await accountManager.AddNewAsync(added);
                if (write.IsSuccess)
                {
                    working.Add(added);
                    addedCount++;
                }
            }

            if (write.IsFailed) failedCount++;
        }

        return new AccountImportOutcome(
            AccountImportStatus.Completed,
            addedCount,
            replacedCount,
            skippedCount,
            failedCount);
    }

    private static bool TryValidate(IReadOnlyCollection<Account> imported, out List<Account> validated)
    {
        validated = [];
        if (imported.Count is 0 or > MaximumImportAccounts) return false;

        foreach (var account in imported)
        {
            var issuer = account.Issuer?.Trim();
            var accountName = account.AccountName?.Trim();
            if (string.IsNullOrWhiteSpace(issuer)
                || issuer.Length > 256
                || (accountName?.Length ?? 0) > 256
                || !SecretValidation.IsValidBase32Secret(account.Secret)
                || !TotpPeriodPolicy.IsSupported(account.PeriodSeconds))
            {
                validated.Clear();
                return false;
            }

            validated.Add(new Account(
                account.ID == Guid.Empty ? Guid.NewGuid() : account.ID,
                issuer,
                SecretValidation.NormalizeBase32Secret(account.Secret),
                string.IsNullOrWhiteSpace(accountName) ? null : accountName,
                account.PeriodSeconds));
        }

        return true;
    }

    private static Account? FindMatch(Account incoming, IEnumerable<Account> accounts) =>
        accounts.FirstOrDefault(account => account.ID == incoming.ID)
        ?? accounts.FirstOrDefault(account =>
            string.Equals(account.Issuer.Trim(), incoming.Issuer.Trim(), StringComparison.OrdinalIgnoreCase)
            && string.Equals(
                (account.AccountName ?? string.Empty).Trim(),
                (incoming.AccountName ?? string.Empty).Trim(),
                StringComparison.OrdinalIgnoreCase));

    private static bool SamePayload(Account left, Account right) =>
        string.Equals(left.Issuer.Trim(), right.Issuer.Trim(), StringComparison.OrdinalIgnoreCase)
        && string.Equals(
            (left.AccountName ?? string.Empty).Trim(),
            (right.AccountName ?? string.Empty).Trim(),
            StringComparison.OrdinalIgnoreCase)
        && string.Equals(
            SecretValidation.NormalizeBase32Secret(left.Secret),
            SecretValidation.NormalizeBase32Secret(right.Secret),
            StringComparison.Ordinal)
        && left.PeriodSeconds == right.PeriodSeconds;

    private static Account CreateKeepBoth(Account incoming, IReadOnlyCollection<Account> accounts)
    {
        var suffix = 1;
        string issuer;
        do
        {
            issuer = suffix == 1
                ? $"{incoming.Issuer} (imported)"
                : $"{incoming.Issuer} (imported {suffix})";
            suffix++;
        } while (accounts.Any(account =>
            string.Equals(account.Issuer, issuer, StringComparison.OrdinalIgnoreCase)
            && string.Equals(account.AccountName, incoming.AccountName, StringComparison.OrdinalIgnoreCase)));

        return new Account(
            Guid.NewGuid(),
            issuer,
            incoming.Secret,
            incoming.AccountName,
            incoming.PeriodSeconds);
    }
}
