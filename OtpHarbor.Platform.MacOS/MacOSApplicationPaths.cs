using TOTP.Core.Common;
using TOTP.Core.Services.Interfaces;

namespace TOTP.Platform.MacOS;

public sealed class MacOSApplicationPaths : IPlatformApplicationPaths
{
    public MacOSApplicationPaths()
        : this(ResolveExecutableDirectory(), Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))
    {
    }

    public MacOSApplicationPaths(string executableDirectory, string homeDirectory)
    {
        ExecutableDirectory = NormalizeRequiredRoot(executableDirectory, "executable directory");
        var home = NormalizeRequiredRoot(homeDirectory, "user home directory");

        ApplicationDataDirectory = Path.Combine(
            home,
            "Library",
            "Application Support",
            StringsConstants.AppDataDirectoryName);
        ConfigurationFilePath = Path.Combine(ExecutableDirectory, StringsConstants.AppSettingsFileName);
        VaultFilePath = Path.Combine(ApplicationDataDirectory, StringsConstants.TokensStorageFileName);
        AuthorizationEnvelopeFilePath = Path.Combine(ApplicationDataDirectory, StringsConstants.AuthorizationEnvelopeFileName);
        PreferencesFilePath = Path.Combine(ApplicationDataDirectory, StringsConstants.PreferencesFileName);
        BackupDirectory = Path.Combine(ApplicationDataDirectory, "Backups");
        LogDirectory = Path.Combine(home, "Library", "Logs", StringsConstants.AppDataDirectoryName);
        LogFilePath = Path.Combine(LogDirectory, "app.log");
        UpdateStateFilePath = Path.Combine(ApplicationDataDirectory, StringsConstants.AutoUpdateStateFileName);
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

    private static string NormalizeRequiredRoot(string path, string description)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            throw new InvalidOperationException($"The macOS {description} must be an absolute path.");

        return Path.GetFullPath(path);
    }

    private static string ResolveExecutableDirectory() =>
        Path.GetDirectoryName(Environment.ProcessPath)
        ?? AppDomain.CurrentDomain.BaseDirectory;
}
