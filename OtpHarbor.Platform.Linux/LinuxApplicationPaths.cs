using TOTP.Core.Common;
using TOTP.Core.Services.Interfaces;

namespace TOTP.Platform.Linux;

public sealed class LinuxApplicationPaths : IPlatformApplicationPaths
{
    private const string ApplicationDirectoryName = "totp-manager";

    public LinuxApplicationPaths()
        : this(
            ResolveExecutableDirectory(),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetEnvironmentVariable("XDG_CONFIG_HOME"),
            Environment.GetEnvironmentVariable("XDG_DATA_HOME"),
            Environment.GetEnvironmentVariable("XDG_STATE_HOME"))
    {
    }

    public LinuxApplicationPaths(
        string executableDirectory,
        string homeDirectory,
        string? xdgConfigHome,
        string? xdgDataHome,
        string? xdgStateHome)
    {
        ExecutableDirectory = NormalizeRequiredRoot(executableDirectory, "executable directory");
        var home = NormalizeRequiredRoot(homeDirectory, "user home directory");
        var configurationRoot = ResolveXdgRoot(xdgConfigHome, Path.Combine(home, ".config"), "XDG_CONFIG_HOME");
        var dataRoot = ResolveXdgRoot(xdgDataHome, Path.Combine(home, ".local", "share"), "XDG_DATA_HOME");
        var stateRoot = ResolveXdgRoot(xdgStateHome, Path.Combine(home, ".local", "state"), "XDG_STATE_HOME");

        var configurationDirectory = Path.Combine(configurationRoot, ApplicationDirectoryName);
        ApplicationDataDirectory = Path.Combine(dataRoot, ApplicationDirectoryName);
        var stateDirectory = Path.Combine(stateRoot, ApplicationDirectoryName);

        ConfigurationFilePath = Path.Combine(ExecutableDirectory, StringsConstants.AppSettingsFileName);
        VaultFilePath = Path.Combine(ApplicationDataDirectory, StringsConstants.TokensStorageFileName);
        AuthorizationEnvelopeFilePath = Path.Combine(ApplicationDataDirectory, StringsConstants.AuthorizationEnvelopeFileName);
        PreferencesFilePath = Path.Combine(configurationDirectory, StringsConstants.PreferencesFileName);
        BackupDirectory = Path.Combine(ApplicationDataDirectory, "backups");
        LogDirectory = Path.Combine(stateDirectory, "logs");
        LogFilePath = Path.Combine(LogDirectory, "app.log");
        UpdateStateFilePath = Path.Combine(stateDirectory, StringsConstants.AutoUpdateStateFileName);
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

    private static string ResolveXdgRoot(string? configuredPath, string fallback, string variableName)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
            return fallback;

        if (!Path.IsPathFullyQualified(configuredPath))
            throw new InvalidOperationException($"{variableName} must be an absolute path when set.");

        return Path.GetFullPath(configuredPath);
    }

    private static string NormalizeRequiredRoot(string path, string description)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            throw new InvalidOperationException($"The Linux {description} must be an absolute path.");

        return Path.GetFullPath(path);
    }

    private static string ResolveExecutableDirectory() =>
        Path.GetDirectoryName(Environment.ProcessPath)
        ?? AppDomain.CurrentDomain.BaseDirectory;
}
