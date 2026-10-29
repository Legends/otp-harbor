using System.Diagnostics;
using System.Runtime.InteropServices;
using TOTP.Avalonia.Desktop.Presentation;
using TOTP.Camera.OpenCv;

namespace TOTP.Avalonia.Desktop.Startup;

internal sealed record M3FilterMeasurement(
    int AccountCount,
    double P50Milliseconds,
    double P95Milliseconds);

internal sealed record M3AutomatedMeasurements(
    string OperatingSystem,
    string Architecture,
    double ProcessUptimeMilliseconds,
    long WorkingSetBytes,
    bool NativeRuntimeAvailable,
    string NativeRuntimeVersion,
    IReadOnlyList<M3FilterMeasurement> FilterMeasurements);

internal static class M3MeasurementProbe
{
    private const int WarmupIterations = 20;
    private const int SampleIterations = 100;

    public static M3AutomatedMeasurements Measure()
    {
        var native = OpenCvNativeRuntimeProbe.Probe();
        var measurements = new[] { 500, 1000, 5000 }
            .Select(MeasureFiltering)
            .ToArray();
        var process = Process.GetCurrentProcess();
        process.Refresh();

        return new M3AutomatedMeasurements(
            RuntimeInformation.OSDescription,
            RuntimeInformation.ProcessArchitecture.ToString(),
            Math.Max(0, (DateTime.UtcNow - process.StartTime.ToUniversalTime()).TotalMilliseconds),
            process.WorkingSet64,
            native.IsAvailable,
            native.Version,
            measurements);
    }

    private static M3FilterMeasurement MeasureFiltering(int accountCount)
    {
        var accounts = Enumerable.Range(0, accountCount)
            .Select(index => new AccountListItemViewModel(
                Guid.Empty,
                $"Issuer {index:D5}",
                $"account-{index:D5}@example.invalid"))
            .ToArray();

        for (var index = 0; index < WarmupIterations; index++)
            _ = AccountListFilter.Apply(accounts, "499");

        var samples = new double[SampleIterations];
        for (var index = 0; index < samples.Length; index++)
        {
            var started = Stopwatch.GetTimestamp();
            _ = AccountListFilter.Apply(accounts, (index % 97).ToString("D2"));
            samples[index] = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        }

        Array.Sort(samples);
        return new M3FilterMeasurement(
            accountCount,
            Percentile(samples, 0.50),
            Percentile(samples, 0.95));
    }

    private static double Percentile(IReadOnlyList<double> sorted, double percentile)
    {
        var index = (int)Math.Ceiling(percentile * sorted.Count) - 1;
        return Math.Round(sorted[Math.Clamp(index, 0, sorted.Count - 1)], 3);
    }
}
