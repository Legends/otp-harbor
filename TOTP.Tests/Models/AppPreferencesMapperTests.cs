using TOTP.Core.Enums;
using TOTP.Core.Models;

namespace TOTP.Tests.Models;

public sealed class AppPreferencesMapperTests
{
    [Fact]
    public void FromSettings_MapsEveryReviewedPreferenceAndNoAuthorization()
    {
        var settings = CreateSettings();

        var preferences = AppPreferencesMapper.FromSettings(settings);

        Assert.Equal("de-DE", preferences.CultureName);
        Assert.Equal(AppLogLevel.Warning, preferences.MinimumLogLevel);
        Assert.Equal(PreferredUnlockMethod.PlatformQuickUnlock, preferences.PreferredUnlockMethod);
        Assert.False(preferences.AppLockEnabled);
        Assert.Equal(7, preferences.IdleTimeoutMinutes);
        Assert.False(preferences.LockOnSessionLock);
        Assert.False(preferences.LockOnMinimize);
        Assert.False(preferences.ClearClipboardEnabled);
        Assert.Equal(12, preferences.ClearClipboardSeconds);
        Assert.Equal(2.5, preferences.QrPreviewScaleFactor);
        Assert.Equal(175, preferences.InterfaceScalePercent);
        Assert.False(preferences.ExportEncrypt);
        Assert.False(preferences.OpenExportFileAfterExport);
        Assert.False(preferences.HideSecretsByDefault);
        Assert.DoesNotContain(
            typeof(AppPreferencesV1).GetProperties(),
            property => property.Name.Contains("Authorization", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ApplyTo_MapsEveryReviewedPreference()
    {
        var settings = new AppSettings();
        var preferences = AppPreferencesV1CodecTests.CreatePreferences();

        AppPreferencesMapper.ApplyTo(preferences, settings);

        Assert.Equal("de-DE", settings.CultureName);
        Assert.Equal(AppLogLevel.Warning, settings.MinimumLogLevel);
        Assert.Equal(PreferredUnlockMethod.PlatformQuickUnlock, settings.PreferredUnlockMethod);
        Assert.False(settings.AppLockEnabled);
        Assert.Equal(TimeSpan.FromMinutes(7), settings.IdleTimeout);
        Assert.False(settings.LockOnSessionLock);
        Assert.False(settings.LockOnMinimize);
        Assert.True(settings.ClearClipboardEnabled);
        Assert.Equal(12, settings.ClearClipboardSeconds);
        Assert.Equal(2.5, settings.QrPreviewScaleFactor);
        Assert.Equal(175, settings.InterfaceScalePercent);
        Assert.False(settings.ExportEncrypt);
        Assert.False(settings.OpenExportFileAfterExport);
        Assert.False(settings.HideSecretsByDefault);
    }

    [Fact]
    public void FromSettings_LegacyOutOfRangeValues_NormalizesToValidPortableValues()
    {
        var settings = new AppSettings
        {
            CultureName = "not a culture!",
            MinimumLogLevel = (AppLogLevel)999,
            PreferredUnlockMethod = (PreferredUnlockMethod)999,
            IdleTimeout = TimeSpan.FromSeconds(30),
            ClearClipboardSeconds = 0,
            QrPreviewScaleFactor = double.NaN,
            InterfaceScalePercent = 110
        };

        var preferences = AppPreferencesMapper.FromSettings(settings);
        var encoded = AppPreferencesV1Codec.Serialize(preferences);

        Assert.Equal("en", preferences.CultureName);
        Assert.Equal(AppLogLevel.Information, preferences.MinimumLogLevel);
        Assert.Equal(PreferredUnlockMethod.Password, preferences.PreferredUnlockMethod);
        Assert.Equal(1, preferences.IdleTimeoutMinutes);
        Assert.Equal(AppSettings.DefaultClearClipboardSeconds, preferences.ClearClipboardSeconds);
        Assert.Equal(AppSettings.DefaultQrPreviewScaleFactor, preferences.QrPreviewScaleFactor);
        Assert.Equal(AppSettings.DefaultInterfaceScalePercent, preferences.InterfaceScalePercent);
        Assert.True(encoded.IsSuccess);
    }

    [Fact]
    public void RoundTrip_PreservesDisabledIdleTimeout()
    {
        var settings = new AppSettings { IdleTimeout = TimeSpan.Zero };

        var preferences = AppPreferencesMapper.FromSettings(settings);
        var target = new AppSettings();
        AppPreferencesMapper.ApplyTo(preferences, target);

        Assert.Equal(0, preferences.IdleTimeoutMinutes);
        Assert.Equal(TimeSpan.Zero, target.IdleTimeout);
    }

    private static AppSettings CreateSettings() => new()
    {
        CultureName = "de-DE",
        MinimumLogLevel = AppLogLevel.Warning,
        PreferredUnlockMethod = PreferredUnlockMethod.PlatformQuickUnlock,
        AppLockEnabled = false,
        IdleTimeout = TimeSpan.FromMinutes(7),
        LockOnSessionLock = false,
        LockOnMinimize = false,
        ClearClipboardEnabled = false,
        ClearClipboardSeconds = 12,
        QrPreviewScaleFactor = 2.5,
        InterfaceScalePercent = 175,
        ExportEncrypt = false,
        OpenExportFileAfterExport = false,
        HideSecretsByDefault = false
    };
}
