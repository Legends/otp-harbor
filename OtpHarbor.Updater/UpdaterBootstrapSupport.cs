using System.IO;
using System.Text;

namespace TOTP.Updater;

internal static class UpdaterBootstrapSupport
{
    public static void WriteReadySignal(string? readySignalPath)
    {
        if (string.IsNullOrWhiteSpace(readySignalPath))
        {
            return;
        }

        var directory = Path.GetDirectoryName(readySignalPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(readySignalPath, "ready", Encoding.UTF8);
    }

    public static void WriteBootstrapLog(string? logPath, string message)
    {
        if (string.IsNullOrWhiteSpace(logPath))
        {
            return;
        }

        var directory = Path.GetDirectoryName(logPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}";
        File.AppendAllText(logPath, line, Encoding.UTF8);
    }

    public static string? TryGetArgumentValue(IReadOnlyList<string> args, string key)
    {
        for (var index = 0; index < args.Count - 1; index++)
        {
            if (string.Equals(args[index], key, StringComparison.OrdinalIgnoreCase))
            {
                return args[index + 1];
            }
        }

        return null;
    }
}
