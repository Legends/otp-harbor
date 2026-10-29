using System.Diagnostics;
using FluentResults;
using TOTP.Core.Services.Interfaces;

namespace TOTP.Avalonia.Desktop.Platform;

public sealed class AvaloniaPlatformFolderLauncher : IPlatformFolderLauncher
{
    public async Task<Result> OpenFolderAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return Result.Fail("The requested folder is unavailable.");

        try
        {
            var startInfo = CreateStartInfo(path);
            using var process = Process.Start(startInfo);
            if (startInfo.UseShellExecute)
                return Result.Ok();

            if (process is not null
                && !string.Equals(startInfo.FileName, "explorer.exe", StringComparison.OrdinalIgnoreCase))
            {
                await process.WaitForExitAsync(cancellationToken);
                if (process.ExitCode != 0)
                    return Result.Fail("The platform shell did not open the folder.");
            }

            return process is null
                ? Result.Fail("The platform shell did not open the folder.")
                : Result.Ok();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return Result.Fail("The platform shell could not open the folder.");
        }
    }

    private static ProcessStartInfo CreateStartInfo(string path)
    {
        if (OperatingSystem.IsLinux())
        {
            var isWsl = !string.IsNullOrWhiteSpace(
                Environment.GetEnvironmentVariable("WSL_DISTRO_NAME"));
            var startInfo = new ProcessStartInfo
            {
                FileName = isWsl ? "explorer.exe" : "xdg-open",
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = isWsl ? path : string.Empty
            };
            startInfo.ArgumentList.Add(isWsl ? "." : path);
            return startInfo;
        }

        if (OperatingSystem.IsMacOS())
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "open",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add(path);
            return startInfo;
        }

        return new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        };
    }
}
