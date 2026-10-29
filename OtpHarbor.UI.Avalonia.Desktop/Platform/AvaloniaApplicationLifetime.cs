using Avalonia.Controls.ApplicationLifetimes;
using Serilog;
using TOTP.Core.Services.Interfaces;
using AppLifetime = TOTP.Core.Services.Interfaces.IApplicationLifetime;

namespace TOTP.Avalonia.Desktop.Platform;

internal sealed class AvaloniaApplicationLifetime(
    IClassicDesktopStyleApplicationLifetime desktopLifetime,
    IUiScheduler uiScheduler) : AppLifetime
{
    public void Shutdown(int exitCode = 0)
    {
        if (uiScheduler.CheckAccess())
        {
            desktopLifetime.TryShutdown(exitCode);
            return;
        }

        uiScheduler.Post(() => desktopLifetime.TryShutdown(exitCode));
    }

    public void ExitProcess(int exitCode = 0)
    {
        Log.CloseAndFlush();
        Environment.Exit(exitCode);
    }
}
