using FluentResults;
using TOTP.Core.Models;
using TOTP.Core.Services.Interfaces;
using TOTP.Core.Services.Models;
using TOTP.Core.Validation;
using TOTP.Infrastructure.Parser;

namespace TOTP.Infrastructure.Services;

public sealed class QrAccountImportService(IAccountManager accounts) : IQrAccountImportService
{
    private const int MaximumPayloadLength = 4096;

    private enum PlannedWriteKind
    {
        Add,
        Update,
        KeepBoth
    }

    private sealed record PlannedWrite(
        PlannedWriteKind Kind,
        Account Incoming,
        Account? Existing = null);

    public async Task<Result<QrAccountImportOutcome>> ImportAsync(
        string decodedPayload,
        Func<QrAccountConflict, CancellationToken, Task<QrAccountConflictDecision>> resolveConflict,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resolveConflict);
        if (string.IsNullOrWhiteSpace(decodedPayload) || decodedPayload.Length > MaximumPayloadLength)
            return Result.Fail<QrAccountImportOutcome>("The QR payload is invalid.");

        if (GoogleAuthenticatorMigrationParser.IsMigrationPayload(decodedPayload))
            return await ImportMigrationAsync(decodedPayload, resolveConflict, cancellationToken);

        OtpauthParser.TOTPData parsed;
        try
        {
            parsed = OtpauthParser.Parse(decodedPayload);
        }
        catch (Exception)
        {
            return Result.Fail<QrAccountImportOutcome>("The QR payload is invalid.");
        }

        if (!OtpAuthSupportPolicy.IsSupported(parsed))
            return Result.Fail<QrAccountImportOutcome>("The TOTP parameters are unsupported.");

        cancellationToken.ThrowIfCancellationRequested();
        var issuer = parsed.Issuer?.Trim() ?? string.Empty;
        var accountName = parsed.Label.Trim();
        var loaded = await accounts.GetAllOtpEntriesSortedAsync();
        if (loaded.IsFailed)
            return Result.Fail<QrAccountImportOutcome>(loaded.Errors);

        var existing = loaded.Value.FirstOrDefault(account =>
            string.Equals(account.Issuer.Trim(), issuer, StringComparison.OrdinalIgnoreCase)
            && string.Equals(
                (account.AccountName ?? string.Empty).Trim(),
                accountName,
                StringComparison.OrdinalIgnoreCase));
        var incoming = new Account(
            Guid.NewGuid(),
            issuer,
            parsed.SecretBase32,
            EmptyToNull(accountName),
            parsed.Period);
        if (existing is null)
        {
            var added = await accounts.AddNewAsync(incoming);
            return added.IsSuccess
                ? Result.Ok(new QrAccountImportOutcome(
                    QrAccountImportStatus.Added,
                    incoming.ID,
                    issuer,
                    accountName))
                : Result.Fail<QrAccountImportOutcome>(added.Errors);
        }

        if (string.Equals(
            NormalizeSecret(existing.Secret),
            NormalizeSecret(incoming.Secret),
            StringComparison.Ordinal)
            && existing.PeriodSeconds == incoming.PeriodSeconds)
        {
            return Result.Ok(new QrAccountImportOutcome(
                QrAccountImportStatus.DuplicateUnchanged,
                existing.ID,
                issuer,
                accountName));
        }

        var decision = await resolveConflict(
            new QrAccountConflict(issuer, accountName),
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (decision == QrAccountConflictDecision.Cancel)
        {
            return Result.Ok(new QrAccountImportOutcome(
                QrAccountImportStatus.Cancelled,
                existing.ID,
                issuer,
                accountName));
        }

        FluentResults.Result saved;
        QrAccountImportStatus status;
        Guid affectedAccountId;
        if (decision == QrAccountConflictDecision.UpdateExisting)
        {
            var updated = new Account(
                existing.ID,
                issuer,
                incoming.Secret,
                EmptyToNull(accountName),
                incoming.PeriodSeconds,
                isFavorite: existing.IsFavorite);
            saved = await accounts.UpdateAsync(existing, updated);
            status = QrAccountImportStatus.Updated;
            affectedAccountId = existing.ID;
        }
        else
        {
            saved = await accounts.AddNewAsync(incoming);
            status = QrAccountImportStatus.KeptBoth;
            affectedAccountId = incoming.ID;
        }

        return saved.IsSuccess
            ? Result.Ok(new QrAccountImportOutcome(status, affectedAccountId, issuer, accountName))
            : Result.Fail<QrAccountImportOutcome>(saved.Errors);
    }

    private static string? EmptyToNull(string value) => value.Length == 0 ? null : value;

    private static string NormalizeSecret(string value) => SecretValidation.NormalizeBase32Secret(value);

