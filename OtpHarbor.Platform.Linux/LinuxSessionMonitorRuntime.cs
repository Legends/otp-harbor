using System.Diagnostics;
using TOTP.Core.Services.Models;

namespace TOTP.Platform.Linux;

public interface ILinuxSessionMonitor : IDisposable
{
}

public interface ILinuxSessionMonitorRuntime
{
    bool IsSupported { get; }
    PlatformCapabilityStatus CapabilityStatus { get; }
    ILinuxSessionMonitor Start(Action<string> onOutputLine, Action onExited);
}

public sealed class LinuxSessionMonitorRuntime : ILinuxSessionMonitorRuntime
{
    private static readonly string[] SupportedDesktops =
        ["GNOME", "KDE", "PLASMA", "CINNAMON", "MATE"];

    public bool IsSupported => CapabilityStatus == PlatformCapabilityStatus.Supported;

    public PlatformCapabilityStatus CapabilityStatus
    {
        get
        {
            if (!OperatingSystem.IsLinux()) return PlatformCapabilityStatus.PermanentlyUnavailable;
            if (!IsKnownDesktop(Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP")))
                return PlatformCapabilityStatus.PermanentlyUnavailable;
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS"))
                || FindDbusMonitor() is null)
                return PlatformCapabilityStatus.Misconfigured;
            return PlatformCapabilityStatus.Supported;
        }
    }

    public ILinuxSessionMonitor Start(Action<string> onOutputLine, Action onExited)
    {
        ArgumentNullException.ThrowIfNull(onOutputLine);
        ArgumentNullException.ThrowIfNull(onExited);
        var executable = FindDbusMonitor()
            ?? throw new PlatformNotSupportedException("dbus-monitor is unavailable.");
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("--session");
        startInfo.ArgumentList.Add("type='signal',interface='org.freedesktop.ScreenSaver',member='ActiveChanged'");
        startInfo.ArgumentList.Add("type='signal',interface='org.gnome.ScreenSaver',member='ActiveChanged'");

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, args) =>
        {
            if (args.Data is not null) onOutputLine(args.Data);
        };
        process.ErrorDataReceived += static (_, _) => { };
        process.Exited += (_, _) => onExited();
        if (!process.Start())
        {
            process.Dispose();
            throw new InvalidOperationException("dbus-monitor could not be started.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return new ProcessMonitor(process);
    }

    internal static bool IsKnownDesktop(string? desktop) =>
        !string.IsNullOrWhiteSpace(desktop)
        && desktop.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(value => SupportedDesktops.Contains(value, StringComparer.OrdinalIgnoreCase));

    private static string? FindDbusMonitor()
    {
        if (!OperatingSystem.IsLinux()) return null;
        foreach (var path in new[] { "/usr/bin/dbus-monitor", "/bin/dbus-monitor" })
        {
            if (File.Exists(path)) return path;
        }

        var pathValue = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathValue)) return null;
        foreach (var directory in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!Path.IsPathFullyQualified(directory)) continue;
            var candidate = Path.Combine(directory, "dbus-monitor");
            if (File.Exists(candidate)) return candidate;
        }

        return null;
    }

    private sealed class ProcessMonitor(Process process) : ILinuxSessionMonitor
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            try
            {
                process.CancelOutputRead();
                process.CancelErrorRead();
                if (!process.HasExited) process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Best-effort disposal of the monitor process owned by this adapter.
            }
            finally
            {
                process.Dispose();
            }
        }
    }
}
