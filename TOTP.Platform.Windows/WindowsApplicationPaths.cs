using System.Runtime.InteropServices;
using System.Text;
using TOTP.Core.Common;
using TOTP.Core.Services.Interfaces;

namespace TOTP.Platform.Windows;

public sealed class WindowsApplicationPaths : IPlatformApplicationPaths
{
    public const string InstalledDistributionMarkerFileName = "otp-harbor.installed";

    public WindowsApplicationPaths(
        string? executableDirectory = null,
        string? roamingApplicationDataDirectory = null,
        bool? hasPackageIdentity = null,
        bool? hasInstalledDistributionMarker = null)
    {
        ExecutableDirectory = Path.GetFullPath(executableDirectory ?? ResolveExecutableDirectory());

        var roamingRoot = roamingApplicationDataDirectory
            ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        if (string.IsNullOrWhiteSpace(roamingRoot))
        {
            throw new InvalidOperationException("The Windows roaming application-data directory is unavailable.");
        }

        ApplicationDataDirectory = Path.Combine(
            Path.GetFullPath(roamingRoot),
            StringsConstants.AppDataDirectoryName);

        ConfigurationFilePath = Path.Combine(ExecutableDirectory, StringsConstants.AppSettingsFileName);
        VaultFilePath = Path.Combine(ApplicationDataDirectory, StringsConstants.TokensStorageFileName);
        AuthorizationEnvelopeFilePath = Path.Combine(ApplicationDataDirectory, StringsConstants.AuthorizationEnvelopeFileName);
        PreferencesFilePath = Path.Combine(ApplicationDataDirectory, StringsConstants.PreferencesFileName);
        BackupDirectory = ApplicationDataDirectory;
        // Portable copies and older installers can also reside in protected folders.
        LogDirectory = Path.Combine(ApplicationDataDirectory, "Logs");
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

    private static string ResolveExecutableDirectory() =>
        Path.GetDirectoryName(Environment.ProcessPath)
        ?? AppDomain.CurrentDomain.BaseDirectory;
}

internal static class WindowsPackageIdentity
{
    private const int ErrorInsufficientBuffer = 122;
    private const int AppModelErrorNoPackage = 15700;

    public static bool HasCurrent()
    {
        var length = 0;
        var result = GetCurrentPackageFullName(ref length, null);
        return result switch
        {
            ErrorInsufficientBuffer => true,
            AppModelErrorNoPackage => false,
            _ => false
        };
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(
        ref int packageFullNameLength,
        StringBuilder? packageFullName);
}
