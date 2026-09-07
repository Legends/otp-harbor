using TOTP.Core.Enums;

namespace TOTP.Core.Security.Interfaces;

public interface IAppSettings
{
    string CultureName { get; set; }
    AppLogLevel MinimumLogLevel { get; set; }
    PreferredUnlockMethod PreferredUnlockMethod { get; set; }
    bool AppLockEnabled { get; set; }
    TimeSpan IdleTimeout { get; set; }
    bool LockOnSessionLock { get; set; }
    bool LockOnMinimize { get; set; }
    bool ClearClipboardEnabled { get; set; }
    int ClearClipboardSeconds { get; set; }
    double QrPreviewScaleFactor { get; set; }
    int InterfaceScalePercent { get; set; }
    bool ExportEncrypt { get; set; }
    bool OpenExportFileAfterExport { get; set; }
    bool HideSecretsByDefault { get; set; }
}
