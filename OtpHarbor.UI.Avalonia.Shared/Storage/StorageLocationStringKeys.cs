using TOTP.Core.Services.Interfaces;

namespace TOTP.Avalonia.Shared.Storage;

public static class StorageLocationStringKeys
{
    public const string CopyPath = global::TOTP.Localization.CommonStringKeys.StorageLocationsCopyPath;
    public const string OpenFolder = global::TOTP.Localization.CommonStringKeys.StorageLocationsOpenFolder;
    public const string CopySucceeded = global::TOTP.Localization.CommonStringKeys.StorageLocationsCopySucceeded;
    public const string CopyFailed = global::TOTP.Localization.CommonStringKeys.StorageLocationsCopyFailed;
    public const string FolderOpened = global::TOTP.Localization.CommonStringKeys.StorageLocationsFolderOpened;
    public const string FolderOpenFailed = global::TOTP.Localization.CommonStringKeys.StorageLocationsFolderOpenFailed;
    public const string CopyLabelFormat = global::TOTP.Localization.CommonStringKeys.StorageLocationsCopyLabelFormat;
    public const string OpenLabelFormat = global::TOTP.Localization.CommonStringKeys.StorageLocationsOpenLabelFormat;
    public const string Info = global::TOTP.Localization.CommonStringKeys.StorageLocationsInfo;
    public const string InfoDescription = global::TOTP.Localization.CommonStringKeys.StorageLocationsInfoDescription;

    public static string ForKind(ApplicationStorageLocationKind kind) => $"StorageLocation{kind}";

    public static IReadOnlyList<string> All { get; } =
    [
        CopyPath, OpenFolder, CopySucceeded, CopyFailed, FolderOpened, FolderOpenFailed,
        CopyLabelFormat, OpenLabelFormat, Info, InfoDescription,
        .. Enum.GetValues<ApplicationStorageLocationKind>().Select(ForKind)
    ];
}
