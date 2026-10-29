using System.Reflection;
using System.Runtime.InteropServices;
using TOTP.Core.Services.Interfaces;
using TOTP.Core.Services.Models;

namespace TOTP.Infrastructure.Services;

public sealed class SupportDiagnosticsService(
    IPlatformApplicationPaths applicationPaths,
    IStartupDiagnostics startupDiagnostics) : ISupportDiagnosticsService
{
    public SupportDiagnosticsSnapshot Capture() => new(
        GetVersion(),
        GetOperatingSystem(),
        RuntimeInformation.ProcessArchitecture.ToString(),
        RuntimeInformation.FrameworkDescription,
        Directory.Exists(applicationPaths.LogDirectory),
        startupDiagnostics.Snapshot());

    private static string GetVersion() =>
        (Assembly.GetEntryAssembly() ?? typeof(SupportDiagnosticsService).Assembly)
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion.Split('+')[0]
        ?? "unknown";

    private static string GetOperatingSystem() =>
        OperatingSystem.IsWindows() ? "Windows" :
        OperatingSystem.IsMacOS() ? "macOS" :
        OperatingSystem.IsLinux() ? "Linux" :
        "Other";
}
