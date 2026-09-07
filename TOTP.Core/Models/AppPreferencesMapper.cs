using System.Globalization;
using TOTP.Core.Enums;
using TOTP.Core.Security.Interfaces;

namespace TOTP.Core.Models;

public static class AppPreferencesMapper
{
    public static AppPreferencesV1 FromSettings(IAppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return Normalize(new AppPreferencesV1
        {
            CultureName = settings.CultureName,
            MinimumLogLevel = settings.MinimumLogLevel,
            PreferredUnlockMethod = settings.PreferredUnlockMethod,
            AppLockEnabled = settings.AppLockEnabled,
            IdleTimeoutMinutes = ToIdleTimeoutMinutes(settings.IdleTimeout),
            LockOnSessionLock = settings.LockOnSessionLock,
            LockOnMinimize = settings.LockOnMinimize,
            ClearClipboardEnabled = settings.ClearClipboardEnabled,
            ClearClipboardSeconds = settings.ClearClipboardSeconds,
            QrPreviewScaleFactor = settings.QrPreviewScaleFactor,
            InterfaceScalePercent = settings.InterfaceScalePercent,
            ExportEncrypt = settings.ExportEncrypt,
            OpenExportFileAfterExport = settings.OpenExportFileAfterExport,
            HideSecretsByDefault = settings.HideSecretsByDefault
        });
    }

    public static void ApplyTo(AppPreferencesV1 preferences, IAppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        ArgumentNullException.ThrowIfNull(settings);
        var normalized = Normalize(preferences);

        settings.CultureName = normalized.CultureName;
        settings.MinimumLogLevel = normalized.MinimumLogLevel;
        settings.PreferredUnlockMethod = normalized.PreferredUnlockMethod;
        settings.AppLockEnabled = normalized.AppLockEnabled;
        settings.IdleTimeout = normalized.IdleTimeoutMinutes == 0
            ? TimeSpan.Zero
            : TimeSpan.FromMinutes(normalized.IdleTimeoutMinutes);
        settings.LockOnSessionLock = normalized.LockOnSessionLock;
        settings.LockOnMinimize = normalized.LockOnMinimize;
        settings.ClearClipboardEnabled = normalized.ClearClipboardEnabled;
        settings.ClearClipboardSeconds = normalized.ClearClipboardSeconds;
        settings.QrPreviewScaleFactor = normalized.QrPreviewScaleFactor;
        settings.InterfaceScalePercent = normalized.InterfaceScalePercent;
        settings.ExportEncrypt = normalized.ExportEncrypt;
        settings.OpenExportFileAfterExport = normalized.OpenExportFileAfterExport;
        settings.HideSecretsByDefault = normalized.HideSecretsByDefault;
    }

    private static AppPreferencesV1 Normalize(AppPreferencesV1 preferences) => preferences with
    {
        Format = AppPreferencesV1.FormatIdentifier,
        Version = AppPreferencesV1.CurrentVersion,
        CultureName = IsValidCulture(preferences.CultureName) ? preferences.CultureName : "en",
        MinimumLogLevel = Enum.IsDefined(preferences.MinimumLogLevel)
            ? preferences.MinimumLogLevel
            : AppLogLevel.Information,
        PreferredUnlockMethod = Enum.IsDefined(preferences.PreferredUnlockMethod)
            ? preferences.PreferredUnlockMethod
            : PreferredUnlockMethod.Password,
        IdleTimeoutMinutes = Math.Clamp(preferences.IdleTimeoutMinutes, 0, 1440),
        ClearClipboardSeconds = preferences.ClearClipboardSeconds > 0
            ? Math.Clamp(preferences.ClearClipboardSeconds, 1, 300)
            : AppSettings.DefaultClearClipboardSeconds,
        QrPreviewScaleFactor = NormalizeQrScale(preferences.QrPreviewScaleFactor),
        InterfaceScalePercent = NormalizeInterfaceScale(preferences.InterfaceScalePercent)
    };

    private static int ToIdleTimeoutMinutes(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero) return 0;
        return Math.Clamp((int)Math.Ceiling(timeout.TotalMinutes), 1, 1440);
    }

    private static double NormalizeQrScale(double scale)
    {
        if (!double.IsFinite(scale) || scale <= 0) scale = AppSettings.DefaultQrPreviewScaleFactor;
        var clamped = Math.Clamp(scale, 1.0, 6.0);
        return Math.Round(clamped * 2, MidpointRounding.AwayFromZero) / 2;
    }

    private static int NormalizeInterfaceScale(int percent)
    {
        return AppSettings.IsSupportedInterfaceScale(percent)
            ? percent
            : AppSettings.DefaultInterfaceScalePercent;
    }

    private static bool IsValidCulture(string? cultureName)
    {
        if (string.IsNullOrWhiteSpace(cultureName) || cultureName.Length > 32) return false;
        try
        {
            _ = CultureInfo.GetCultureInfo(cultureName);
            return true;
        }
        catch (CultureNotFoundException)
        {
            return false;
        }
    }
}
