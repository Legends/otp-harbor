using TOTP.Core.Common;
using TOTP.Platform.Windows;

namespace TOTP.Tests.Platform.Windows;

public sealed class WindowsApplicationPathsTests
{
    [Fact]
    public void Constructor_PreservesLegacyWindowsLocations()
    {
        var executableDirectory = Path.GetFullPath(Path.Combine("test", "application"));
        var roamingDirectory = Path.GetFullPath(Path.Combine("test", "roaming"));

        var sut = new WindowsApplicationPaths(
            executableDirectory,
            roamingDirectory,
            hasPackageIdentity: false,
            hasInstalledDistributionMarker: false);

        var appDataDirectory = Path.Combine(roamingDirectory, StringsConstants.AppDataDirectoryName);
        Assert.Equal(executableDirectory, sut.ExecutableDirectory);
        Assert.Equal(Path.Combine(executableDirectory, "appsettings.json"), sut.ConfigurationFilePath);
        Assert.Equal(appDataDirectory, sut.ApplicationDataDirectory);
        Assert.Equal(Path.Combine(appDataDirectory, "master.totp"), sut.VaultFilePath);
        Assert.Equal(Path.Combine(appDataDirectory, "authorization-envelope.bin"), sut.AuthorizationEnvelopeFilePath);
        Assert.Equal(Path.Combine(appDataDirectory, "preferences.json"), sut.PreferencesFilePath);
        Assert.Equal(appDataDirectory, sut.BackupDirectory);
        Assert.Equal(Path.Combine(executableDirectory, "Logs"), sut.LogDirectory);
        Assert.Equal(Path.Combine(executableDirectory, "Logs", "app.log"), sut.LogFilePath);
        Assert.Equal(Path.Combine(appDataDirectory, "autoupdate-state.json"), sut.UpdateStateFilePath);
    }

    [Fact]
    public void Constructor_WithPackageIdentity_StoresLogsInWritableApplicationData()
    {
        var executableDirectory = Path.GetFullPath(Path.Combine("test", "application"));
        var roamingDirectory = Path.GetFullPath(Path.Combine("test", "roaming"));

        var sut = new WindowsApplicationPaths(
            executableDirectory,
            roamingDirectory,
            hasPackageIdentity: true);

        var expectedLogDirectory = Path.Combine(
            roamingDirectory,
            StringsConstants.AppDataDirectoryName,
            "Logs");
        Assert.Equal(expectedLogDirectory, sut.LogDirectory);
        Assert.Equal(Path.Combine(expectedLogDirectory, "app.log"), sut.LogFilePath);
    }

    [Fact]
    public void Constructor_WithInstalledDistributionMarker_StoresLogsInWritableApplicationData()
    {
        var executableDirectory = Path.GetFullPath(Path.Combine("test", "application"));
        var roamingDirectory = Path.GetFullPath(Path.Combine("test", "roaming"));

        var sut = new WindowsApplicationPaths(
            executableDirectory,
            roamingDirectory,
            hasPackageIdentity: false,
            hasInstalledDistributionMarker: true);

        var expectedLogDirectory = Path.Combine(
            roamingDirectory,
            StringsConstants.AppDataDirectoryName,
            "Logs");
        Assert.Equal(expectedLogDirectory, sut.LogDirectory);
        Assert.Equal(Path.Combine(expectedLogDirectory, "app.log"), sut.LogFilePath);
    }

    [Fact]
    public void Constructor_WhenRoamingApplicationDataIsUnavailable_FailsClosed()
    {
        var act = () => new WindowsApplicationPaths(AppContext.BaseDirectory, string.Empty);

        var exception = Assert.Throws<InvalidOperationException>(act);
        Assert.Contains("application-data directory is unavailable", exception.Message);
    }

    [Fact]
    public void DefaultConstructor_MatchesTheExistingWindowsDataDirectory()
    {
        var sut = new WindowsApplicationPaths();

        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TOTP-Manager");

        Assert.Equal(expected, sut.ApplicationDataDirectory);
    }
}
