using FluentResults;
using Moq;
using TOTP.Avalonia.Desktop.Platform;
using TOTP.Avalonia.Desktop.Presentation.Dialogs;
using TOTP.Core.Backup;
using TOTP.Core.Services.Interfaces;

namespace TOTP.Tests.Avalonia.Presentation;

public sealed partial class NativeFilePickerViewModelTests
{
    [Theory]
    [InlineData(false, "cancel")]
    [InlineData(false, "failure")]
    [InlineData(false, "success")]
    [InlineData(true, "failure")]
    [InlineData(true, "success")]
    public void BackupToggle_ReflectsConfirmedConfigurationAfterCancellationFailureAndSuccess(bool configured, string outcome)
    {
        var ready = new AutomaticBackupStatus(true, false, "Backups", null, AutomaticBackupStatusKind.Ready);
        var status = configured ? ready : AutomaticBackupStatus.NotConfigured;
        var backup = new Mock<IAutomaticBackupCoordinator>();
        backup.SetupGet(value => value.Status).Returns(() => status);
        backup.Setup(value => value.ConfigureAsync(It.IsAny<CancellationToken>())).Returns(() =>
        {
            if (outcome == "failure") return Task.FromResult(Result.Fail<AutomaticBackupRunResult>("unavailable"));
            if (outcome == "success") status = ready;
            return Task.FromResult(Result.Ok(new AutomaticBackupRunResult(outcome == "cancel"
                ? AutomaticBackupRunStatus.NotConfigured : AutomaticBackupRunStatus.Created)));
        });
        backup.Setup(value => value.DisableAsync(It.IsAny<CancellationToken>())).Returns(() =>
        {
            if (outcome == "failure") return Task.FromResult(Result.Fail("unavailable"));
            status = AutomaticBackupStatus.NotConfigured;
            return Task.FromResult(Result.Ok());
        });
        using var sut = Create(Mock.Of<IAvaloniaFilePicker>(), Mock.Of<IExportService>(),
            Mock.Of<IAccountManager>(), Mock.Of<IAvaloniaDialogService>(), automaticBackup: backup.Object);
        var changed = new List<string?>();
        sut.PropertyChanged += (_, args) => changed.Add(args.PropertyName);
        Assert.Same(configured ? sut.DisableAutomaticBackupCommand : sut.ConfigureAutomaticBackupCommand, sut.ToggleAutomaticBackupCommand);
        sut.ToggleAutomaticBackupCommand.Execute(null);
        var expected = outcome == "success" ? !configured : configured;
        Assert.Equal(expected, sut.IsAutomaticBackupConfigured);
        Assert.Same(expected ? sut.DisableAutomaticBackupCommand : sut.ConfigureAutomaticBackupCommand, sut.ToggleAutomaticBackupCommand);
        Assert.Contains(nameof(sut.ToggleAutomaticBackupCommand), changed);
        Assert.Contains(nameof(sut.AutomaticBackupEnabledText), changed);
        Assert.True(sut.ToggleAutomaticBackupCommand.CanExecute(null));
        backup.Verify(value => value.ConfigureAsync(It.IsAny<CancellationToken>()), configured ? Times.Never() : Times.Once());
        backup.Verify(value => value.DisableAsync(It.IsAny<CancellationToken>()), configured ? Times.Once() : Times.Never());
    }

    [Theory]
    [InlineData("en", "Enabled", "Disabled")]
    [InlineData("de", "Aktiviert", "Deaktiviert")]
    [InlineData("fr", "Activées", "Désactivées")]
    [InlineData("es", "Activadas", "Desactivadas")]
    public void BackupStatus_UsesActiveLocaleAndExposesSelectedFolderWithoutChangingCoordinator(string locale, string enabled, string disabled)
    {
        var status = new AutomaticBackupStatus(true, false, "Backups", null, AutomaticBackupStatusKind.Ready);
        var backup = new Mock<IAutomaticBackupCoordinator>();
        backup.SetupGet(value => value.Status).Returns(() => status);
        backup.SetupGet(value => value.Destination).Returns(new AutomaticBackupDestination("desktop-folder-v1", @"\\server\backups", "Backups"));
        using var sut = Create(Mock.Of<IAvaloniaFilePicker>(), Mock.Of<IExportService>(),
            Mock.Of<IAccountManager>(), Mock.Of<IAvaloniaDialogService>(), automaticBackup: backup.Object, localization: Localization(locale));
        Assert.Equal(enabled, sut.AutomaticBackupEnabledText);
        Assert.Equal(@"\\server\backups", sut.AutomaticBackupFolderPath);
        Assert.True(sut.UseNetworkBackupFolder);
        sut.UseNetworkBackupFolder = false;
        Assert.False(sut.UseNetworkBackupFolder);
        backup.Verify(value => value.ConfigureAsync(It.IsAny<CancellationToken>()), Times.Never);
        status = AutomaticBackupStatus.NotConfigured;
        backup.Raise(value => value.StatusChanged += null, EventArgs.Empty);
        Assert.Equal(disabled, sut.AutomaticBackupEnabledText);
        Assert.False(sut.UseNetworkBackupFolder);
    }
}
