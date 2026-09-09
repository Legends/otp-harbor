using FluentResults;
using Moq;
using TOTP.Core.Models;
using TOTP.Core.Security.Interfaces;
using TOTP.Avalonia.Desktop.Presentation;
using TOTP.Avalonia.Desktop.Localization;
using TOTP.Core.Enums;
using TOTP.Core.Services.Interfaces;

namespace TOTP.Tests.Avalonia.Presentation;

public sealed class SettingsPageViewModelTests
{
    [Fact]
    public void TransientNotices_UseFifteenHundredMillisecondsByDefault()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(1500), TransientNotificationDefaults.Duration);
    }

    [Fact]
    public void Constructor_DoesNotPersistValuesWhileLoading()
    {
        var settings = CreateSettings(new AppSettings());

        using var sut = new SettingsPageViewModel(
            settings.Object,
            autoSaveDelay: TimeSpan.Zero);

        settings.Verify(value => value.SaveAsync(), Times.Never);
    }

    [Fact]
    public async Task SaveAsync_PersistsReviewedSecurityPreferences()
    {
        var current = new AppSettings();
        var settings = CreateSettings(current);
        settings.Setup(value => value.SaveAsync()).ReturnsAsync(Result.Ok());
        using var sut = new SettingsPageViewModel(settings.Object)
        {
            IdleTimeoutMinutes = 25,
            LockOnMinimize = false
        };

        await sut.SaveAsync();

        Assert.Equal(TimeSpan.FromMinutes(25), current.IdleTimeout);
        Assert.False(current.LockOnMinimize);
        Assert.Equal("Settings saved automatically.", sut.Message);
        Assert.Equal(NotificationSeverity.Success, sut.MessageSeverity);
    }

    [Fact]
    public async Task SaveAsync_SuccessNoticeClearsAfterConfiguredTransientDuration()
    {
        var settings = CreateSettings(new AppSettings());
        settings.Setup(value => value.SaveAsync()).ReturnsAsync(Result.Ok());
        using var sut = new SettingsPageViewModel(
            settings.Object,
            transientMessageDuration: TimeSpan.FromMilliseconds(20));

        await sut.SaveAsync();
        Assert.Equal(NotificationSeverity.Success, sut.MessageSeverity);
        Assert.False(string.IsNullOrWhiteSpace(sut.Message));

        await Task.Delay(100, TestContext.Current.CancellationToken);

        Assert.Empty(sut.Message);
    }

    [Fact]
    public async Task SaveAsync_WhenPersistenceFails_RestoresActiveSettings()
    {
        var current = new AppSettings
        {
            IdleTimeout = TimeSpan.FromMinutes(10),
            LockOnMinimize = true
        };
        var settings = CreateSettings(current);
        settings.Setup(value => value.SaveAsync())
            .ReturnsAsync(Result.Fail("synthetic failure"));
        using var sut = new SettingsPageViewModel(settings.Object)
        {
            IdleTimeoutMinutes = 60,
            LockOnMinimize = false
        };

        await sut.SaveAsync();

        Assert.Equal(TimeSpan.FromMinutes(10), current.IdleTimeout);
        Assert.True(current.LockOnMinimize);
        Assert.DoesNotContain("synthetic", sut.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(NotificationSeverity.Error, sut.MessageSeverity);
    }

    [Fact]
    public void SelectedLanguage_AppliesImmediatelyWithoutChangingSecuritySettings()
    {
        var current = new AppSettings
        {
            IdleTimeout = TimeSpan.FromMinutes(12),
            LockOnMinimize = true
        };
        var localization = new Mock<IAvaloniaLocalizationService>();
        var english = new LanguageOption("en", "English");
        var german = new LanguageOption("de", "Deutsch");
        localization.SetupGet(value => value.SupportedLanguages).Returns([english, german]);
        localization.SetupGet(value => value.CurrentLanguage).Returns(english);
        using var sut = new SettingsPageViewModel(CreateSettings(current).Object, localization.Object);

        sut.SelectedLanguage = german;

        localization.Verify(value => value.ApplyCulture("de"), Times.Once);
        Assert.Equal(TimeSpan.FromMinutes(12), current.IdleTimeout);
        Assert.True(current.LockOnMinimize);
    }

    [Fact]
    public void CultureChanged_AfterStoredSettingsLoad_SynchronizesLanguageSelector()
    {
        var english = new LanguageOption("en", "English");
        var german = new LanguageOption("de", "Deutsch");
        var currentLanguage = english;
        var localization = new Mock<IAvaloniaLocalizationService>();
        localization.SetupGet(value => value.SupportedLanguages).Returns([english, german]);
        localization.SetupGet(value => value.CurrentLanguage)
            .Returns(() => currentLanguage);
        using var sut = new SettingsPageViewModel(
            CreateSettings(new AppSettings { CultureName = "de" }).Object,
            localization.Object,
            autoSaveDelay: TimeSpan.Zero);

        currentLanguage = german;
        localization.Raise(value => value.CultureChanged += null, EventArgs.Empty);

        Assert.Same(german, sut.SelectedLanguage);
        localization.Verify(value => value.ApplyCulture(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task SaveAsync_PersistsCompletePortablePreferenceSet()
    {
        var current = new AppSettings();
        var settings = CreateSettings(current);
        settings.Setup(value => value.SaveAsync()).ReturnsAsync(Result.Ok());
        using var sut = new SettingsPageViewModel(settings.Object)
        {
            IdleTimeoutMinutes = 45,
            LockOnMinimize = false,
            LockOnSessionLock = false,
            ClearClipboardEnabled = true,
            ClearClipboardSeconds = 20,
            QrPreviewScaleFactor = 2.5m,
            OpenExportFileAfterExport = false,
            MinimumLogLevel = AppLogLevel.Warning
        };
        sut.SelectedInterfaceScale = Assert.Single(
            sut.InterfaceScales,
            option => option.Percent == 175);

        await sut.SaveAsync();

        Assert.Equal(TimeSpan.FromMinutes(45), current.IdleTimeout);
        Assert.False(current.LockOnMinimize);
        Assert.False(current.LockOnSessionLock);
        Assert.True(current.ClearClipboardEnabled);
        Assert.Equal(20, current.ClearClipboardSeconds);
        Assert.Equal(2.5, current.QrPreviewScaleFactor);
        Assert.Equal(175, current.InterfaceScalePercent);
        Assert.False(current.OpenExportFileAfterExport);
        Assert.Equal(AppLogLevel.Warning, current.MinimumLogLevel);
    }


    [Fact]
    public async Task SaveAsync_WhenInterfaceScaleChanges_ExplainsRestartRequirement()
    {
        var current = new AppSettings();
        var settings = CreateSettings(current);
        settings.Setup(value => value.SaveAsync()).ReturnsAsync(Result.Ok());
        using var sut = new SettingsPageViewModel(
            settings.Object,
            transientMessageDuration: TimeSpan.FromMilliseconds(20));
        sut.SelectedInterfaceScale = Assert.Single(
            sut.InterfaceScales,
            option => option.Percent == 200);

        await sut.SaveAsync();

        Assert.Equal(200, current.InterfaceScalePercent);
        Assert.Contains("restart", sut.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(NotificationSeverity.Information, sut.MessageSeverity);
        await Task.Delay(100, TestContext.Current.CancellationToken);
        Assert.Empty(sut.Message);
    }

    [Fact]
    public async Task OpenLogFolderAsync_UsesPlatformPathWithoutDisplayingIt()
    {
        var settings = CreateSettings(new AppSettings());
        var paths = new Mock<IPlatformApplicationPaths>();
        paths.SetupGet(value => value.LogDirectory).Returns(@"C:\synthetic\logs");
        var launcher = new Mock<IPlatformFolderLauncher>();
        launcher.Setup(value => value.OpenFolderAsync(
                @"C:\synthetic\logs",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok());
        using var sut = new SettingsPageViewModel(
            settings.Object,
            applicationPaths: paths.Object,
            folderLauncher: launcher.Object);

        await sut.OpenLogFolderAsync();

        Assert.Contains("opened", sut.LogFolderMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("synthetic", sut.LogFolderMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(sut.Message);
        Assert.False(string.IsNullOrWhiteSpace(sut.VersionText));
    }

    [Fact]
    public async Task OpenLogFolderAsync_UsesActiveLocaleForCompleteSuccessNotice()
    {
        var paths = new Mock<IPlatformApplicationPaths>();
        paths.SetupGet(value => value.LogDirectory).Returns(@"C:\synthetic\logs");
        var launcher = new Mock<IPlatformFolderLauncher>();
        launcher.Setup(value => value.OpenFolderAsync(
                @"C:\synthetic\logs",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok());
        var localization = new Mock<IAvaloniaLocalizationService>();
        localization.Setup(value => value.GetString(AvaloniaStringKeys.LogFolderOpened))
            .Returns("Protokollordner geöffnet.");
        using var sut = new SettingsPageViewModel(
            CreateSettings(new AppSettings()).Object,
            localization.Object,
            paths.Object,
            launcher.Object);

        await sut.OpenLogFolderAsync();

        Assert.Equal("Protokollordner geöffnet.", sut.LogFolderMessage);
        Assert.Equal(NotificationSeverity.Success, sut.LogFolderMessageSeverity);
        Assert.Empty(sut.Message);
    }

    [Fact]
    public async Task OpenLogFolderAsync_FailureNotice_RemainsScopedToInfoTab()
    {
        var paths = new Mock<IPlatformApplicationPaths>();
        paths.SetupGet(value => value.LogDirectory).Returns(@"C:\synthetic\logs");
        var launcher = new Mock<IPlatformFolderLauncher>();
        launcher.Setup(value => value.OpenFolderAsync(
                @"C:\synthetic\logs",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Fail("synthetic failure"));
        using var sut = new SettingsPageViewModel(
            CreateSettings(new AppSettings()).Object,
            applicationPaths: paths.Object,
            folderLauncher: launcher.Object);

        await sut.OpenLogFolderAsync();

        Assert.False(string.IsNullOrWhiteSpace(sut.LogFolderMessage));
        Assert.Equal(NotificationSeverity.Error, sut.LogFolderMessageSeverity);
        Assert.Empty(sut.Message);
    }

    [Fact]
    public async Task ChangedPreference_IsSavedAutomatically()
    {
        var current = new AppSettings { LockOnMinimize = true };
        var settings = CreateSettings(current);
        var saved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        settings.Setup(value => value.SaveAsync()).Returns(() =>
        {
            saved.TrySetResult();
            return Task.FromResult(Result.Ok());
        });
        using var sut = new SettingsPageViewModel(
            settings.Object,
            autoSaveDelay: TimeSpan.Zero);

        sut.LockOnMinimize = false;
        await saved.Task.WaitAsync(
            TimeSpan.FromSeconds(1),
            TestContext.Current.CancellationToken);

        Assert.False(current.LockOnMinimize);
        settings.Verify(value => value.SaveAsync(), Times.Once);
    }

    [Fact]
    public async Task RapidPreferenceChanges_ArePersistedAsOneSnapshot()
    {
        var current = new AppSettings();
        var settings = CreateSettings(current);
        var saved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        settings.Setup(value => value.SaveAsync()).Returns(() =>
        {
            saved.TrySetResult();
            return Task.FromResult(Result.Ok());
        });
        using var sut = new SettingsPageViewModel(
            settings.Object,
            autoSaveDelay: TimeSpan.FromMilliseconds(25));

        sut.IdleTimeoutMinutes = 15;
        sut.ClearClipboardSeconds = 20;
        sut.OpenExportFileAfterExport = false;
        await saved.Task.WaitAsync(
            TimeSpan.FromSeconds(1),
            TestContext.Current.CancellationToken);
        await Task.Delay(50, TestContext.Current.CancellationToken);

        Assert.Equal(TimeSpan.FromMinutes(15), current.IdleTimeout);
        Assert.Equal(20, current.ClearClipboardSeconds);
        Assert.False(current.OpenExportFileAfterExport);
        settings.Verify(value => value.SaveAsync(), Times.Once);
    }

    private static Mock<ISettingsService> CreateSettings(AppSettings current)
    {
        var settings = new Mock<ISettingsService>();
        settings.SetupGet(value => value.Current).Returns(current);
        return settings;
    }
}
