using Microsoft.Extensions.Logging;

namespace TOTP.Avalonia.Shared.Diagnostics;

public interface IGitHubIssueReporter
{
    Task<bool> OpenAsync();
}

public sealed class GitHubIssueReporter(
    IDiagnosticEnvironmentProvider environment,
    Func<Uri, Task<bool>> openUri,
    ILogger<GitHubIssueReporter>? logger = null) : IGitHubIssueReporter
{
    public async Task<bool> OpenAsync()
    {
        try
        {
            var info = environment.Capture();
            var opened = await openUri(CreateUri(info.AppVersion, info.OperatingSystem));
            logger?.Log(opened ? LogLevel.Information : LogLevel.Warning,
                "GitHub report form browser handoff completed. Success: {Success}.", opened);
            return opened;
        }
        catch (Exception exception)
        {
            // Metadata/browser availability is an expected platform boundary failure.
            logger?.LogError("GitHub report form failed with exception type {ExceptionType}.", exception.GetType().Name);
            return false;
        }
    }

    public static Uri CreateUri(string version, string platform) => new(
        "https://github.com/Legends/otp-harbor/issues/new?template=bug_report.yml"
        + $"&version={Encode(version)}&platform={Encode(platform)}");

    private static string Encode(string value) => Uri.EscapeDataString(
        new string(value.Where(character => !char.IsControl(character)).Take(160).ToArray()));
}
