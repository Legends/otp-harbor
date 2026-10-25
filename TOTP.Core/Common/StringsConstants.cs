namespace TOTP.Core.Common;

public static class StringsConstants
{
    public const string AppSettingsFileName = "appsettings.json";
    public const string TokensStorageFilePathConfigKey = "Accounts:StorageFilePath";
    public const string AppDataDirectoryName = "TOTP-Manager";
    public const string TokensStorageFileName = "master.vault";
    public const string LegacyTokensStorageFileName = "master.totp";
    public const string AuthorizationEnvelopeFileName = "authorization-envelope.bin";
    public const string PreferencesFileName = "preferences.json";
    public const string AutoUpdateStateFileName = "autoupdate-state.json";
}
