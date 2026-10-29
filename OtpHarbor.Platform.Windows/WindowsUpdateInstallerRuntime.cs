using System.Diagnostics;

namespace TOTP.Platform.Windows;

public interface IWindowsUpdateInstallerRuntime
{
    string InstallationDirectory { get; }
    string? CurrentExecutablePath { get; }
    int CurrentProcessId { get; }
    string TemporaryDirectory { get; }
    bool Start(ProcessStartInfo startInfo);
}

public sealed class WindowsUpdateInstallerRuntime : IWindowsUpdateInstallerRuntime
{
    public string InstallationDirectory => AppContext.BaseDirectory;
    public string? CurrentExecutablePath => Environment.ProcessPath;
    public int CurrentProcessId => Environment.ProcessId;
    public string TemporaryDirectory => Path.GetTempPath();

    public bool Start(ProcessStartInfo startInfo) => Process.Start(startInfo) is not null;
}
