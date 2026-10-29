using FluentResults;
using Moq;
using Serilog.Events;
using TOTP.Avalonia.Desktop.Startup;
using TOTP.Core.Enums;
using TOTP.Core.Models;
using TOTP.Core.Security.Interfaces;
using TOTP.Infrastructure.Logging;

namespace TOTP.Tests.Avalonia.Startup;

public sealed class AvaloniaLoggingPreferenceBootstrapperTests
{
    [Fact]
    public void ApplyFromPreferences_SavedLevel_UpdatesStartupLogSwitch()
    {
        var previous = LogSwitchService.SharedSwitch.MinimumLevel;
        var store = CreateStore(new AppPreferencesV1
        {
            MinimumLogLevel = AppLogLevel.Warning
        });

        try
        {
            var applied = AvaloniaLoggingPreferenceBootstrapper.ApplyFromPreferences(
                store.Object,
                commandLineOverride: null);

            Assert.True(applied);
            Assert.Equal(LogEventLevel.Warning, LogSwitchService.SharedSwitch.MinimumLevel);
        }
        finally
        {
            LogSwitchService.SharedSwitch.MinimumLevel = previous;
        }
    }

    [Fact]
    public void ApplyFromPreferences_CommandLineOverride_PreservesActiveLogSwitch()
    {
        var previous = LogSwitchService.SharedSwitch.MinimumLevel;
        var store = CreateStore(new AppPreferencesV1
        {
            MinimumLogLevel = AppLogLevel.Warning
        });

        try
        {
            LogSwitchService.SharedSwitch.MinimumLevel = LogEventLevel.Debug;

            var applied = AvaloniaLoggingPreferenceBootstrapper.ApplyFromPreferences(
                store.Object,
                AppLogLevel.Debug);

            Assert.False(applied);
            Assert.Equal(LogEventLevel.Debug, LogSwitchService.SharedSwitch.MinimumLevel);
            store.Verify(
                value => value.LoadAsync(It.IsAny<CancellationToken>()),
                Times.Never);
        }
        finally
        {
            LogSwitchService.SharedSwitch.MinimumLevel = previous;
        }
    }

    [Fact]
    public void ApplyFromPreferences_InvalidSavedLevel_UsesSafeInformationDefault()
    {
        var previous = LogSwitchService.SharedSwitch.MinimumLevel;
        var store = CreateStore(new AppPreferencesV1
        {
            MinimumLogLevel = (AppLogLevel)999
        });

        try
        {
            var applied = AvaloniaLoggingPreferenceBootstrapper.ApplyFromPreferences(
                store.Object,
                commandLineOverride: null);

            Assert.True(applied);
            Assert.Equal(LogEventLevel.Information, LogSwitchService.SharedSwitch.MinimumLevel);
        }
        finally
        {
            LogSwitchService.SharedSwitch.MinimumLevel = previous;
        }
    }

    [Fact]
    public void ApplyFromPreferences_LoadFailure_PreservesActiveLogSwitch()
    {
        var previous = LogSwitchService.SharedSwitch.MinimumLevel;
        var store = new Mock<IAppPreferencesStore>();
        store.Setup(value => value.LoadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Fail<AppPreferencesV1?>("synthetic failure"));

        try
        {
            LogSwitchService.SharedSwitch.MinimumLevel = LogEventLevel.Error;

            var applied = AvaloniaLoggingPreferenceBootstrapper.ApplyFromPreferences(
                store.Object,
                commandLineOverride: null);

            Assert.False(applied);
            Assert.Equal(LogEventLevel.Error, LogSwitchService.SharedSwitch.MinimumLevel);
        }
        finally
        {
            LogSwitchService.SharedSwitch.MinimumLevel = previous;
        }
    }

    private static Mock<IAppPreferencesStore> CreateStore(AppPreferencesV1 preferences)
    {
        var store = new Mock<IAppPreferencesStore>();
        store.Setup(value => value.LoadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok<AppPreferencesV1?>(preferences));
        return store;
    }
}
