using System.IO;

namespace TOTP.Updater;

internal sealed class UpdateInstallArguments
{
    public required string PackagePath { get; init; }
    public required string TargetDirectory { get; init; }
    public required string ExecutablePath { get; init; }
    public required int ParentProcessId { get; init; }
    public required string LogPath { get; init; }
    public string? ReadySignalPath { get; init; }

    public static UpdateInstallArguments Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < args.Length - 1; index += 2)
        {
            values[args[index]] = args[index + 1];
        }

        return new UpdateInstallArguments
        {
            PackagePath = GetRequired(values, "--packagePath"),
            TargetDirectory = GetRequired(values, "--targetDir"),
            ExecutablePath = GetRequired(values, "--exePath"),
            ParentProcessId = int.Parse(GetRequired(values, "--parentPid")),
            LogPath = values.TryGetValue("--logPath", out var logPath)
                ? logPath
                : Path.Combine(Path.GetTempPath(), "totp-updater.log"),
            ReadySignalPath = values.TryGetValue("--readySignalPath", out var readySignalPath)
                ? readySignalPath
                : null
        };
    }

    private static string GetRequired(IReadOnlyDictionary<string, string> values, string key)
    {
        if (values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        throw new ArgumentException($"Missing required argument '{key}'.");
    }
}
