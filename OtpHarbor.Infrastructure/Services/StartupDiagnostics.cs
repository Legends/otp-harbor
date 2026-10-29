using TOTP.Core.Services.Interfaces;
using TOTP.Core.Services.Models;

namespace TOTP.Infrastructure.Services;

public sealed class StartupDiagnostics : IStartupDiagnostics
{
    private readonly object _gate = new();
    private readonly Dictionary<StartupDiagnosticStage, StartupDiagnosticRecord> _records = [];

    public void Record(StartupDiagnosticStage stage, TimeSpan elapsed, bool succeeded)
    {
        var record = new StartupDiagnosticRecord(
            stage,
            Math.Max(0, (long)Math.Round(elapsed.TotalMilliseconds)),
            succeeded);
        lock (_gate)
        {
            _records[stage] = record;
        }
    }

    public IReadOnlyList<StartupDiagnosticRecord> Snapshot()
    {
        lock (_gate)
        {
            return _records.Values.OrderBy(value => value.Stage).ToArray();
        }
    }
}
