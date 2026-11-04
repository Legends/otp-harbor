using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NSec.Cryptography;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TOTP.Core.Backup;
using TOTP.Core.Models;
using TOTP.Core.Security;
using TOTP.Core.Security.Interfaces;
using TOTP.Core.Security.Models;
using TOTP.Core.Services.Interfaces;
using TOTP.DAL.Services;
using TOTP.DAL.Storage;
using TOTP.Infrastructure.Security;
using TOTP.Infrastructure.Services;
using TOTP.Infrastructure.Storage;

namespace TOTP.Tests.Infrastructure.Storage;

public sealed class VaultFileNameMigrationTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"otp-harbor-vault-name-migration-{Guid.NewGuid():N}");

    [Fact]
    public void MigrateDefaultVaultFile_WithLegacyVaultAndBackups_MovesEveryFile()
    {
        Directory.CreateDirectory(_root);
        var paths = CreatePaths();
        var fileSecurity = new Mock<IPlatformFileSecurity>();
        Write("master.totp", "encrypted-vault");
        Write("authorization-envelope.bin", "encrypted-authorization");
        for (var generation = 1; generation <= 5; generation++)
            Write($"master.totp.bak{generation}", $"encrypted-backup-{generation}");

        Migrate(paths.Object, fileSecurity.Object);

        Assert.Equal("encrypted-vault", Read("master.vault"));
        Assert.False(File.Exists(Path.Combine(_root, "master.totp")));
        for (var generation = 1; generation <= 5; generation++)
        {
            Assert.Equal(
                $"encrypted-backup-{generation}",
                Read($"master.vault.bak{generation}"));
            Assert.False(File.Exists(Path.Combine(_root, $"master.totp.bak{generation}")));
            Assert.Equal($"encrypted-backup-{generation}", Read($"pre-upgrade-rollback/master.totp.bak{generation}"));
        }
        Assert.Equal("encrypted-vault", Read("pre-upgrade-rollback/master.totp"));
        Assert.Equal("encrypted-authorization", Read("pre-upgrade-rollback/authorization-envelope.bin"));
        fileSecurity.Verify(
            value => value.RestrictDirectoryToCurrentUser(
                Path.Combine(_root, LegacyVaultRollbackSnapshot.DirectoryName)),
            Times.AtLeastOnce());
    }

    [Fact]
    public void MigrateDefaultVaultFile_WhenCurrentVaultExists_DoesNotOverwriteOrMoveLegacyFiles()
    {
        Directory.CreateDirectory(_root);
        var paths = CreatePaths();
        var fileSecurity = new Mock<IPlatformFileSecurity>();
        Write("master.vault", "current-encrypted-vault");
        Write("master.totp", "legacy-encrypted-vault");
        Write("master.totp.bak1", "legacy-encrypted-backup");

        Migrate(paths.Object, fileSecurity.Object);

        Assert.Equal("current-encrypted-vault", Read("master.vault"));
        Assert.Equal("legacy-encrypted-vault", Read("master.totp"));
        Assert.Equal("legacy-encrypted-backup", Read("master.totp.bak1"));
        fileSecurity.VerifyNoOtherCalls();
    }

    [Fact]
    public void MigrateDefaultVaultFile_WhenPermissionHardeningFails_LeavesLegacyVaultInPlace()
    {
        Directory.CreateDirectory(_root);
        var paths = CreatePaths();
        var legacyPath = Write("master.totp", "encrypted-vault");
        var fileSecurity = new Mock<IPlatformFileSecurity>();
        fileSecurity
            .Setup(value => value.RestrictFileToCurrentUser(legacyPath))
            .Throws<UnauthorizedAccessException>();

        Assert.Throws<UnauthorizedAccessException>(() =>
            Migrate(paths.Object, fileSecurity.Object));

        Assert.Equal("encrypted-vault", Read("master.totp"));
        Assert.False(File.Exists(Path.Combine(_root, "master.vault")));
    }

    [Fact]
    public void MigrateDefaultVaultFile_WhenBackupHardeningFails_DoesNotMoveTheMainVault()
    {
        Directory.CreateDirectory(_root);
        var paths = CreatePaths();
        Write("master.totp", "encrypted-vault");
        var backupPath = Write("master.totp.bak1", "encrypted-backup");
        var fileSecurity = new Mock<IPlatformFileSecurity>();
        fileSecurity
            .Setup(value => value.RestrictFileToCurrentUser(backupPath))
            .Throws<UnauthorizedAccessException>();

        Assert.Throws<UnauthorizedAccessException>(() =>
            Migrate(paths.Object, fileSecurity.Object));

        Assert.Equal("encrypted-vault", Read("master.totp"));
        Assert.Equal("encrypted-backup", Read("master.totp.bak1"));
        Assert.False(File.Exists(Path.Combine(_root, "master.vault")));
    }

    [Fact]
    public void MigrateDefaultVaultFile_WhenNewBackupAlreadyExists_PreservesBothBackupVersions()
    {
        Directory.CreateDirectory(_root);
        var paths = CreatePaths();
        var fileSecurity = new Mock<IPlatformFileSecurity>();
        Write("master.totp", "legacy-encrypted-vault");
        Write("master.totp.bak1", "legacy-encrypted-backup");
        Write("master.vault.bak1", "current-encrypted-backup");

        Migrate(paths.Object, fileSecurity.Object);

        Assert.Equal("legacy-encrypted-vault", Read("master.vault"));
        Assert.Equal("legacy-encrypted-backup", Read("master.totp.bak1"));
        Assert.Equal("current-encrypted-backup", Read("master.vault.bak1"));
        fileSecurity.Verify(
            value => value.RestrictFileToCurrentUser(
                Path.Combine(_root, "master.totp")),
            Times.AtLeastOnce());
    }

    [Fact]
    public void MigrateDefaultVaultFile_WhenExistingRollbackCopyDiffers_LeavesVaultInPlace()
    {
        Directory.CreateDirectory(_root);
        var paths = CreatePaths();
        var fileSecurity = Mock.Of<IPlatformFileSecurity>();
        var legacy = Write("master.totp", "encrypted-vault");
        LegacyVaultRollbackSnapshot.Ensure(legacy,
            Path.Combine(_root, "authorization-envelope.bin"), fileSecurity);
        Write("pre-upgrade-rollback/master.totp", "tampered-copy");

        Assert.Throws<InvalidDataException>(() => Migrate(paths.Object, fileSecurity));

        Assert.Equal("encrypted-vault", Read("master.totp"));
        Assert.False(File.Exists(Path.Combine(_root, "master.vault")));
    }

    [Fact]
    public void MigrateDefaultVaultFile_AfterInterruptedBackupMove_ResumesFromVerifiedSnapshot()
    {
        Directory.CreateDirectory(_root);
        var paths = CreatePaths();
        var fileSecurity = Mock.Of<IPlatformFileSecurity>();
        var legacy = Write("master.totp", "encrypted-vault");
        Write("master.totp.bak1", "encrypted-backup-1");
        Write("master.totp.bak2", "encrypted-backup-2");
        LegacyVaultRollbackSnapshot.Ensure(legacy,
            Path.Combine(_root, "authorization-envelope.bin"), fileSecurity);
        File.Move(Path.Combine(_root, "master.totp.bak1"),
            Path.Combine(_root, "master.vault.bak1"));

        Migrate(paths.Object, fileSecurity);

        Assert.Equal("encrypted-vault", Read("master.vault"));
        Assert.Equal("encrypted-backup-1", Read("master.vault.bak1"));
        Assert.Equal("encrypted-backup-2", Read("master.vault.bak2"));
        Assert.Equal("encrypted-backup-1", Read("pre-upgrade-rollback/master.totp.bak1"));
    }

    [Fact]
    public void MigrateDefaultVaultFile_WhenRollbackDirectoryHardeningFails_LeavesVaultInPlace()
    {
        Directory.CreateDirectory(_root);
        var paths = CreatePaths();
        Write("master.totp", "encrypted-vault");
        var fileSecurity = new Mock<IPlatformFileSecurity>();
        fileSecurity.Setup(value => value.RestrictDirectoryToCurrentUser(
                It.Is<string>(path => path.Contains("pre-upgrade-rollback.", StringComparison.Ordinal))))
            .Throws<UnauthorizedAccessException>();

        Assert.Throws<UnauthorizedAccessException>(() => Migrate(paths.Object, fileSecurity.Object));

        Assert.Equal("encrypted-vault", Read("master.totp"));
        Assert.False(Directory.Exists(Path.Combine(_root, LegacyVaultRollbackSnapshot.DirectoryName)));
        Assert.False(Directory.EnumerateDirectories(_root, "pre-upgrade-rollback.*.tmp").Any());
    }

    [Fact]
    public void MigrateDefaultVaultFile_WhenAlreadyRenamedLegacyVaultExists_PreservesRollbackCopy()
    {
        Directory.CreateDirectory(_root);
        var paths = CreatePaths();
        var fileSecurity = Mock.Of<IPlatformFileSecurity>();
        Write("master.vault", "TVLT-encrypted-vault");
        Write("authorization-envelope.bin", "encrypted-authorization");

        Migrate(paths.Object, fileSecurity);

        Assert.Equal("TVLT-encrypted-vault", Read("master.vault"));
        Assert.Equal("TVLT-encrypted-vault", Read("pre-upgrade-rollback/master.totp"));
        Assert.Equal("encrypted-authorization", Read("pre-upgrade-rollback/authorization-envelope.bin"));
    }

    [Fact]
    public void RollbackSnapshot_WhenAuthorizationEnvelopeIsInAnotherDirectory_PreservesIt()
    {
        Directory.CreateDirectory(Path.Combine(_root, "vault"));
        Directory.CreateDirectory(Path.Combine(_root, "config"));
        var vault = Write("vault/master.totp", "encrypted-vault");
        var envelope = Write("config/authorization-envelope.bin", "portable-wrapper");

        LegacyVaultRollbackSnapshot.Ensure(vault, envelope, Mock.Of<IPlatformFileSecurity>());

        Assert.Equal("encrypted-vault", Read("vault/pre-upgrade-rollback/master.totp"));
        Assert.Equal("portable-wrapper", Read("vault/pre-upgrade-rollback/authorization-envelope.bin"));
    }

    [Fact]
    public async Task RollbackSnapshot_AfterV3VaultWrite_RestoresV2VaultWithOriginalPassword()
    {
        Directory.CreateDirectory(_root);
        const string password = "synthetic-rollback-password";
        var cancellationToken = TestContext.Current.CancellationToken;
        var dek = RandomNumberGenerator.GetBytes(32);
        var nonce = RandomNumberGenerator.GetBytes(AeadAlgorithm.Aes256Gcm.NonceSize);
        var accountId = Guid.NewGuid();
        var group = new AccountGroup(Guid.NewGuid(), "Synthetic", "#4F6BED");
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(new[]
        {
            new
            {
                id = accountId,
                issuer = "Example",
                secret = "JBSWY3DPEHPK3PXP",
                account_name = "demo@example.invalid",
                period = 30,
                group,
                favorite = false
            }
        });
        byte[]? ciphertext = null;
        byte[]? recoveredDek = null;

        try
        {
            using (var key = Key.Import(AeadAlgorithm.Aes256Gcm, dek, KeyBlobFormat.RawSymmetricKey))
                ciphertext = AeadAlgorithm.Aes256Gcm.Encrypt(key, nonce, ReadOnlySpan<byte>.Empty, plaintext);
            var originalVault = Encoding.ASCII.GetBytes("TVLT").Concat(nonce).Concat(ciphertext).ToArray();
            var passwordService = new MasterPasswordService(NullLogger<MasterPasswordService>.Instance);
            var wrapper = await passwordService.WrapKeyV2Async(dek, password, cancellationToken);
            var portableEnvelope = new AuthorizationEnvelopeV2
            {
                PasswordWrapper = wrapper
            };
            var envelope = AuthorizationEnvelopeV2Codec.Serialize(portableEnvelope);
            Assert.True(envelope.IsSuccess);
            File.WriteAllBytes(Path.Combine(_root, "master.totp"), originalVault);
            File.WriteAllBytes(Path.Combine(_root, "master.totp.bak1"), originalVault);
            File.WriteAllBytes(Path.Combine(_root, "authorization-envelope.bin"), envelope.Value);

            Migrate(CreatePaths().Object, Mock.Of<IPlatformFileSecurity>());
            var snapshot = Path.Combine(_root, LegacyVaultRollbackSnapshot.DirectoryName);
            Assert.Equal(originalVault, File.ReadAllBytes(Path.Combine(snapshot, "master.totp")));
            Assert.Equal(originalVault, File.ReadAllBytes(Path.Combine(snapshot, "master.totp.bak1")));
            Assert.Equal(envelope.Value, File.ReadAllBytes(Path.Combine(snapshot, "authorization-envelope.bin")));

            var currentSecurity = new Mock<ISecurityContext>();
            currentSecurity.SetupGet(value => value.IsUnlocked).Returns(true);
            currentSecurity.Setup(value => value.GetDekCopy()).Returns(() => (byte[])dek.Clone());
            var currentPath = Path.Combine(_root, "master.vault");
            var accountDal = new AccountDAL(
                NullLogger<AccountDAL>.Instance,
                new VaultService(currentSecurity.Object),
                currentPath,
                Mock.Of<IPlatformFileSecurity>(),
                Path.Combine(_root, "authorization-envelope.bin"));
            var imported = await accountDal.GetAllAsync();
            Assert.True(imported.IsSuccess);
            Assert.Equal(accountId, Assert.Single(imported.Value).ID);
            var upgraded = File.ReadAllBytes(currentPath);
            Assert.Equal("TVL2", Encoding.ASCII.GetString(upgraded, 0, 4));
            var secondLoad = await accountDal.GetAllAsync();
            Assert.True(secondLoad.IsSuccess);
            Assert.Equal(upgraded, File.ReadAllBytes(currentPath));

            var legacyBackup = AutomaticBackupPackageV1Codec.Serialize(new AutomaticBackupPackageV1
            {
                CreatedUtc = DateTimeOffset.UtcNow,
                VaultSha256 = Convert.ToHexString(SHA256.HashData(originalVault)),
                EncryptedVault = (byte[])originalVault.Clone(),
                AuthorizationEnvelope = portableEnvelope
            });
            Assert.True(legacyBackup.IsSuccess);
            var backupReader = new AutomaticBackupSnapshotService(
                currentPath,
                Mock.Of<IAuthorizationEnvelopeStore>(),
                passwordService,
                new VaultService(currentSecurity.Object),
                Mock.Of<IPlatformFileSecurity>(),
                TimeProvider.System,
                NullLogger<AutomaticBackupSnapshotService>.Instance);
            await using (var backupStream = new MemoryStream(legacyBackup.Value, writable: false))
            {
                var backupAccounts = await backupReader.ReadAccountsAsync(
                    backupStream, password, cancellationToken);
                Assert.True(backupAccounts.IsSuccess);
                Assert.Equal(accountId, Assert.Single(backupAccounts.Value).ID);
            }

            var v2Directory = Path.Combine(_root, "v2-restore", "TOTP-Manager");
            Directory.CreateDirectory(v2Directory);
            var restoredVaultPath = Path.Combine(v2Directory, "master.totp");
            var restoredEnvelopePath = Path.Combine(v2Directory, "authorization-envelope.bin");
            File.Copy(Path.Combine(snapshot, "master.totp"), restoredVaultPath);
            File.Copy(Path.Combine(snapshot, "authorization-envelope.bin"), restoredEnvelopePath);

            var restoredEnvelope = AuthorizationEnvelopeV2Codec.Deserialize(
                File.ReadAllBytes(restoredEnvelopePath));
            Assert.True(restoredEnvelope.IsSuccess);
            Assert.Null(await passwordService.UnwrapKeyV2Async(
                restoredEnvelope.Value.PasswordWrapper, "wrong-password", cancellationToken));
            recoveredDek = await passwordService.UnwrapKeyV2Async(
                restoredEnvelope.Value.PasswordWrapper, password, cancellationToken);
            Assert.NotNull(recoveredDek);
            var v2Security = new Mock<ISecurityContext>();
            v2Security.SetupGet(value => value.IsUnlocked).Returns(true);
            v2Security.Setup(value => value.GetDekCopy()).Returns(() => (byte[])recoveredDek.Clone());
            var restoredVault = Assert.Single(new VaultService(v2Security.Object)
                .DecryptVault(File.ReadAllBytes(restoredVaultPath)));

            Assert.Equal("TVLT", Encoding.ASCII.GetString(File.ReadAllBytes(restoredVaultPath), 0, 4));
            Assert.Equal(accountId, restoredVault.ID);
            Assert.Equal("Example", restoredVault.Issuer);
            File.Copy(Path.Combine(snapshot, "master.totp.bak1"), restoredVaultPath, overwrite: true);
            var restoredBackupAccount = Assert.Single(new VaultService(v2Security.Object)
                .DecryptVault(File.ReadAllBytes(restoredVaultPath)));
            Assert.Equal(accountId, restoredBackupAccount.ID);
            Assert.Equal(originalVault, File.ReadAllBytes(Path.Combine(snapshot, "master.totp")));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dek);
            CryptographicOperations.ZeroMemory(plaintext);
            if (ciphertext is not null) CryptographicOperations.ZeroMemory(ciphertext);
            if (recoveredDek is not null) CryptographicOperations.ZeroMemory(recoveredDek);
        }
    }

    [Fact]
    public void MigrateDefaultVaultFile_WhenLegacyPathIsDirectory_FailsClosed()
    {
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(Path.Combine(_root, "master.totp"));
        var paths = CreatePaths();

        Assert.Throws<InvalidDataException>(() =>
            Migrate(paths.Object, Mock.Of<IPlatformFileSecurity>()));
    }

    [Fact]
    public void MigrateDefaultVaultFile_WhenCurrentPathIsDirectory_FailsClosed()
    {
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(Path.Combine(_root, "master.vault"));
        var paths = CreatePaths();

        Assert.Throws<InvalidDataException>(() =>
            Migrate(paths.Object, Mock.Of<IPlatformFileSecurity>()));
    }

    [Fact]
    public void MigrateDefaultVaultFile_WhenNoLegacyVaultExists_DoesNothing()
    {
        Directory.CreateDirectory(_root);
        var paths = CreatePaths();
        var fileSecurity = new Mock<IPlatformFileSecurity>();

        Migrate(paths.Object, fileSecurity.Object);

        Assert.False(File.Exists(Path.Combine(_root, "master.vault")));
        fileSecurity.VerifyNoOtherCalls();
    }

    [Fact]
    public void MigrateIfUsingDefaultPath_WhenCustomVaultPathIsConfigured_DoesNothing()
    {
        Directory.CreateDirectory(_root);
        var paths = CreatePaths();
        var fileSecurity = new Mock<IPlatformFileSecurity>();
        Write("master.totp", "legacy-encrypted-vault");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Accounts:StorageFilePath"] = Path.Combine(_root, "custom.vault")
            })
            .Build();

        VaultFileNameMigration.MigrateIfUsingDefaultPath(
            configuration,
            paths.Object,
            fileSecurity.Object);

        Assert.Equal("legacy-encrypted-vault", Read("master.totp"));
        Assert.False(File.Exists(Path.Combine(_root, "master.vault")));
        fileSecurity.VerifyNoOtherCalls();
    }

    [Fact]
    public void MigrateIfUsingDefaultPath_WithCustomLegacyVault_PreservesRollbackCopyWithoutRenaming()
    {
        Directory.CreateDirectory(_root);
        var paths = CreatePaths();
        var customPath = Write("custom.vault", "TVLT-encrypted-vault");
        Write("authorization-envelope.bin", "encrypted-authorization");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Accounts:StorageFilePath"] = customPath
            })
            .Build();

        VaultFileNameMigration.MigrateIfUsingDefaultPath(
            configuration, paths.Object, Mock.Of<IPlatformFileSecurity>());

        Assert.Equal("TVLT-encrypted-vault", Read("custom.vault"));
        Assert.Equal("TVLT-encrypted-vault", Read("pre-upgrade-rollback/master.totp"));
        Assert.Equal("encrypted-authorization", Read("pre-upgrade-rollback/authorization-envelope.bin"));
        Assert.False(File.Exists(Path.Combine(_root, "master.vault")));
    }

    [Fact]
    public void DesktopHost_RunsMigrationOnlyAfterSingleInstanceOwnershipIsConfirmed()
    {
        var source = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "Avalonia",
            "DesktopProgram.cs"));
        var instanceOutcome = source.IndexOf(
            "var outcome = instance.Start(activationRequest);",
            StringComparison.Ordinal);
        var activationFailureGuard = source.IndexOf(
            "if (outcome == SingleInstanceOutcome.ActivationFailed)",
            StringComparison.Ordinal);
        var directoryMigration = source.IndexOf(
            "var platformServices = DesktopPlatformServiceFactory.Create(",
            StringComparison.Ordinal);
        var migration = source.IndexOf(
            "VaultFileNameMigration.MigrateIfUsingDefaultPath(",
            StringComparison.Ordinal);

        Assert.True(instanceOutcome >= 0);
        Assert.True(activationFailureGuard > instanceOutcome);
        Assert.True(directoryMigration > activationFailureGuard);
        Assert.True(migration > directoryMigration);
    }

    [Fact]
    public void AndroidHost_RunsMigrationBeforeInfrastructureUsesTheVaultPath()
    {
        var source = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "Android",
            "AndroidCompositionRoot.cs"));
        var migration = source.IndexOf(
            "VaultFileNameMigration.MigrateIfUsingDefaultPath(",
            StringComparison.Ordinal);
        var infrastructure = source.IndexOf(
            "services.AddInfrastructure(configuration, paths, fileSecurity);",
            StringComparison.Ordinal);

        Assert.True(migration >= 0);
        Assert.True(infrastructure > migration);
    }

    private Mock<IPlatformApplicationPaths> CreatePaths()
    {
        var paths = new Mock<IPlatformApplicationPaths>();
        paths.SetupGet(value => value.VaultFilePath)
            .Returns(Path.Combine(_root, "master.vault"));
        paths.SetupGet(value => value.AuthorizationEnvelopeFilePath)
            .Returns(Path.Combine(_root, "authorization-envelope.bin"));
        return paths;
    }

    private static void Migrate(
        IPlatformApplicationPaths paths,
        IPlatformFileSecurity fileSecurity) =>
        VaultFileNameMigration.MigrateIfUsingDefaultPath(
            new ConfigurationBuilder().Build(),
            paths,
            fileSecurity);

    private string Write(string fileName, string contents)
    {
        var path = Path.Combine(_root, fileName);
        File.WriteAllText(path, contents);
        return path;
    }

    private string Read(string fileName) => File.ReadAllText(Path.Combine(_root, fileName));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
