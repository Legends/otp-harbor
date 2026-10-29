using Moq;
using TOTP.Core.Services.Interfaces;
using TOTP.Core.Services.Models;
using TOTP.Infrastructure.Services;

namespace TOTP.Tests.Infrastructure.Services;

public sealed class SupportDiagnosticsServiceTests
{
    [Fact]
    public void Capture_DoesNotIncludeConfiguredFilesystemPathsOrMachineIdentity()
    {
        var paths = new Mock<IPlatformApplicationPaths>();
        paths.SetupGet(value => value.LogDirectory)
            .Returns("C:\\Users\\synthetic-user\\AppData\\Local\\TOTP\\Logs");
        var startup = new Mock<IStartupDiagnostics>();
        startup.Setup(value => value.Snapshot()).Returns(
            [new StartupDiagnosticRecord(StartupDiagnosticStage.Completed, 42, true)]);
        var sut = new SupportDiagnosticsService(paths.Object, startup.Object);

        var snapshot = sut.Capture();
        var serialized = System.Text.Json.JsonSerializer.Serialize(snapshot);

        Assert.DoesNotContain("synthetic-user", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AppData", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(snapshot.OperatingSystem, new[] { "Windows", "macOS", "Linux", "Other" });
        Assert.Single(snapshot.StartupRecords);
    }
}
