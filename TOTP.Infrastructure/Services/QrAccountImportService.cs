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
        Conflict
    }

    private sealed record PlannedWrite(
        int ImportIndex,
        PlannedWriteKind Kind,
        Account Incoming,
        Account? Existing = null);

    public async Task<Result<QrAccountImportOutcome>> ImportAsync(
        string decodedPayload,
        Func<QrAccountConflict, CancellationToken, Task<QrAccountConflictDecision>> resolveConflict,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resolveConflict);
        return await ImportWithConflictResolutionAsync(
            decodedPayload,
            async (preview, token) =>
            {
                var resolutions = new List<QrAccountImportConflictResolution>(
                    preview.ChangedConflicts.Count);
                foreach (var conflict in preview.ChangedConflicts)
                {
                    var decision = await resolveConflict(conflict, token);
                    if (decision == QrAccountConflictDecision.Cancel) return null;
                    resolutions.Add(new QrAccountImportConflictResolution(
                        conflict.ImportIndex,
                        decision));
                }

                return new QrAccountImportResolution(resolutions);
            },
            cancellationToken);
    }

    public async Task<Result<QrAccountImportOutcome>> ImportWithConflictResolutionAsync(
        string decodedPayload,
        Func<QrAccountImportPreview, CancellationToken, Task<QrAccountImportResolution?>> resolveImport,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resolveImport);
        if (string.IsNullOrWhiteSpace(decodedPayload) || decodedPayload.Length > MaximumPayloadLength)
            return Result.Fail<QrAccountImportOutcome>("The QR payload is invalid.");

        if (GoogleAuthenticatorMigrationParser.IsMigrationPayload(decodedPayload))
            return await ImportMigrationAsync(decodedPayload, resolveImport, cancellationToken);

        OtpauthParser.TOTPData parsed;
        try
        {
            parsed = OtpauthParser.Parse(decodedPayload);
        }
        catch (Exception)
        {
            return Result.Fail<QrAccountImportOutcome>("The QR payload is invalid.");
        }

        if (!OtpAuthSupportPolicy.TryResolveSupportedIdentity(
                parsed,
                out var issuer,
                out var accountName))
        {
            return Result.Fail<QrAccountImportOutcome>("The TOTP parameters are unsupported.");
        }

        cancellationToken.ThrowIfCancellationRequested();
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

        var conflict = CreateConflict(0, incoming, existing);
        var resolution = await resolveImport(
            new QrAccountImportPreview(1, 0, 0, [conflict], 0, 1),
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (resolution is null)
        {
            return Result.Ok(new QrAccountImportOutcome(
                QrAccountImportStatus.Cancelled,
                existing.ID,
                issuer,
                accountName));
        }

        if (!TryResolveDecisions([conflict], resolution, out var decisions))
            return Result.Fail<QrAccountImportOutcome>("The QR import conflict resolution is invalid.");

        var decision = decisions[0];
        if (decision == QrAccountConflictDecision.Skip)
        {
            return Result.Ok(new QrAccountImportOutcome(
                QrAccountImportStatus.Skipped,
                existing.ID,
                issuer,
                accountName,
                SkippedCount: 1));
        }

        QrAccountImportStatus status;
        Guid affectedAccountId;
        var snapshot = loaded.Value.ToList();
        if (decision == QrAccountConflictDecision.UpdateExisting)
        {
            var updated = new Account(
                existing.ID,
                issuer,
                incoming.Secret,
                EmptyToNull(accountName),
                incoming.PeriodSeconds,
                isFavorite: existing.IsFavorite,
                groups: existing.Groups);
            var existingIndex = snapshot.FindIndex(account => account.ID == existing.ID);
            if (existingIndex < 0)
                return Result.Fail<QrAccountImportOutcome>("The QR import plan is inconsistent.");
            snapshot[existingIndex] = updated;
            status = QrAccountImportStatus.Updated;
            affectedAccountId = existing.ID;
        }
        else
        {
            snapshot.Add(incoming);
            status = QrAccountImportStatus.KeptBoth;
            affectedAccountId = incoming.ID;
        }

        var backup = await accounts.BackupOtpEntriesStorageFileAsync();
        if (backup.IsFailed) return Result.Fail<QrAccountImportOutcome>(backup.Errors);
        cancellationToken.ThrowIfCancellationRequested();
        var committed = await accounts.CommitImportAsync(snapshot);
        return committed.IsSuccess
            ? Result.Ok(new QrAccountImportOutcome(status, affectedAccountId, issuer, accountName))
            : Result.Fail<QrAccountImportOutcome>(committed.Errors);
    }

    private static string? EmptyToNull(string value) => value.Length == 0 ? null : value;

    private static string NormalizeSecret(string value) => SecretValidation.NormalizeBase32Secret(value);

    private async Task<Result<QrAccountImportOutcome>> ImportMigrationAsync(
        string decodedPayload,
        Func<QrAccountImportPreview, CancellationToken, Task<QrAccountImportResolution?>> resolveImport,
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
        var conflicts = new List<QrAccountConflict>();
        var duplicateCount = 0;
        for (var importIndex = 0; importIndex < migration.Accounts.Count; importIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var parsed = migration.Accounts[importIndex];
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
                writes.Add(new PlannedWrite(importIndex, PlannedWriteKind.Add, incoming));
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
            conflicts.Add(CreateConflict(importIndex, incoming, existing));
            writes.Add(new PlannedWrite(importIndex, PlannedWriteKind.Conflict, incoming, existing));
        }

        var resolution = await resolveImport(
            new QrAccountImportPreview(
                migration.Accounts.Count,
                writes.Count(write => write.Kind == PlannedWriteKind.Add),
                duplicateCount,
                conflicts,
                migration.BatchIndex,
                migration.BatchSize),
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (resolution is null)
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

        if (!TryResolveDecisions(conflicts, resolution, out var decisions))
            return Result.Fail<QrAccountImportOutcome>("The QR import conflict resolution is invalid.");

        var updatedCount = 0;
        var keptBothCount = 0;
        var skippedCount = 0;
        Account? lastAffected = writes.LastOrDefault(write => write.Kind == PlannedWriteKind.Add)?.Incoming;
        foreach (var write in writes.Where(write => write.Kind == PlannedWriteKind.Conflict))
        {
            var decision = decisions[write.ImportIndex];
            if (decision == QrAccountConflictDecision.Skip)
            {
                skippedCount++;
                continue;
            }

            if (decision == QrAccountConflictDecision.KeepBoth)
            {
                working.Add(write.Incoming);
                keptBothCount++;
                lastAffected = write.Incoming;
                continue;
            }

            var existing = write.Existing!;
            var updated = new Account(
                existing.ID,
                write.Incoming.Issuer,
                write.Incoming.Secret,
                write.Incoming.AccountName,
                write.Incoming.PeriodSeconds,
                isFavorite: existing.IsFavorite,
                groups: existing.Groups);
            var existingIndex = working.FindIndex(account => account.ID == existing.ID);
            if (existingIndex < 0)
                return Result.Fail<QrAccountImportOutcome>("The QR import plan is inconsistent.");
            working[existingIndex] = updated;
            updatedCount++;
            lastAffected = updated;
        }

        var addedCount = writes.Count(write => write.Kind == PlannedWriteKind.Add);
        if (addedCount + updatedCount + keptBothCount > 0)
        {
            var backup = await accounts.BackupOtpEntriesStorageFileAsync();
            if (backup.IsFailed)
                return Result.Fail<QrAccountImportOutcome>(backup.Errors);
            cancellationToken.ThrowIfCancellationRequested();
            var committed = await accounts.CommitImportAsync(working);
            if (committed.IsFailed)
                return Result.Fail<QrAccountImportOutcome>(committed.Errors);
        }

        return Result.Ok(new QrAccountImportOutcome(
            QrAccountImportStatus.BulkImported,
            lastAffected?.ID ?? Guid.Empty,
            lastAffected?.Issuer ?? string.Empty,
            lastAffected?.AccountName ?? string.Empty,
            migration.Accounts.Count,
            addedCount,
            updatedCount,
            keptBothCount,
            duplicateCount,
            skippedCount,
            FailedCount: 0,
            migration.BatchIndex,
            migration.BatchSize));
    }

    private static QrAccountConflict CreateConflict(
        int importIndex,
        Account incoming,
        Account existing) =>
        new(
            importIndex,
            incoming.Issuer,
            incoming.AccountName ?? string.Empty,
            !string.Equals(
                NormalizeSecret(existing.Secret),
                NormalizeSecret(incoming.Secret),
                StringComparison.Ordinal),
            existing.PeriodSeconds != incoming.PeriodSeconds);

    private static bool TryResolveDecisions(
        IReadOnlyCollection<QrAccountConflict> conflicts,
        QrAccountImportResolution resolution,
        out IReadOnlyDictionary<int, QrAccountConflictDecision> decisions)
    {
        var expectedIndexes = conflicts.Select(conflict => conflict.ImportIndex).ToHashSet();
        var resolved = new Dictionary<int, QrAccountConflictDecision>();
        foreach (var candidate in resolution.Conflicts)
        {
            if (!expectedIndexes.Contains(candidate.ImportIndex)
                || candidate.Decision is not (QrAccountConflictDecision.Skip
                    or QrAccountConflictDecision.UpdateExisting
                    or QrAccountConflictDecision.KeepBoth)
                || !resolved.TryAdd(candidate.ImportIndex, candidate.Decision))
            {
                decisions = new Dictionary<int, QrAccountConflictDecision>();
                return false;
            }
        }

        if (resolved.Count != expectedIndexes.Count)
        {
            decisions = new Dictionary<int, QrAccountConflictDecision>();
            return false;
        }

        decisions = resolved;
        return true;
    }

    private static bool IdentityMatches(Account left, Account right) =>
        string.Equals(left.Issuer.Trim(), right.Issuer.Trim(), StringComparison.OrdinalIgnoreCase)
        && string.Equals(
            (left.AccountName ?? string.Empty).Trim(),
            (right.AccountName ?? string.Empty).Trim(),
            StringComparison.OrdinalIgnoreCase);

}
