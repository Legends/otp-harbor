using TOTP.Core.Enums;
using TOTP.Core.Security.Interfaces;

namespace TOTP.Core.Models;

public sealed class AppSettings : IAppSettings
{
    public static readonly TimeSpan DefaultIdleTimeout = TimeSpan.FromMinutes(10);
    public const int DefaultClearClipboardSeconds = 15;
    public const double DefaultQrPreviewScaleFactor = 1.5;
    public const int DefaultInterfaceScalePercent = 0;

    public string CultureName { get; set; } = "en";

    public AppLogLevel MinimumLogLevel { get; set; } = AppLogLevel.Information;

    public PreferredUnlockMethod PreferredUnlockMethod { get; set; } = PreferredUnlockMethod.Password;

    public bool AppLockEnabled { get; set; } = true;

    public TimeSpan IdleTimeout { get; set; } = DefaultIdleTimeout;

    public bool LockOnSessionLock { get; set; } = true;

    public bool LockOnMinimize { get; set; } = true;

    public bool ClearClipboardEnabled { get; set; } = true;

    public int ClearClipboardSeconds { get; set; } = DefaultClearClipboardSeconds;

    public double QrPreviewScaleFactor { get; set; } = DefaultQrPreviewScaleFactor;

    public int InterfaceScalePercent { get; set; } = DefaultInterfaceScalePercent;

    public bool ExportEncrypt { get; set; } = true;

    public bool OpenExportFileAfterExport { get; set; } = true;

    public bool HideSecretsByDefault { get; set; } = true;

    public static bool IsSupportedInterfaceScale(int percent) =>
        percent == DefaultInterfaceScalePercent
        || percent is >= 100 and <= 300 && percent % 25 == 0;
}
