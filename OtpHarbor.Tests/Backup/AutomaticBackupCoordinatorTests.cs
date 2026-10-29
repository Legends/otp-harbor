using FluentResults;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TOTP.Core.Backup;
using TOTP.Core.Models;
using TOTP.Core.Security;
using TOTP.Core.Services.Interfaces;
using TOTP.Infrastructure.Services;

namespace TOTP.Tests.Backup;

public sealed class AutomaticBackupCoordinatorTests
{
    private static readonly AutomaticBackupDestination Destination = new(
        "test-destination-v1",
        "opaque-location",
        "My backups");

    [Fact]
    public async Task ConfigureAsync_PublishesSnapshotAndPersistsSuccessfulFingerprint()
    {
        var store = CreateEmptyStore();
        AutomaticBackupConfigurationV1? finalConfiguration = null;
        store.Setup(value => value.SaveAsync(
                It.IsAny<AutomaticBackupConfigurationV1>(),
                It.IsAny<CancellationToken>()))
            .Callback<AutomaticBackupConfigurationV1, CancellationToken>((configuration, _) =>
                finalConfiguration = configuration)
            .ReturnsAsync(Result.Ok());
        var snapshots = new Mock<IAutomaticBackupSnapshotService>();
        snapshots.Setup(value => value.CreateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok(new AutomaticBackupSnapshot([1, 2, 3], new string('A', 64))));
        byte[]? published = null;
        var destination = CreateDestinationAdapter();
        destination.Setup(value => value.SelectAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok<AutomaticBackupDestination?>(Destination));
        destination.Setup(value => value.WriteAsync(
                Destination,
                It.IsAny<string>(),
                It.IsAny<ReadOnlyMemory<byte>>(),
                It.IsAny<CancellationToken>()))
            .Callback<AutomaticBackupDestination, string, ReadOnlyMemory<byte>, CancellationToken>(
                (_, _, payload, _) => published = payload.ToArray())
            .ReturnsAsync(Result.Ok());

        using var sut = CreateCoordinator(store, snapshots, destination);
        var result = await sut.ConfigureAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(AutomaticBackupRunStatus.Created, result.Value.Status);
        Assert.Equal(new byte[] { 1, 2, 3 }, published);
        Assert.Equal(new string('A', 64), finalConfiguration?.LastFingerprint);
        Assert.NotNull(finalConfiguration?.LastSuccessfulUtc);
        Assert.False(string.IsNullOrWhiteSpace(finalConfiguration?.LastSuccessfulFileName));
        Assert.Equal(AutomaticBackupDestinationAccess.Accessible, finalConfiguration?.DestinationAccess);
        Assert.Equal(AutomaticBackupStatusKind.Succeeded, sut.Status.Kind);
    }

