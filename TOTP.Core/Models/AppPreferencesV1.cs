using System.Text.Json.Serialization;
using TOTP.Core.Enums;

namespace TOTP.Core.Models;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AppPreferencesV1
{
    public const string FormatIdentifier = "totp-preferences";
    public const int CurrentVersion = 1;

    [JsonRequired]
    [JsonPropertyName("format")]
    public string Format { get; init; } = FormatIdentifier;

    [JsonRequired]
    [JsonPropertyName("version")]
    public int Version { get; init; } = CurrentVersion;

    [JsonPropertyName("culture")]
    public string CultureName { get; init; } = "en";

    [JsonPropertyName("minimumLogLevel")]
    public AppLogLevel MinimumLogLevel { get; init; } = AppLogLevel.Information;

    [JsonPropertyName("preferredUnlockMethod")]
    public PreferredUnlockMethod PreferredUnlockMethod { get; init; } = PreferredUnlockMethod.Password;

    [JsonPropertyName("appLockEnabled")]
    public bool AppLockEnabled { get; init; } = true;

    [JsonPropertyName("idleTimeoutMinutes")]
    public int IdleTimeoutMinutes { get; init; } = 10;

    [JsonPropertyName("lockOnSessionLock")]
    public bool LockOnSessionLock { get; init; } = true;

    [JsonPropertyName("lockOnMinimize")]
    public bool LockOnMinimize { get; init; } = true;

    [JsonPropertyName("clearClipboardEnabled")]
    public bool ClearClipboardEnabled { get; init; } = true;

    [JsonPropertyName("clearClipboardSeconds")]
    public int ClearClipboardSeconds { get; init; } = 15;

    [JsonPropertyName("qrPreviewScaleFactor")]
    public double QrPreviewScaleFactor { get; init; } = 1.5;

    [JsonPropertyName("interfaceScalePercent")]
    public int InterfaceScalePercent { get; init; }

    [JsonPropertyName("exportEncrypt")]
    // Retained in the strict version-1 wire contract for existing preference files.
    public bool ExportEncrypt { get; init; } = true;

    [JsonPropertyName("openExportFileAfterExport")]
    public bool OpenExportFileAfterExport { get; init; } = true;

    [JsonPropertyName("hideSecretsByDefault")]
    // Retained in the strict version-1 wire contract for existing preference files.
    public bool HideSecretsByDefault { get; init; } = true;
}
