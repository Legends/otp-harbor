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

        return await ImportCoreAsync(
            importedAccounts,
            conflictStrategy,
            confirmAsync,
            null,
            cancellationToken);
    }

    public async Task<Result<AccountImportOutcome>> ImportWithConflictResolutionAsync(
        IReadOnlyList<Account> importedAccounts,
        Func<AccountImportPreview, CancellationToken, Task<AccountImportResolution?>> resolveAsync,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(importedAccounts);
        ArgumentNullException.ThrowIfNull(resolveAsync);

        return await ImportCoreAsync(
            importedAccounts,
            null,
            null,
            resolveAsync,
            cancellationToken);
    }

    private async Task<Result<AccountImportOutcome>> ImportCoreAsync(
        IReadOnlyList<Account> importedAccounts,
        ImportConflictStrategy? requestedStrategy,
        Func<AccountImportPreview, CancellationToken, Task<bool>>? confirmAsync,
        Func<AccountImportPreview, CancellationToken, Task<AccountImportResolution?>>? resolveAsync,
        CancellationToken cancellationToken)
    {

        try
        {
            if (!TryValidate(importedAccounts, out var validated))
                return Result.Ok(new AccountImportOutcome(AccountImportStatus.InvalidTargets));

            var currentResult = await accountManager.GetAllOtpEntriesSortedAsync();
            if (currentResult.IsFailed)
                return Result.Ok(new AccountImportOutcome(AccountImportStatus.ExistingAccountsUnavailable));

            var matches = validated
                .Select((account, index) => (
                    ImportIndex: index,
                    Incoming: account,
                    Existing: FindMatch(account, currentResult.Value)))
                .ToList();
            var unchanged = matches.Count(value =>
                value.Existing is not null && SamePayload(value.Existing, value.Incoming));
            var changedConflicts = matches
                .Where(value => value.Existing is not null
                    && !SamePayload(value.Existing, value.Incoming))
                .Select(value => new AccountImportConflict(
                    value.ImportIndex,
                    value.Existing!.Issuer,
                    value.Existing.AccountName ?? string.Empty,
                    value.Incoming.Issuer,
                    value.Incoming.AccountName ?? string.Empty,
                    !SameIssuer(value.Existing, value.Incoming),
                    !SameAccountName(value.Existing, value.Incoming),
                    !SameSecret(value.Existing, value.Incoming),
                    value.Existing.PeriodSeconds != value.Incoming.PeriodSeconds))
                .ToList();
            var newCount = validated.Count - unchanged - changedConflicts.Count;
            var conflicts = unchanged + changedConflicts.Count;
            if (requestedStrategy == ImportConflictStrategy.SkipExisting
                && conflicts == validated.Count)
            {
                return Result.Ok(new AccountImportOutcome(
                    AccountImportStatus.Completed,
                    Skipped: validated.Count));
            }

            var preview = new AccountImportPreview(
                validated.Count,
                conflicts,
                requestedStrategy ?? ImportConflictStrategy.SkipExisting)
            {
                NewCount = newCount,
                UnchangedCount = unchanged,
                ChangedConflicts = changedConflicts
            };
            ImportConflictStrategy conflictStrategy;
            IReadOnlyDictionary<int, AccountImportConflictAction>? perAccountResolution = null;
            if (requestedStrategy.HasValue)
            {
                if (!IsSupportedStrategy(requestedStrategy.Value))
                    return Result.Fail<AccountImportOutcome>("The import conflict strategy is invalid.");

                var confirmed = await confirmAsync!(preview, cancellationToken);
                if (!confirmed)
                    return Result.Ok(new AccountImportOutcome(AccountImportStatus.Cancelled));

                conflictStrategy = requestedStrategy.Value;
            }
            else
            {
                var resolution = await resolveAsync!(preview, cancellationToken);
                if (resolution is null)
                    return Result.Ok(new AccountImportOutcome(AccountImportStatus.Cancelled));
                if (!TryValidateResolution(changedConflicts, resolution, out perAccountResolution))
                    return Result.Fail<AccountImportOutcome>("The import conflict resolution is invalid.");

                conflictStrategy = ImportConflictStrategy.SkipExisting;
            }

            if (conflictStrategy == ImportConflictStrategy.SkipExisting
                && newCount == 0
                && (perAccountResolution is null
                    || perAccountResolution.Values.All(value =>
                        value == AccountImportConflictAction.Skip)))
            {
                return Result.Ok(new AccountImportOutcome(
                    AccountImportStatus.Completed,
                    Skipped: validated.Count));
            }

            var backup = await accountManager.BackupOtpEntriesStorageFileAsync();
            if (backup.IsFailed)
                return Result.Ok(new AccountImportOutcome(AccountImportStatus.RecoveryBackupFailed));

            return Result.Ok(await ApplyAsync(
                validated,
                currentResult.Value,
                conflictStrategy,
                perAccountResolution,
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
        IReadOnlyDictionary<int, AccountImportConflictAction>? perAccountResolution,
        CancellationToken cancellationToken)
    {
        var working = existing.ToList();
        var addedCount = 0;
        var replacedCount = 0;
        var skippedCount = 0;
        var failedCount = 0;
        for (var importIndex = 0; importIndex < imported.Count; importIndex++)
        {
            var incoming = imported[importIndex];
            cancellationToken.ThrowIfCancellationRequested();
            var match = FindMatch(incoming, working);
            var resolvedAction = perAccountResolution is not null
                && perAccountResolution.TryGetValue(importIndex, out var action)
                    ? action
                    : (AccountImportConflictAction?)null;
            if (match is not null && (SamePayload(match, incoming)
                                      || resolvedAction == AccountImportConflictAction.Skip
                                      || (resolvedAction is null
                                          && strategy == ImportConflictStrategy.SkipExisting)))
            {
                skippedCount++;
                continue;
            }

            Result write;
            if (match is not null
                && (resolvedAction == AccountImportConflictAction.Replace
                    || strategy == ImportConflictStrategy.ReplaceExisting))
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
        SameIssuer(left, right)
        && SameAccountName(left, right)
        && SameSecret(left, right)
        && left.PeriodSeconds == right.PeriodSeconds;

    private static bool SameIssuer(Account left, Account right) =>
        string.Equals(
            left.Issuer.Trim(),
            right.Issuer.Trim(),
            StringComparison.OrdinalIgnoreCase);

    private static bool SameAccountName(Account left, Account right) =>
        string.Equals(
            (left.AccountName ?? string.Empty).Trim(),
            (right.AccountName ?? string.Empty).Trim(),
            StringComparison.OrdinalIgnoreCase);

    private static bool SameSecret(Account left, Account right) =>
        string.Equals(
            SecretValidation.NormalizeBase32Secret(left.Secret),
            SecretValidation.NormalizeBase32Secret(right.Secret),
            StringComparison.Ordinal);

    private static bool IsSupportedStrategy(ImportConflictStrategy strategy) =>
        strategy is ImportConflictStrategy.SkipExisting
            or ImportConflictStrategy.ReplaceExisting
            or ImportConflictStrategy.KeepBoth;

    private static bool TryValidateResolution(
        IReadOnlyCollection<AccountImportConflict> conflicts,
        AccountImportResolution resolution,
        out IReadOnlyDictionary<int, AccountImportConflictAction> decisions)
    {
        var expectedIndexes = conflicts.Select(value => value.ImportIndex).ToHashSet();
        var resolved = new Dictionary<int, AccountImportConflictAction>();
        foreach (var decision in resolution.Conflicts)
        {
            if (!expectedIndexes.Contains(decision.ImportIndex)
                || !Enum.IsDefined(decision.Action)
                || !resolved.TryAdd(decision.ImportIndex, decision.Action))
            {
                decisions = new Dictionary<int, AccountImportConflictAction>();
                return false;
            }
        }

        if (resolved.Count != expectedIndexes.Count)
        {
            decisions = new Dictionary<int, AccountImportConflictAction>();
            return false;
        }

        decisions = resolved;
        return true;
    }

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
