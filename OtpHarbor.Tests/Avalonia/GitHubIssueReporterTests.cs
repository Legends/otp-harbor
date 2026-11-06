using Moq;
using TOTP.Avalonia.Shared.Diagnostics;

namespace TOTP.Tests.Avalonia;

public sealed class GitHubIssueReporterTests
{
    [Theory]
    [InlineData("3.0.2 (30000299)", "Android 16 (API 36)")]
    [InlineData("4.7.9+development", "Microsoft Windows 11")]
    [InlineData("4.7.9", "Linux x64")]
    [InlineData("4.7.9", "macOS ARM64")]
    public async Task OpenAsync_PrefillsInstalledVersionAndOsOnly(string version, string platform)
    {
        var metadata = new Mock<IDiagnosticEnvironmentProvider>();
        metadata.Setup(value => value.Capture()).Returns(DiagnosticEnvironmentTests.Sample("private-device")
            with { AppVersion = version, OperatingSystem = platform, Build = "private-build" });
        Uri? opened = null;
        var reporter = new GitHubIssueReporter(metadata.Object, uri =>
        {
            opened = uri;
            return Task.FromResult(true);
        });
        metadata.Verify(value => value.Capture(), Times.Never);
        Assert.True(await reporter.OpenAsync());
        Assert.NotNull(opened);
        Assert.Equal("https", opened.Scheme);
        Assert.Equal("github.com", opened.Host);
        Assert.Equal("/Legends/otp-harbor/issues/new", opened.AbsolutePath);
        var query = Query(opened);
        Assert.Equal(3, query.Count);
        Assert.Equal("bug_report.yml", query["template"]);
        Assert.Equal(version, query["version"]);
        Assert.Equal(platform, query["platform"]);
        Assert.DoesNotContain("private-device", opened.AbsoluteUri);
        Assert.DoesNotContain("private-build", opened.AbsoluteUri);
        metadata.Verify(value => value.Capture(), Times.Once);
    }

    [Fact]
    public void CreateUri_EncodesQueryInjectionAndBoundsValues()
    {
        var uri = GitHubIssueReporter.CreateUri("4.0+build&body=injected#fragment\r\n", new string('x', 500));
        var query = Query(uri);
        Assert.Equal(3, query.Count);
        Assert.Equal("4.0+build&body=injected#fragment", query["version"]);
        Assert.Equal(160, query["platform"].Length);
        Assert.Empty(uri.Fragment);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OpenAsync_WhenPlatformOrMetadataFails_ReturnsFailure(bool metadataFails)
    {
        var metadata = new Mock<IDiagnosticEnvironmentProvider>();
        if (metadataFails) metadata.Setup(value => value.Capture()).Throws<InvalidOperationException>();
        else metadata.Setup(value => value.Capture()).Returns(DiagnosticEnvironmentTests.Sample());
        var launches = 0;
        var reporter = new GitHubIssueReporter(metadata.Object, _ =>
        {
            launches++;
            throw new InvalidOperationException("sensitive platform failure");
        });
        Assert.False(await reporter.OpenAsync());
        Assert.Equal(metadataFails ? 0 : 1, launches);
    }

    [Fact]
    public async Task OpenAsync_WhenBrowserRejectsRequest_ReturnsFailure()
    {
        var metadata = new Mock<IDiagnosticEnvironmentProvider>();
        metadata.Setup(value => value.Capture()).Returns(DiagnosticEnvironmentTests.Sample());
        Assert.False(await new GitHubIssueReporter(metadata.Object, _ => Task.FromResult(false)).OpenAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OpenAsync_AwaitsNativeBrowserHandoffAndHandlesAsynchronousFailure(bool throws)
    {
        var metadata = new Mock<IDiagnosticEnvironmentProvider>();
        metadata.Setup(value => value.Capture()).Returns(DiagnosticEnvironmentTests.Sample());
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = new GitHubIssueReporter(metadata.Object, _ => completion.Task).OpenAsync();
        Assert.False(pending.IsCompleted);
        if (throws) completion.SetException(new InvalidOperationException("sensitive native failure"));
        else completion.SetResult(false);
        Assert.False(await pending);
    }

    private static Dictionary<string, string> Query(Uri uri) => uri.Query.TrimStart('?').Split('&')
        .Select(part => part.Split('=', 2)).ToDictionary(parts => parts[0], parts => Uri.UnescapeDataString(parts[1]));
}
