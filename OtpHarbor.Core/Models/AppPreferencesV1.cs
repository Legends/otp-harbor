using System.Text.Json.Serialization;
using TOTP.Core.Enums;

namespace TOTP.Core.Models;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AppPreferencesV1
{
    public const string FormatIdentifier = "totp-preferences";
    public const int CurrentVersion = 1;

    public AppPreferencesV1() : this(cultureName: "en") { }

    // Constructor defaults preserve omitted v1 fields with .NET 10 JSON source generation.
    [JsonConstructor]
    public AppPreferencesV1(
        string cultureName = "en",
        AppLogLevel minimumLogLevel = AppLogLevel.Information,
        PreferredUnlockMethod preferredUnlockMethod = PreferredUnlockMethod.Password,
        bool appLockEnabled = true,
        int idleTimeoutMinutes = 10,
        bool lockOnSessionLock = true,
        bool lockOnMinimize = true,
        bool clearClipboardEnabled = true,
        int clearClipboardSeconds = 15,
        double qrPreviewScaleFactor = 1.5,
        bool exportEncrypt = true,
        bool openExportFileAfterExport = true,
        bool hideSecretsByDefault = true,
        string favoriteGroupColor = AppSettings.DefaultFavoriteGroupColor,
        bool allowScreenshots = true)
    {
        CultureName = cultureName;
        MinimumLogLevel = minimumLogLevel;
        PreferredUnlockMethod = preferredUnlockMethod;
        AppLockEnabled = appLockEnabled;
        IdleTimeoutMinutes = idleTimeoutMinutes;
        LockOnSessionLock = lockOnSessionLock;
        LockOnMinimize = lockOnMinimize;
        ClearClipboardEnabled = clearClipboardEnabled;
        ClearClipboardSeconds = clearClipboardSeconds;
        QrPreviewScaleFactor = qrPreviewScaleFactor;
        ExportEncrypt = exportEncrypt;
        OpenExportFileAfterExport = openExportFileAfterExport;
        HideSecretsByDefault = hideSecretsByDefault;
        FavoriteGroupColor = favoriteGroupColor;
        AllowScreenshots = allowScreenshots;
    }

    [JsonRequired]
    [JsonPropertyName("format")]
    public string Format { get; init; } = FormatIdentifier;

    [JsonRequired]
    [JsonPropertyName("version")]
    public int Version { get; init; } = CurrentVersion;

    [JsonPropertyName("culture")]
    public string CultureName { get; init; }

    [JsonPropertyName("minimumLogLevel")]
    public AppLogLevel MinimumLogLevel { get; init; }

    [JsonPropertyName("preferredUnlockMethod")]
    public PreferredUnlockMethod PreferredUnlockMethod { get; init; }

    [JsonPropertyName("appLockEnabled")]
    public bool AppLockEnabled { get; init; }

    [JsonPropertyName("idleTimeoutMinutes")]
    public int IdleTimeoutMinutes { get; init; }

    // Present only when a sub-minute precision value cannot be represented by
    // the original v1 minutes field. Older files continue to use minutes.
    [JsonPropertyName("idleTimeoutSeconds")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? IdleTimeoutSeconds { get; init; }

    [JsonPropertyName("lockOnSessionLock")]
    public bool LockOnSessionLock { get; init; }

    [JsonPropertyName("lockOnMinimize")]
    public bool LockOnMinimize { get; init; }

    [JsonPropertyName("clearClipboardEnabled")]
    public bool ClearClipboardEnabled { get; init; }

    [JsonPropertyName("clearClipboardSeconds")]
    public int ClearClipboardSeconds { get; init; }

    [JsonPropertyName("qrPreviewScaleFactor")]
    public double QrPreviewScaleFactor { get; init; }

    [JsonPropertyName("interfaceScalePercent")]
    public int InterfaceScalePercent { get; init; }

    [JsonPropertyName("exportEncrypt")]
    // Retained in the strict version-1 wire contract for existing preference files.
    public bool ExportEncrypt { get; init; }

    [JsonPropertyName("openExportFileAfterExport")]
    public bool OpenExportFileAfterExport { get; init; }

    [JsonPropertyName("hideSecretsByDefault")]
    // Retained in the strict version-1 wire contract for existing preference files.
    public bool HideSecretsByDefault { get; init; }

    [JsonPropertyName("favoriteGroupColor")]
    public string FavoriteGroupColor { get; init; }

    [JsonPropertyName("showNextToken")]
    public bool ShowNextToken { get; init; }

    [JsonPropertyName("allowScreenshots")]
    public bool AllowScreenshots { get; init; }

    // Accepted only to migrate development builds that briefly wrote this
    // branding-only value into the strict v1 preferences contract. New writes
    // omit it and persist the value under BrandIcons instead.
    [JsonPropertyName("showIssuerLogo")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? LegacyShowIssuerLogo { get; init; }
}
