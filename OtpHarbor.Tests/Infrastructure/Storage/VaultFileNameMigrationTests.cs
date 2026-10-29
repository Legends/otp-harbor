using Microsoft.Extensions.Configuration;
using Moq;
using TOTP.Core.Services.Interfaces;
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
        }
        fileSecurity.Verify(
            value => value.RestrictFileToCurrentUser(It.IsAny<string>()),
            Times.Exactly(6));
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
            Times.Once);
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
        var migration = source.IndexOf(
            "VaultFileNameMigration.MigrateIfUsingDefaultPath(",
            StringComparison.Ordinal);

        Assert.True(instanceOutcome >= 0);
        Assert.True(activationFailureGuard > instanceOutcome);
        Assert.True(migration > activationFailureGuard);
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
