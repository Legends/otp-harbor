using TOTP.Core.Services.Models;
using TOTP.Infrastructure.Services;

namespace TOTP.Tests.Infrastructure.Services;

public sealed class StartupDiagnosticsTests
{
    [Fact]
    public void Record_KeepsOnlyLatestBoundedRecordForEachAllowlistedStage()
    {
        var sut = new StartupDiagnostics();

        sut.Record(StartupDiagnosticStage.Preferences, TimeSpan.FromMilliseconds(-5), false);
        sut.Record(StartupDiagnosticStage.Preferences, TimeSpan.FromMilliseconds(12.6), true);
        sut.Record(StartupDiagnosticStage.Completed, TimeSpan.FromMilliseconds(20), true);

        var snapshot = sut.Snapshot();
        Assert.Equal(2, snapshot.Count);
        var preferences = Assert.Single(snapshot, value =>
            value.Stage == StartupDiagnosticStage.Preferences);
        Assert.Equal(13, preferences.ElapsedMilliseconds);
        Assert.True(preferences.Succeeded);
    }
}
