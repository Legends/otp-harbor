using TOTP.Platform.Linux;

namespace TOTP.Tests.Platform.Linux;

public sealed class LinuxApplicationPathsTests
{
    [Fact]
    public void Constructor_UsesAbsoluteXdgRoots()
    {
        var executableDirectory = Absolute("application");
        var homeDirectory = Absolute("home");
        var configurationRoot = Absolute("xdg-config");
        var dataRoot = Absolute("xdg-data");
        var stateRoot = Absolute("xdg-state");

        var sut = new LinuxApplicationPaths(
            executableDirectory,
            homeDirectory,
            configurationRoot,
            dataRoot,
            stateRoot);

        var dataDirectory = Path.Combine(dataRoot, "totp-manager");
        var stateDirectory = Path.Combine(stateRoot, "totp-manager");
        Assert.Equal(Path.Combine(executableDirectory, "appsettings.json"), sut.ConfigurationFilePath);
        Assert.Equal(dataDirectory, sut.ApplicationDataDirectory);
        Assert.Equal(Path.Combine(dataDirectory, "master.vault"), sut.VaultFilePath);
        Assert.Equal(Path.Combine(dataDirectory, "authorization-envelope.bin"), sut.AuthorizationEnvelopeFilePath);
        Assert.Equal(Path.Combine(configurationRoot, "totp-manager", "preferences.json"), sut.PreferencesFilePath);
        Assert.Equal(Path.Combine(dataDirectory, "backups"), sut.BackupDirectory);
        Assert.Equal(Path.Combine(stateDirectory, "logs"), sut.LogDirectory);
        Assert.Equal(Path.Combine(sut.LogDirectory, "app.log"), sut.LogFilePath);
        Assert.Equal(Path.Combine(stateDirectory, "autoupdate-state.json"), sut.UpdateStateFilePath);
    }

    [Fact]
    public void Constructor_WhenXdgRootsAreAbsent_UsesHomeDirectoryFallbacks()
    {
        var executableDirectory = Absolute("application");
        var homeDirectory = Absolute("home");

        var sut = new LinuxApplicationPaths(
            executableDirectory,
            homeDirectory,
            null,
            string.Empty,
            null);

        Assert.Equal(
            Path.Combine(homeDirectory, ".local", "share", "totp-manager"),
            sut.ApplicationDataDirectory);
        Assert.Equal(
            Path.Combine(homeDirectory, ".config", "totp-manager", "preferences.json"),
            sut.PreferencesFilePath);
        Assert.Equal(
            Path.Combine(homeDirectory, ".local", "state", "totp-manager", "logs"),
            sut.LogDirectory);
    }

    [Theory]
    [InlineData("relative", null, null)]
    [InlineData(null, "relative", null)]
    [InlineData(null, null, "relative")]
    public void Constructor_WhenConfiguredXdgRootIsRelative_FailsClosed(
        string? configurationRoot,
        string? dataRoot,
        string? stateRoot)
    {
        var act = () => new LinuxApplicationPaths(
            Absolute("application"),
            Absolute("home"),
            configurationRoot,
            dataRoot,
            stateRoot);

        Assert.Throws<InvalidOperationException>(act);
    }

    private static string Absolute(string leaf) =>
        Path.GetFullPath(Path.Combine("test", "linux", leaf));
}
