using Microsoft.Extensions.Logging.Abstractions;
using System.Security.Cryptography;
using System.Text;
using TOTP.Core.Common;
using TOTP.Core.Models;
using TOTP.Core.Security.Interfaces;
using TOTP.DAL.Services;
using TOTP.Tests.Common;

namespace TOTP.Tests.Integration;

[Collection(NonParallelCollectionDefinition.NonParallel)]
public sealed class AccountDalIntegrationTests
{
    [Fact]
    public async Task GetAllAsync_WhenStorageMissing_ReturnsEmptyList()
    {
        using var temp = new TempDir();
        var storagePath = Path.Combine(temp.Path, "master.totp");
        var sut = CreateSut(storagePath, new EchoVaultService());

        var result = await sut.GetAllAsync();

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }

    [Fact]
    public async Task AddUpdateDelete_RoundTripFlow_PersistsExpectedEntries()
    {
        using var temp = new TempDir();
        var storagePath = Path.Combine(temp.Path, "master.totp");
        var sut = CreateSut(storagePath, new EchoVaultService());

        var id = Guid.NewGuid();
        var created = new Account(id, "GitHub", "AAAA", "john");

        Assert.True((await sut.AddNewAsync(created)).IsSuccess);

        var afterCreate = await sut.GetAllAsync();
        Assert.True(afterCreate.IsSuccess);
        var createdEntry = Assert.Single(afterCreate.Value);
        Assert.Equal("AAAA", createdEntry.Secret);

        var updated = new Account(id, "GitHub", "BBBB", "john.doe");
        Assert.True((await sut.UpdateAsync(updated)).IsSuccess);

        var afterUpdate = await sut.GetAllAsync();
        Assert.True(afterUpdate.IsSuccess);
        var updatedEntry = Assert.Single(afterUpdate.Value);
        Assert.Equal("BBBB", updatedEntry.Secret);
        Assert.Equal("john.doe", updatedEntry.AccountName);

        Assert.True((await sut.DeleteAsync(updated)).IsSuccess);

        var afterDelete = await sut.GetAllAsync();
        Assert.True(afterDelete.IsSuccess);
        Assert.Empty(afterDelete.Value);
    }

    [Fact]
    public async Task SaveAndDeleteGroup_UpdatesEveryAssignmentInSingleVaultWrite()
    {
        using var temp = new TempDir();
        var storagePath = Path.Combine(temp.Path, "master.totp");
        var vault = new EchoVaultService();
        var sut = CreateSut(storagePath, vault);
        var first = new Account(Guid.NewGuid(), "GitHub", "AAAA", "alice");
        var second = new Account(Guid.NewGuid(), "Microsoft", "BBBB", "bob");
        var third = new Account(Guid.NewGuid(), "Google", "CCCC", "carol");
        Assert.True((await sut.AddNewAsync(first)).IsSuccess);
        Assert.True((await sut.AddNewAsync(second)).IsSuccess);
        Assert.True((await sut.AddNewAsync(third)).IsSuccess);
        var group = new AccountGroup(Guid.NewGuid(), "Work", "#4F6BED");

        Assert.True((await sut.SaveGroupAsync(group, [first.ID, second.ID])).IsSuccess);
        var grouped = (await sut.GetAllAsync()).Value;
        Assert.Equal(group, grouped.Single(account => account.ID == first.ID).Group);
        Assert.Equal(group, grouped.Single(account => account.ID == second.ID).Group);
        Assert.Null(grouped.Single(account => account.ID == third.ID).Group);

        var renamed = new AccountGroup(group.Id, "Office", "#E45757");
        Assert.True((await sut.SaveGroupAsync(renamed, [second.ID, third.ID])).IsSuccess);
        var reassigned = (await sut.GetAllAsync()).Value;
        Assert.Null(reassigned.Single(account => account.ID == first.ID).Group);
        Assert.Equal(renamed, reassigned.Single(account => account.ID == second.ID).Group);
        Assert.Equal(renamed, reassigned.Single(account => account.ID == third.ID).Group);

        Assert.True((await sut.DeleteGroupAsync(group.Id)).IsSuccess);
        Assert.All((await sut.GetAllAsync()).Value, account => Assert.Null(account.Group));
    }