    [Fact]
    public async Task BackupNowAsync_UnchangedSnapshot_DoesNotPublishDuplicate()
    {
        var fingerprint = new string('B', 64);
        const string fileName = "otp-harbor-auto-current.otphbackup";
        var configuration = new AutomaticBackupConfigurationV1
        {
            DestinationKind = Destination.Kind,
            DestinationLocation = Destination.Location,
            DestinationDisplayName = Destination.DisplayName,
            LastFingerprint = fingerprint,
            LastSuccessfulUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
            LastSuccessfulFileName = fileName
        };
        var store = new Mock<IAutomaticBackupConfigurationStore>();
        store.Setup(value => value.LoadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok<AutomaticBackupConfigurationV1?>(configuration));
        var snapshots = new Mock<IAutomaticBackupSnapshotService>();
        snapshots.Setup(value => value.CreateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok(new AutomaticBackupSnapshot([9], fingerprint)));
        var destination = CreateDestinationAdapter();
        destination.Setup(value => value.ListAsync(
                It.IsAny<AutomaticBackupDestination>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok<IReadOnlyList<AutomaticBackupStoredFile>>(
                [new("current", fileName, configuration.LastSuccessfulUtc!.Value)]));

        using var sut = CreateCoordinator(store, snapshots, destination);
        var result = await sut.BackupNowAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(AutomaticBackupRunStatus.UpToDate, result.Value.Status);
        destination.Verify(value => value.WriteAsync(
            It.IsAny<AutomaticBackupDestination>(),
            It.IsAny<string>(),
            It.IsAny<ReadOnlyMemory<byte>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task BackupNowAsync_EmptyVault_DoesNotPublishSnapshot()
    {
        var configuration = new AutomaticBackupConfigurationV1
        {
            DestinationKind = Destination.Kind,
            DestinationLocation = Destination.Location,
            DestinationDisplayName = Destination.DisplayName
        };
        var store = new Mock<IAutomaticBackupConfigurationStore>();
        store.Setup(value => value.LoadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok<AutomaticBackupConfigurationV1?>(configuration));
        var snapshots = new Mock<IAutomaticBackupSnapshotService>();
        var destination = CreateDestinationAdapter();
        var accounts = new Mock<IAccountDAL>();
        accounts.Setup(value => value.GetAllAsync())
            .ReturnsAsync(Result.Ok(new List<Account>()));

        using var sut = CreateCoordinator(store, snapshots, destination, accounts);
        var result = await sut.BackupNowAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(AutomaticBackupRunStatus.UpToDate, result.Value.Status);
        Assert.Null(result.Value.CreatedUtc);
        snapshots.Verify(value => value.CreateAsync(It.IsAny<CancellationToken>()), Times.Never);
        destination.Verify(value => value.WriteAsync(
            It.IsAny<AutomaticBackupDestination>(),
            It.IsAny<string>(),
            It.IsAny<ReadOnlyMemory<byte>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task BackupNowAsync_FailedPublication_DoesNotAdvanceFingerprint()
    {
        var configuration = new AutomaticBackupConfigurationV1
        {
            DestinationKind = Destination.Kind,
            DestinationLocation = Destination.Location,
            DestinationDisplayName = Destination.DisplayName
        };
        var store = new Mock<IAutomaticBackupConfigurationStore>();
        store.Setup(value => value.LoadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok<AutomaticBackupConfigurationV1?>(configuration));
        var snapshots = new Mock<IAutomaticBackupSnapshotService>();
        snapshots.Setup(value => value.CreateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok(new AutomaticBackupSnapshot([4, 5, 6], new string('C', 64))));
        var destination = CreateDestinationAdapter();
        store.Setup(value => value.SaveAsync(
                It.IsAny<AutomaticBackupConfigurationV1>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok());
        destination.Setup(value => value.WriteAsync(
                It.IsAny<AutomaticBackupDestination>(),
                It.IsAny<string>(),
                It.IsAny<ReadOnlyMemory<byte>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Fail("write failed"));

        using var sut = CreateCoordinator(store, snapshots, destination);
        var result = await sut.BackupNowAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsFailed);
        Assert.Equal(AutomaticBackupStatusKind.Failed, sut.Status.Kind);
        store.Verify(value => value.SaveAsync(
            It.Is<AutomaticBackupConfigurationV1>(saved =>
                saved.LastFingerprint == null
                && saved.LastFailureKind == AutomaticBackupFailureKind.WriteFailed),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task InitializeAsync_InaccessibleDestination_PersistsVisibleFailureHealth()
    {
        var configuration = new AutomaticBackupConfigurationV1
        {
            DestinationKind = Destination.Kind,
            DestinationLocation = Destination.Location,
            DestinationDisplayName = Destination.DisplayName
        };
        AutomaticBackupConfigurationV1? persisted = null;
        var store = new Mock<IAutomaticBackupConfigurationStore>();
        store.Setup(value => value.LoadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok<AutomaticBackupConfigurationV1?>(configuration));
        store.Setup(value => value.SaveAsync(It.IsAny<AutomaticBackupConfigurationV1>(), It.IsAny<CancellationToken>()))
            .Callback<AutomaticBackupConfigurationV1, CancellationToken>((value, _) => persisted = value)
            .ReturnsAsync(Result.Ok());
        var destination = CreateDestinationAdapter();
        destination.Setup(value => value.ListAsync(It.IsAny<AutomaticBackupDestination>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Fail<IReadOnlyList<AutomaticBackupStoredFile>>("revoked"));

        using var sut = CreateCoordinator(store, new Mock<IAutomaticBackupSnapshotService>(), destination);
        var result = await sut.InitializeAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(AutomaticBackupStatusKind.DestinationUnavailable, sut.Status.Kind);
        Assert.Equal(AutomaticBackupDestinationAccess.Unavailable, sut.Status.DestinationAccess);
        Assert.Equal(AutomaticBackupFailureKind.DestinationUnavailable, sut.Status.LastFailureKind);
        Assert.Equal(AutomaticBackupFailureKind.DestinationUnavailable, persisted?.LastFailureKind);
    }

    [Fact]
    public async Task EnsureCurrentCheckpointAsync_MissingRecoveryFile_RecreatesUnchangedSnapshot()
    {
        var fingerprint = new string('F', 64);
        var configuration = new AutomaticBackupConfigurationV1
        {
            DestinationKind = Destination.Kind,
            DestinationLocation = Destination.Location,
            DestinationDisplayName = Destination.DisplayName,
            LastFingerprint = fingerprint,
            LastSuccessfulUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
            LastSuccessfulFileName = "otp-harbor-auto-missing.otphbackup"
        };
        var store = new Mock<IAutomaticBackupConfigurationStore>();
        store.Setup(value => value.LoadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok<AutomaticBackupConfigurationV1?>(configuration));
        store.Setup(value => value.SaveAsync(It.IsAny<AutomaticBackupConfigurationV1>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok());
        var snapshots = new Mock<IAutomaticBackupSnapshotService>();
        snapshots.Setup(value => value.CreateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok(new AutomaticBackupSnapshot([1], fingerprint)));
        var destination = CreateDestinationAdapter();
        destination.Setup(value => value.WriteAsync(
                It.IsAny<AutomaticBackupDestination>(),
                It.IsAny<string>(),
                It.IsAny<ReadOnlyMemory<byte>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok());

        using var sut = CreateCoordinator(store, snapshots, destination);
        var result = await sut.EnsureCurrentCheckpointAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(AutomaticBackupRunStatus.Created, result.Value.Status);
        destination.Verify(value => value.WriteAsync(
            It.IsAny<AutomaticBackupDestination>(),
            It.IsAny<string>(),
            It.IsAny<ReadOnlyMemory<byte>>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task BackupNowAsync_AfterSuccessfulPublication_AppliesGenerationRetention()
    {
        var configuration = new AutomaticBackupConfigurationV1
        {
            DestinationKind = Destination.Kind,
            DestinationLocation = Destination.Location,
            DestinationDisplayName = Destination.DisplayName,
            LastFingerprint = new string('D', 64)
        };
        var store = new Mock<IAutomaticBackupConfigurationStore>();
        store.Setup(value => value.LoadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok<AutomaticBackupConfigurationV1?>(configuration));
        store.Setup(value => value.SaveAsync(
                It.IsAny<AutomaticBackupConfigurationV1>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok());
        var snapshots = new Mock<IAutomaticBackupSnapshotService>();
        snapshots.Setup(value => value.CreateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok(new AutomaticBackupSnapshot([7, 8], new string('E', 64))));
        var destination = CreateDestinationAdapter();
        destination.Setup(value => value.WriteAsync(
                It.IsAny<AutomaticBackupDestination>(),
                It.IsAny<string>(),
                It.IsAny<ReadOnlyMemory<byte>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok());
        var newestDay = new DateTimeOffset(2026, 10, 17, 10, 0, 0, TimeSpan.Zero);
        var files = Enumerable.Range(0, 6)
            .Select(index => StoredFile($"same-day-{index}", newestDay.AddHours(-index)))
            .Concat(Enumerable.Range(1, 8)
                .Select(day => StoredFile($"daily-{day}", newestDay.AddDays(-day))))
            .ToList();
        destination.Setup(value => value.ListAsync(
                It.IsAny<AutomaticBackupDestination>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok<IReadOnlyList<AutomaticBackupStoredFile>>(files));
        var deleted = new List<string>();
        destination.Setup(value => value.DeleteAsync(
                It.IsAny<AutomaticBackupDestination>(),
                It.IsAny<AutomaticBackupStoredFile>(),
                It.IsAny<CancellationToken>()))
            .Callback<AutomaticBackupDestination, AutomaticBackupStoredFile, CancellationToken>(
                (_, file, _) => deleted.Add(file.Identifier))
            .ReturnsAsync(Result.Ok());

        using var sut = CreateCoordinator(store, snapshots, destination);
        var result = await sut.BackupNowAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            ["daily-7", "daily-8", "same-day-5"],
            deleted.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task BackupNowAsync_WhenRetentionDeleteFails_StopsDeletingThatDestination()
    {
        var configuration = new AutomaticBackupConfigurationV1
        {
            DestinationKind = Destination.Kind,
            DestinationLocation = Destination.Location,
            DestinationDisplayName = Destination.DisplayName,
            LastFingerprint = new string('D', 64)
        };
        var store = new Mock<IAutomaticBackupConfigurationStore>();
        store.Setup(value => value.LoadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok<AutomaticBackupConfigurationV1?>(configuration));
        store.Setup(value => value.SaveAsync(
                It.IsAny<AutomaticBackupConfigurationV1>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok());
        var snapshots = new Mock<IAutomaticBackupSnapshotService>();
        snapshots.Setup(value => value.CreateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok(new AutomaticBackupSnapshot([7, 8], new string('E', 64))));
        var destination = CreateDestinationAdapter();
        destination.Setup(value => value.WriteAsync(
                It.IsAny<AutomaticBackupDestination>(),
                It.IsAny<string>(),
                It.IsAny<ReadOnlyMemory<byte>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok());
        var files = Enumerable.Range(0, 9)
            .Select(index => StoredFile($"same-day-{index}", new DateTimeOffset(2026, 10, 17, 10, 0, 0, TimeSpan.Zero).AddMinutes(-index)))
            .ToList();
        destination.Setup(value => value.ListAsync(
                It.IsAny<AutomaticBackupDestination>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok<IReadOnlyList<AutomaticBackupStoredFile>>(files));
        destination.Setup(value => value.DeleteAsync(
                It.IsAny<AutomaticBackupDestination>(),
                It.IsAny<AutomaticBackupStoredFile>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Fail(new AutomaticBackupError(
                AutomaticBackupErrorCode.WriteFailed,
                "Synthetic retention failure.")));

        using var sut = CreateCoordinator(store, snapshots, destination);
        var result = await sut.BackupNowAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        destination.Verify(value => value.DeleteAsync(
            It.IsAny<AutomaticBackupDestination>(),
            It.IsAny<AutomaticBackupStoredFile>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    private static AutomaticBackupCoordinator CreateCoordinator(
        Mock<IAutomaticBackupConfigurationStore> store,
        Mock<IAutomaticBackupSnapshotService> snapshots,
        Mock<IAutomaticBackupDestinationAdapter> destination,
        Mock<IAccountDAL>? accounts = null) => new(
        store.Object,
        snapshots.Object,
        destination.Object,
        (accounts ?? CreateAccountDal()).Object,
        new AuthorizationState(),
        TimeProvider.System,
        NullLogger<AutomaticBackupCoordinator>.Instance);

    private static Mock<IAccountDAL> CreateAccountDal()
    {
        var account = new Account(Guid.NewGuid(), "Example", "JBSWY3DPEHPK3PXP", "user");
        var accounts = new Mock<IAccountDAL>();
        accounts.Setup(value => value.GetAllAsync())
            .ReturnsAsync(Result.Ok(new List<Account> { account }));
        return accounts;
    }

    private static Mock<IAutomaticBackupConfigurationStore> CreateEmptyStore()
    {
        var store = new Mock<IAutomaticBackupConfigurationStore>();
        store.Setup(value => value.LoadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok<AutomaticBackupConfigurationV1?>(null));
        return store;
    }

    private static Mock<IAutomaticBackupDestinationAdapter> CreateDestinationAdapter()
    {
        var destination = new Mock<IAutomaticBackupDestinationAdapter>();
        destination.SetupGet(value => value.Kind).Returns(Destination.Kind);
        destination.Setup(value => value.ListAsync(
                It.IsAny<AutomaticBackupDestination>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok<IReadOnlyList<AutomaticBackupStoredFile>>([]));
        return destination;
    }

    private static AutomaticBackupStoredFile StoredFile(string identifier, DateTimeOffset createdUtc) =>
        new(identifier, $"otp-harbor-auto-{identifier}.otphbackup", createdUtc);
}
