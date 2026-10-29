namespace TOTP.Core.Services.Interfaces;

/// <summary>
/// Provides the filesystem locations selected by the current operating-system adapter.
/// </summary>
public interface IPlatformApplicationPaths
{
    string ExecutableDirectory { get; }
    string ConfigurationFilePath { get; }
    string ApplicationDataDirectory { get; }
    string VaultFilePath { get; }
    string AuthorizationEnvelopeFilePath { get; }
    string PreferencesFilePath { get; }
    string BackupDirectory { get; }
    string LogDirectory { get; }
    string LogFilePath { get; }
    string UpdateStateFilePath { get; }
}
