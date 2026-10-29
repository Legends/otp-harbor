namespace TOTP.Core.Services.Models;

public enum StartupDiagnosticStage
{
    Preferences,
    Authorization,
    QuickUnlock,
    Completed
}

public sealed record StartupDiagnosticRecord(
    StartupDiagnosticStage Stage,
    long ElapsedMilliseconds,
    bool Succeeded);

public sealed record SupportDiagnosticsSnapshot(
    string ApplicationVersion,
    string OperatingSystem,
    string ProcessArchitecture,
    string Framework,
    bool LogDirectoryConfigured,
    IReadOnlyList<StartupDiagnosticRecord> StartupRecords);