    private async Task<Result<QrAccountImportOutcome>> ImportMigrationAsync(
        string decodedPayload,
        Func<QrAccountConflict, CancellationToken, Task<QrAccountConflictDecision>> resolveConflict,
        CancellationToken cancellationToken)
    {
        GoogleAuthenticatorMigrationParser.MigrationBatch migration;
        try
        {
            migration = GoogleAuthenticatorMigrationParser.Parse(decodedPayload);
        }
        catch (Exception)
        {
            return Result.Fail<QrAccountImportOutcome>("The Google Authenticator migration payload is invalid.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        var loaded = await accounts.GetAllOtpEntriesSortedAsync();
        if (loaded.IsFailed)
            return Result.Fail<QrAccountImportOutcome>(loaded.Errors);

        var working = loaded.Value.ToList();
        var writes = new List<PlannedWrite>(migration.Accounts.Count);
        var duplicateCount = 0;
        foreach (var parsed in migration.Accounts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var issuer = parsed.Issuer?.Trim() ?? string.Empty;
            var accountName = parsed.Label.Trim();
            var incoming = new Account(
                Guid.NewGuid(),
                issuer,
                parsed.SecretBase32,
                EmptyToNull(accountName),
                TotpPeriodPolicy.DefaultSeconds);
            var identityMatches = working.Where(account => IdentityMatches(incoming, account)).ToArray();
            if (identityMatches.Length == 0)
            {
                writes.Add(new PlannedWrite(PlannedWriteKind.Add, incoming));
                working.Add(incoming);
                continue;
            }

            if (identityMatches.Any(existing => string.Equals(
                NormalizeSecret(existing.Secret),
                NormalizeSecret(incoming.Secret),
                StringComparison.Ordinal)
                && existing.PeriodSeconds == incoming.PeriodSeconds))
            {
                duplicateCount++;
                continue;
            }

            var existing = identityMatches[0];
            var decision = await resolveConflict(
                new QrAccountConflict(issuer, accountName),
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (decision == QrAccountConflictDecision.Cancel)
            {
                return Result.Ok(new QrAccountImportOutcome(
                    QrAccountImportStatus.Cancelled,
                    Guid.Empty,
                    string.Empty,
                    string.Empty,
                    TotalCount: migration.Accounts.Count,
                    DuplicateCount: duplicateCount,
                    BatchIndex: migration.BatchIndex,
                    BatchSize: migration.BatchSize));
            }

            if (decision == QrAccountConflictDecision.UpdateExisting)
            {
                var updated = new Account(
                    existing.ID,
                    incoming.Issuer,
                    incoming.Secret,
                    incoming.AccountName,
                    incoming.PeriodSeconds,
                    isFavorite: existing.IsFavorite);
                writes.Add(new PlannedWrite(PlannedWriteKind.Update, updated, existing));
                working.Remove(existing);
                working.Add(updated);
            }
            else
            {
                writes.Add(new PlannedWrite(PlannedWriteKind.KeepBoth, incoming));
                working.Add(incoming);
            }
        }

        if (writes.Count > 0)
        {
            var backup = await accounts.BackupOtpEntriesStorageFileAsync();
            if (backup.IsFailed)
                return Result.Fail<QrAccountImportOutcome>(backup.Errors);
        }

        var addedCount = 0;
        var updatedCount = 0;
        var keptBothCount = 0;
        var failedCount = 0;
        var lastAccountId = Guid.Empty;
        var lastIssuer = string.Empty;
        var lastAccountName = string.Empty;
        foreach (var write in writes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var saved = write.Kind == PlannedWriteKind.Update
                ? await accounts.UpdateAsync(write.Existing!, write.Incoming)
                : await accounts.AddNewAsync(write.Incoming);
            if (saved.IsFailed)
            {
                failedCount++;
                continue;
            }

            lastAccountId = write.Incoming.ID;
            lastIssuer = write.Incoming.Issuer;
            lastAccountName = write.Incoming.AccountName ?? string.Empty;
            switch (write.Kind)
            {
                case PlannedWriteKind.Add:
                    addedCount++;
                    break;
                case PlannedWriteKind.Update:
                    updatedCount++;
                    break;
                case PlannedWriteKind.KeepBoth:
                    keptBothCount++;
                    break;
            }
        }

        return Result.Ok(new QrAccountImportOutcome(
            QrAccountImportStatus.BulkImported,
            lastAccountId,
            lastIssuer,
            lastAccountName,
            migration.Accounts.Count,
            addedCount,
            updatedCount,
            keptBothCount,
            duplicateCount,
            failedCount,
            migration.BatchIndex,
            migration.BatchSize));
    }

    private static bool IdentityMatches(Account left, Account right) =>
        string.Equals(left.Issuer.Trim(), right.Issuer.Trim(), StringComparison.OrdinalIgnoreCase)
        && string.Equals(
            (left.AccountName ?? string.Empty).Trim(),
            (right.AccountName ?? string.Empty).Trim(),
            StringComparison.OrdinalIgnoreCase);

}