    [Fact]
    public async Task SaveGroup_WhenNoSelectedAccountExists_LeavesAssignmentsUnchanged()
    {
        using var temp = new TempDir();
        var storagePath = Path.Combine(temp.Path, "master.totp");
        var sut = CreateSut(storagePath, new EchoVaultService());
        var existingGroup = new AccountGroup(Guid.NewGuid(), "Work", "#4F6BED");
        var account = new Account(
            Guid.NewGuid(),
            "GitHub",
            "AAAA",
            "alice",
            group: existingGroup);
        Assert.True((await sut.AddNewAsync(account)).IsSuccess);

        var result = await sut.SaveGroupAsync(
            new AccountGroup(existingGroup.Id, "Renamed", "#E45757"),
            [Guid.NewGuid()]);

        Assert.True(result.IsFailed);
        Assert.Equal(existingGroup, Assert.Single((await sut.GetAllAsync()).Value).Group);
    }

    [Fact]
    public async Task ExportEncryptedAsync_WritesDecryptableBlob()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var temp = new TempDir();
        var storagePath = Path.Combine(temp.Path, "master.totp");
        var exportPath = Path.Combine(temp.Path, "export.totp");
        var vault = new EchoVaultService();
        var sut = CreateSut(storagePath, vault);

        var entry = new Account(Guid.NewGuid(), "Google", "CCCC", "a@b.com");
        Assert.True((await sut.AddNewAsync(entry)).IsSuccess);

        var exportResult = await sut.ExportEncryptedAsync(exportPath);

