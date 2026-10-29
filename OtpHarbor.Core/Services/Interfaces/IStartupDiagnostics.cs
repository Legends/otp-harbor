using TOTP.Core.Services.Models;

namespace TOTP.Core.Services.Interfaces;

public interface IStartupDiagnostics
{
    void Record(StartupDiagnosticStage stage, TimeSpan elapsed, bool succeeded);
    IReadOnlyList<StartupDiagnosticRecord> Snapshot();
}
