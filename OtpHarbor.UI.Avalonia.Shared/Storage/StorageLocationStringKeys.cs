using TOTP.Core.Services.Interfaces;

namespace TOTP.Avalonia.Shared.Storage;

public static class StorageLocationStringKeys
{
    public const string CopyPath = "StorageLocationsCopyPath";
    public const string OpenFolder = "StorageLocationsOpenFolder";
    public const string CopySucceeded = "StorageLocationsCopySucceeded";
    public const string CopyFailed = "StorageLocationsCopyFailed";
    public const string FolderOpened = "StorageLocationsFolderOpened";
    public const string FolderOpenFailed = "StorageLocationsFolderOpenFailed";
    public const string CopyLabelFormat = "StorageLocationsCopyLabelFormat";
    public const string OpenLabelFormat = "StorageLocationsOpenLabelFormat";
    public const string Info = "StorageLocationsInfo";
    public const string InfoDescription = "StorageLocationsInfoDescription";

    public static string ForKind(ApplicationStorageLocationKind kind) => $"StorageLocation{kind}";

    public static IReadOnlyList<string> All { get; } =
    [
        CopyPath, OpenFolder, CopySucceeded, CopyFailed, FolderOpened, FolderOpenFailed,
        CopyLabelFormat, OpenLabelFormat, Info, InfoDescription,
        .. Enum.GetValues<ApplicationStorageLocationKind>().Select(ForKind)
    ];
}