        Assert.True(exportResult.IsSuccess);
        Assert.True(File.Exists(exportPath));
        var exportedBlob = await File.ReadAllBytesAsync(exportPath, cancellationToken);
        var exportedItems = vault.DecryptVault(exportedBlob);
        var exported = Assert.Single(exportedItems);
        Assert.Equal(entry.ID, exported.ID);
        Assert.Equal("CCCC", exported.Secret);
    }

    [Fact]
    public async Task ExportEncryptedAsync_WhenTargetDirectoryMissing_ReturnsMappedError()
    {
        using var temp = new TempDir();
        var storagePath = Path.Combine(temp.Path, "master.totp");
        var sut = CreateSut(storagePath, new EchoVaultService());
        Assert.True((await sut.AddNewAsync(new Account(Guid.NewGuid(), "GitLab", "DDDD"))).IsSuccess);

        var target = Path.Combine(temp.Path, "missing", "export.totp");

        var result = await sut.ExportEncryptedAsync(target);

        Assert.False(result.IsSuccess);
        Assert.Equal(AppErrorCode.ExportFileWriteFailed, result.GetErrorCode());
    }

    [Fact]
    public async Task ReEncryptStorageAsync_PreservesStoredEntries()
    {
        using var temp = new TempDir();
        var storagePath = Path.Combine(temp.Path, "master.totp");
        var sut = CreateSut(storagePath, new EchoVaultService());

        var entry = new Account(Guid.NewGuid(), "Azure", "EEEE");
        Assert.True((await sut.AddNewAsync(entry)).IsSuccess);

        var reEncryptResult = await sut.ReEncryptStorageAsync();
        var after = await sut.GetAllAsync();

        Assert.True(reEncryptResult.IsSuccess);
        Assert.True(after.IsSuccess);
        Assert.Single(after.Value);
        Assert.Equal(entry.ID, after.Value[0].ID);
    }

    [Fact]
    public async Task BackupOtpEntriesStorageFileAsync_WhenStorageMissing_ReturnsSuccess()
    {
        using var temp = new TempDir();
        var storagePath = Path.Combine(temp.Path, "master.totp");
        var sut = CreateSut(storagePath, new EchoVaultService());

        var result = await sut.BackupOtpEntriesStorageFileAsync();

        Assert.True(result.IsSuccess);
        Assert.Empty(Directory.GetFiles(temp.Path, "*.bak*"));
    }

    [Fact]
    public async Task BackupOtpEntriesStorageFileAsync_RotatesBackupsAndSkipsWhenUnchanged()
    {
        using var temp = new TempDir();
        var storagePath = Path.Combine(temp.Path, "master.totp");
        var sut = CreateSut(storagePath, new EchoVaultService());

        Assert.True((await sut.AddNewAsync(new Account(Guid.NewGuid(), "One", "1111"))).IsSuccess);
        Assert.True((await sut.BackupOtpEntriesStorageFileAsync()).IsSuccess);
        var bak1 = storagePath + ".bak1";
        Assert.True(File.Exists(bak1));
        var firstWrite = File.GetLastWriteTimeUtc(bak1);

        Assert.True((await sut.BackupOtpEntriesStorageFileAsync()).IsSuccess);
        var secondWrite = File.GetLastWriteTimeUtc(bak1);
        Assert.Equal(firstWrite, secondWrite);

        Assert.True((await sut.AddNewAsync(new Account(Guid.NewGuid(), "Two", "2222"))).IsSuccess);
        Assert.True((await sut.BackupOtpEntriesStorageFileAsync()).IsSuccess);

        Assert.True(File.Exists(storagePath + ".bak1"));
        Assert.True(File.Exists(storagePath + ".bak2"));
    }

    [Fact]
    public async Task BackupOtpEntriesStorageFileAsync_KeepsExactlyFiveGenerations()
    {
        using var temp = new TempDir();
        var storagePath = Path.Combine(temp.Path, "master.totp");
        var sut = CreateSut(storagePath, new EchoVaultService());

        for (var generation = 1; generation <= 7; generation++)
        {
            Assert.True((await sut.AddNewAsync(new Account(
                Guid.NewGuid(),
                $"Account {generation}",
                $"secret-{generation}"))).IsSuccess);
            Assert.True((await sut.BackupOtpEntriesStorageFileAsync()).IsSuccess);
        }

        Assert.Equal(5, Directory.GetFiles(temp.Path, "master.totp.bak*").Length);
        for (var generation = 1; generation <= 5; generation++)
            Assert.True(File.Exists($"{storagePath}.bak{generation}"));
        Assert.False(File.Exists($"{storagePath}.bak6"));
    }

    [Fact]
    public async Task BackupOtpEntriesStorageFileAsync_PublishedGenerationCanRestoreVault()
    {
        using var temp = new TempDir();
        var storagePath = Path.Combine(temp.Path, "master.totp");
        var vault = new EchoVaultService();
        var sut = CreateSut(storagePath, vault);
        var recoverable = new Account(Guid.NewGuid(), "Recoverable", "SYNTHETIC", "backup@example.test");
        Assert.True((await sut.AddNewAsync(recoverable)).IsSuccess);
        Assert.True((await sut.BackupOtpEntriesStorageFileAsync()).IsSuccess);
        Assert.True((await sut.AddNewAsync(new Account(Guid.NewGuid(), "Later", "SYNTHETIC-2"))).IsSuccess);

        File.Copy($"{storagePath}.bak1", storagePath, overwrite: true);
        var restored = await CreateSut(storagePath, vault).GetAllAsync();

        Assert.True(restored.IsSuccess);
        var account = Assert.Single(restored.Value);
        Assert.Equal(recoverable.ID, account.ID);
        Assert.Equal(recoverable.Secret, account.Secret);
    }

    [Fact]
    public async Task GetAllAsync_WhenVaultThrowsCryptographicException_ReturnsDecryptFailedError()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var temp = new TempDir();
        var storagePath = Path.Combine(temp.Path, "master.totp");
        await File.WriteAllBytesAsync(storagePath, Encoding.UTF8.GetBytes("blob"), cancellationToken);
        var sut = CreateSut(storagePath, new ThrowingVaultService(new CryptographicException("bad")));

        var result = await sut.GetAllAsync();

        Assert.False(result.IsSuccess);
        Assert.Equal(AppErrorCode.OtpStorageDecryptFailed, result.GetErrorCode());
    }

    [Fact]
    public async Task AddNewAsync_WhenDirectoryCannotBeHardened_ReturnsAccessDeniedWithoutCreatingVault()
    {
        using var temp = new TempDir();
        var storagePath = Path.Combine(temp.Path, "master.totp");
        var fileSecurity = new DelegatingPlatformFileSecurity
        {
            RestrictDirectory = _ => throw new UnauthorizedAccessException("denied")
        };
        var sut = new AccountDAL(
            NullLogger<AccountDAL>.Instance,
            new EchoVaultService(),
            storagePath,
            fileSecurity);

        var result = await sut.AddNewAsync(new Account(Guid.NewGuid(), "GitHub", "AAAA"));

        Assert.False(result.IsSuccess);
        Assert.Equal(AppErrorCode.OtpStorageAccessDenied, result.GetErrorCode());
        Assert.False(File.Exists(storagePath));
        Assert.Empty(Directory.GetFiles(temp.Path, "master.totp.*.tmp"));
    }

    [Fact]
    public async Task AddNewAsync_WhenStagedFileCannotBeHardened_PreservesExistingVault()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var temp = new TempDir();
        var storagePath = Path.Combine(temp.Path, "master.totp");
        var originalBytes = Encoding.UTF8.GetBytes("[]");
        await File.WriteAllBytesAsync(storagePath, originalBytes, cancellationToken);
        var fileSecurity = new DelegatingPlatformFileSecurity
        {
            RestrictFile = path =>
            {
                if (path.EndsWith(".tmp", StringComparison.Ordinal))
                {
                    throw new UnauthorizedAccessException("denied");
                }
            }
        };
        var sut = new AccountDAL(
            NullLogger<AccountDAL>.Instance,
            new EchoVaultService(),
            storagePath,
            fileSecurity);

        var result = await sut.AddNewAsync(new Account(Guid.NewGuid(), "GitHub", "AAAA"));

        Assert.False(result.IsSuccess);
        Assert.Equal(AppErrorCode.OtpStorageAccessDenied, result.GetErrorCode());
        Assert.Equal(originalBytes, await File.ReadAllBytesAsync(storagePath, cancellationToken));
        Assert.Empty(Directory.GetFiles(temp.Path, "master.totp.*.tmp"));
    }

    [Fact]
    public async Task AddNewAsync_WhenStagedVaultIsTruncated_PreservesExistingVault()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var temp = new TempDir();
        var storagePath = Path.Combine(temp.Path, "master.totp");
        var originalBytes = Encoding.UTF8.GetBytes("[]");
        await File.WriteAllBytesAsync(storagePath, originalBytes, cancellationToken);
        var sut = new AccountDAL(
            NullLogger<AccountDAL>.Instance,
            new EchoVaultService(),
            storagePath,
            new DelegatingPlatformFileSecurity
            {
                RestrictFile = path =>
                {
                    if (path.EndsWith(".tmp", StringComparison.Ordinal))
                        File.WriteAllText(path, "[");
                }
            });

        var result = await sut.AddNewAsync(new Account(Guid.NewGuid(), "GitHub", "AAAA"));

        Assert.False(result.IsSuccess);
        Assert.Equal(AppErrorCode.OtpCreateFailed, result.GetErrorCode());
        Assert.Equal(originalBytes, await File.ReadAllBytesAsync(storagePath, cancellationToken));
        Assert.Empty(Directory.GetFiles(temp.Path, "master.totp.*.tmp"));
    }

    [Fact]
    public async Task AddNewAsync_WhenPostCommitHardeningFails_RollsBackExistingVault()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var temp = new TempDir();
        var storagePath = Path.Combine(temp.Path, "master.totp");
        var originalBytes = Encoding.UTF8.GetBytes("[]");
        await File.WriteAllBytesAsync(storagePath, originalBytes, cancellationToken);
        var stagedFileWasHardened = false;
        var sut = new AccountDAL(
            NullLogger<AccountDAL>.Instance,
            new EchoVaultService(),
            storagePath,
            new DelegatingPlatformFileSecurity
            {
                RestrictFile = path =>
                {
                    if (path.EndsWith(".tmp", StringComparison.Ordinal))
                    {
                        stagedFileWasHardened = true;
                    }
                    else if (stagedFileWasHardened
                             && string.Equals(path, storagePath, StringComparison.Ordinal))
                    {
                        throw new UnauthorizedAccessException("denied after commit");
                    }
                }
            });

        var result = await sut.AddNewAsync(new Account(Guid.NewGuid(), "GitHub", "AAAA"));

        Assert.False(result.IsSuccess);
        Assert.Equal(AppErrorCode.OtpStorageAccessDenied, result.GetErrorCode());
        Assert.Equal(originalBytes, await File.ReadAllBytesAsync(storagePath, cancellationToken));
        Assert.Empty(Directory.GetFiles(temp.Path, "master.totp.*.tmp"));
        Assert.Empty(Directory.GetFiles(temp.Path, "master.totp.*.rollback"));
    }

    [Fact]
    public async Task AddNewAsync_WhenFirstCommitHardeningFails_RemovesFailedVault()
    {
        using var temp = new TempDir();
        var storagePath = Path.Combine(temp.Path, "master.totp");
        var sut = new AccountDAL(
            NullLogger<AccountDAL>.Instance,
            new EchoVaultService(),
            storagePath,
            new DelegatingPlatformFileSecurity
            {
                RestrictFile = path =>
                {
                    if (string.Equals(path, storagePath, StringComparison.Ordinal))
                        throw new UnauthorizedAccessException("denied after commit");
                }
            });

        var result = await sut.AddNewAsync(new Account(Guid.NewGuid(), "GitHub", "AAAA"));

        Assert.False(result.IsSuccess);
        Assert.Equal(AppErrorCode.OtpStorageAccessDenied, result.GetErrorCode());
        Assert.False(File.Exists(storagePath));
        Assert.Empty(Directory.GetFiles(temp.Path, "master.totp.*.tmp"));
        Assert.Empty(Directory.GetFiles(temp.Path, "master.totp.*.rollback"));
    }

    [Fact]
    public async Task AddNewAsync_AfterCommit_ClearsTemporaryEncryptedBlob()
    {
        using var temp = new TempDir();
        var storagePath = Path.Combine(temp.Path, "master.totp");
        var vault = new TrackingVaultService();
        var sut = CreateSut(storagePath, vault);

        var result = await sut.AddNewAsync(new Account(Guid.NewGuid(), "GitHub", "AAAA"));

        Assert.True(result.IsSuccess);
        Assert.NotNull(vault.LastEncryptedBlob);
        Assert.All(vault.LastEncryptedBlob, value => Assert.Equal(0, value));
    }

    [Fact]
    public async Task GetAllAsync_WhenVaultCannotBeHardened_ReturnsAccessDenied()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var temp = new TempDir();
        var storagePath = Path.Combine(temp.Path, "master.totp");
        await File.WriteAllTextAsync(storagePath, "[]", cancellationToken);
        var sut = new AccountDAL(
            NullLogger<AccountDAL>.Instance,
            new EchoVaultService(),
            storagePath,
            new DelegatingPlatformFileSecurity
            {
                RestrictFile = _ => throw new UnauthorizedAccessException("denied")
            });

        var result = await sut.GetAllAsync();

        Assert.False(result.IsSuccess);
        Assert.Equal(AppErrorCode.OtpStorageAccessDenied, result.GetErrorCode());
    }

    [Fact]
    public async Task ExportEncryptedAsync_WhenStagedExportCannotBeHardened_PreservesExistingTarget()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var temp = new TempDir();
        var storagePath = Path.Combine(temp.Path, "master.totp");
        await File.WriteAllTextAsync(storagePath, "[]", cancellationToken);
        var targetPath = Path.Combine(temp.Path, "export.totp");
        var originalTarget = Encoding.UTF8.GetBytes("existing");
        await File.WriteAllBytesAsync(targetPath, originalTarget, cancellationToken);
        var sut = new AccountDAL(
            NullLogger<AccountDAL>.Instance,
            new EchoVaultService(),
            storagePath,
            new DelegatingPlatformFileSecurity
            {
                RestrictFile = path =>
                {
                    if (path.EndsWith(".tmp", StringComparison.Ordinal))
                    {
                        throw new UnauthorizedAccessException("denied");
                    }
                }
            });

        var result = await sut.ExportEncryptedAsync(targetPath);

        Assert.False(result.IsSuccess);
        Assert.Equal(AppErrorCode.ExportFileAccessDenied, result.GetErrorCode());
        Assert.Equal(originalTarget, await File.ReadAllBytesAsync(targetPath, cancellationToken));
        Assert.Empty(Directory.GetFiles(temp.Path, "export.totp.*.tmp"));
    }

    [Fact]
    public async Task BackupAsync_WhenStagedBackupCannotBeHardened_DoesNotRotateBackups()
    {
        using var temp = new TempDir();
        var storagePath = Path.Combine(temp.Path, "master.totp");
        var setupSut = CreateSut(storagePath, new EchoVaultService());
        Assert.True((await setupSut.AddNewAsync(new Account(Guid.NewGuid(), "GitHub", "AAAA"))).IsSuccess);
        var sut = new AccountDAL(
            NullLogger<AccountDAL>.Instance,
            new EchoVaultService(),
            storagePath,
            new DelegatingPlatformFileSecurity
            {
                RestrictFile = path =>
                {
                    if (Path.GetFileName(path).Contains(".bak1.", StringComparison.Ordinal)
                        && path.EndsWith(".tmp", StringComparison.Ordinal))
                    {
                        throw new UnauthorizedAccessException("denied");
                    }
                }
            });

        var result = await sut.BackupOtpEntriesStorageFileAsync();

        Assert.False(result.IsSuccess);
        Assert.Equal(AppErrorCode.OtpStorageBackupFailed, result.GetErrorCode());
        Assert.Empty(Directory.GetFiles(temp.Path, "*.bak*"));
    }

    [Fact]
    public async Task BackupAsync_WhenLatestStagingIsTruncated_DoesNotPublishBackup()
    {
        using var temp = new TempDir();
        var storagePath = Path.Combine(temp.Path, "master.totp");
        var setupSut = CreateSut(storagePath, new EchoVaultService());
        Assert.True((await setupSut.AddNewAsync(new Account(Guid.NewGuid(), "GitHub", "AAAA"))).IsSuccess);
        var sut = new AccountDAL(
            NullLogger<AccountDAL>.Instance,
            new EchoVaultService(),
            storagePath,
            new DelegatingPlatformFileSecurity
            {
                RestrictFile = path =>
                {
                    if (Path.GetFileName(path).Contains(".bak1.", StringComparison.Ordinal)
                        && path.EndsWith(".tmp", StringComparison.Ordinal))
                    {
                        File.WriteAllText(path, "[");
                    }
                }
            });

        var result = await sut.BackupOtpEntriesStorageFileAsync();

        Assert.False(result.IsSuccess);
        Assert.Equal(AppErrorCode.OtpStorageBackupFailed, result.GetErrorCode());
        Assert.Empty(Directory.GetFiles(temp.Path, "*.bak*"));
        Assert.Empty(Directory.GetFiles(temp.Path, "*.tmp"));
    }

    [Fact]
    public async Task BackupAsync_WhenOlderGenerationCommitFails_DoesNotPublishNewLatestBackup()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var temp = new TempDir();
        var storagePath = Path.Combine(temp.Path, "master.totp");
        var setupSut = CreateSut(storagePath, new EchoVaultService());
        Assert.True((await setupSut.AddNewAsync(new Account(Guid.NewGuid(), "One", "1111"))).IsSuccess);
        Assert.True((await setupSut.BackupOtpEntriesStorageFileAsync()).IsSuccess);
        Assert.True((await setupSut.AddNewAsync(new Account(Guid.NewGuid(), "Two", "2222"))).IsSuccess);
        Assert.True((await setupSut.BackupOtpEntriesStorageFileAsync()).IsSuccess);
        Assert.True((await setupSut.AddNewAsync(new Account(Guid.NewGuid(), "Three", "3333"))).IsSuccess);
        var originalLatest = await File.ReadAllBytesAsync($"{storagePath}.bak1", cancellationToken);
        var originalSecond = await File.ReadAllBytesAsync($"{storagePath}.bak2", cancellationToken);
        var secondGenerationWasStaged = false;
        var sut = new AccountDAL(
            NullLogger<AccountDAL>.Instance,
            new EchoVaultService(),
            storagePath,
            new DelegatingPlatformFileSecurity
            {
                RestrictFile = path =>
                {
                    if (path.StartsWith($"{storagePath}.bak2.", StringComparison.Ordinal)
                        && path.EndsWith(".tmp", StringComparison.Ordinal))
                    {
                        secondGenerationWasStaged = true;
                    }
                    else if (secondGenerationWasStaged
                             && string.Equals(path, $"{storagePath}.bak2", StringComparison.Ordinal))
                    {
                        throw new UnauthorizedAccessException("denied during rotation");
                    }
                }
            });

        var result = await sut.BackupOtpEntriesStorageFileAsync();

        Assert.False(result.IsSuccess);
        Assert.Equal(AppErrorCode.OtpStorageBackupFailed, result.GetErrorCode());
        Assert.Equal(originalLatest, await File.ReadAllBytesAsync($"{storagePath}.bak1", cancellationToken));
        Assert.Equal(originalSecond, await File.ReadAllBytesAsync($"{storagePath}.bak2", cancellationToken));
        Assert.Empty(Directory.GetFiles(temp.Path, "*.tmp"));
        Assert.Empty(Directory.GetFiles(temp.Path, "*.rollback"));

        var vault = new EchoVaultService();
        foreach (var backupPath in Directory.GetFiles(temp.Path, "master.totp.bak*"))
            Assert.NotNull(vault.DecryptVault(await File.ReadAllBytesAsync(backupPath, cancellationToken)));
    }

    private static AccountDAL CreateSut(string storagePath, IVaultService vault) =>
        new(NullLogger<AccountDAL>.Instance, vault, storagePath, NoOpPlatformFileSecurity.Instance);

    private sealed class EchoVaultService : IVaultService
    {
        public List<Account> DecryptVault(byte[] encryptedBlob) =>
            System.Text.Json.JsonSerializer.Deserialize<List<Account>>(encryptedBlob) ?? [];

        public byte[] EncryptVault(List<Account> entries) =>
            System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(entries);
    }

    private sealed class ThrowingVaultService(Exception exception) : IVaultService
    {
        public List<Account> DecryptVault(byte[] encryptedBlob) => throw exception;
        public byte[] EncryptVault(List<Account> entries) => throw exception;
    }

    private sealed class TrackingVaultService : IVaultService
    {
        public byte[]? LastEncryptedBlob { get; private set; }

        public List<Account> DecryptVault(byte[] encryptedBlob) => [];

        public byte[] EncryptVault(List<Account> entries)
        {
            LastEncryptedBlob = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(entries);
            return LastEncryptedBlob;
        }
    }

    private sealed class TempDir : IDisposable
    {
        public TempDir()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "totp-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, recursive: true);
                }
            }
            catch
            {
                // best-effort test cleanup
            }
        }
    }
}
