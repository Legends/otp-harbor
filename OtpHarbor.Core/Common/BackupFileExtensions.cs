namespace TOTP.Core.Common;

public static class BackupFileExtensions
{
    public const string EncryptedBackup = ".otphbp";
    public const string LegacyEncryptedBackup = ".totp";
    public const string AutomaticBackup = ".otphbackup";

    public static bool IsEncryptedBackup(string? extension) =>
        extension?.Equals(EncryptedBackup, StringComparison.OrdinalIgnoreCase) == true
        || extension?.Equals(LegacyEncryptedBackup, StringComparison.OrdinalIgnoreCase) == true;

    public static bool IsSupportedBackup(string? extension) =>
        IsEncryptedBackup(extension)
        || extension?.Equals(AutomaticBackup, StringComparison.OrdinalIgnoreCase) == true;
}
