using Android.Content;
using TOTP.Core.Common;
using TOTP.Core.Services.Interfaces;

namespace TOTP.Platform.Android;

public sealed class AndroidApplicationPaths : IPlatformApplicationPaths
{
    public AndroidApplicationPaths(Context context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var filesDirectory = context.FilesDir?.CanonicalPath;
        var cacheDirectory = context.CacheDir?.CanonicalPath;
        if (string.IsNullOrWhiteSpace(filesDirectory)
            || string.IsNullOrWhiteSpace(cacheDirectory))
        {
            throw new InvalidOperationException("Android application storage is unavailable.");
        }

        ExecutableDirectory = AppContext.BaseDirectory;
        ApplicationDataDirectory = Path.Combine(
            filesDirectory,
            StringsConstants.AppDataDirectoryName);
        ConfigurationFilePath = Path.Combine(
            ApplicationDataDirectory,
            StringsConstants.AppSettingsFileName);
        VaultFilePath = Path.Combine(
            ApplicationDataDirectory,
            StringsConstants.TokensStorageFileName);
        AuthorizationEnvelopeFilePath = Path.Combine(
            ApplicationDataDirectory,
            StringsConstants.AuthorizationEnvelopeFileName);
        PreferencesFilePath = Path.Combine(
            ApplicationDataDirectory,
            StringsConstants.PreferencesFileName);
        BackupDirectory = Path.Combine(ApplicationDataDirectory, "Backups");
        LogDirectory = Path.Combine(cacheDirectory, "Logs");
        LogFilePath = Path.Combine(LogDirectory, "app.log");
        UpdateStateFilePath = Path.Combine(
            ApplicationDataDirectory,
            StringsConstants.AutoUpdateStateFileName);
    }

    public string ExecutableDirectory { get; }
    public string ConfigurationFilePath { get; }
    public string ApplicationDataDirectory { get; }
    public string VaultFilePath { get; }
    public string AuthorizationEnvelopeFilePath { get; }
    public string PreferencesFilePath { get; }
    public string BackupDirectory { get; }
    public string LogDirectory { get; }
    public string LogFilePath { get; }
    public string UpdateStateFilePath { get; }
}
