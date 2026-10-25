using TOTP.Platform.MacOS;

namespace TOTP.Tests.Platform.MacOS;

public sealed class MacOSApplicationPathsTests
{
    [Fact]
    public void Constructor_UsesApplicationSupportAndUserLogs()
    {
        var executableDirectory = Path.GetFullPath(Path.Combine("test", "macos", "application"));
        var homeDirectory = Path.GetFullPath(Path.Combine("test", "macos", "home"));

        var sut = new MacOSApplicationPaths(executableDirectory, homeDirectory);

        var dataDirectory = Path.Combine(homeDirectory, "Library", "Application Support", "TOTP-Manager");
        Assert.Equal(Path.Combine(executableDirectory, "appsettings.json"), sut.ConfigurationFilePath);
        Assert.Equal(dataDirectory, sut.ApplicationDataDirectory);
        Assert.Equal(Path.Combine(dataDirectory, "master.vault"), sut.VaultFilePath);
        Assert.Equal(Path.Combine(dataDirectory, "authorization-envelope.bin"), sut.AuthorizationEnvelopeFilePath);
        Assert.Equal(Path.Combine(dataDirectory, "preferences.json"), sut.PreferencesFilePath);
        Assert.Equal(Path.Combine(dataDirectory, "Backups"), sut.BackupDirectory);
        Assert.Equal(Path.Combine(homeDirectory, "Library", "Logs", "TOTP-Manager"), sut.LogDirectory);
        Assert.Equal(Path.Combine(sut.LogDirectory, "app.log"), sut.LogFilePath);
        Assert.Equal(Path.Combine(dataDirectory, "autoupdate-state.json"), sut.UpdateStateFilePath);
    }

    [Theory]
    [InlineData("", "home")]
    [InlineData("application", "")]
    [InlineData("application", "relative-home")]
    public void Constructor_WhenRequiredRootIsUnavailableOrRelative_FailsClosed(
        string executableDirectory,
        string homeDirectory)
    {
        var act = () => new MacOSApplicationPaths(executableDirectory, homeDirectory);

        Assert.Throws<InvalidOperationException>(act);
    }
}
