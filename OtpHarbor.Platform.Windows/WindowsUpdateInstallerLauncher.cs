using System.Diagnostics;
using System.Security.Cryptography;
using FluentResults;
using Microsoft.Extensions.Logging;
using TOTP.Core.Services.Interfaces;
using TOTP.Core.Services.Models;

namespace TOTP.Platform.Windows;

public sealed class WindowsUpdateInstallerLauncher(
    ISignedPayloadVerifier payloadVerifier,
    IApplicationLifetime applicationLifetime,
    IWindowsUpdateInstallerRuntime runtime,
    ILogger<WindowsUpdateInstallerLauncher> logger) : IUpdateInstallerLauncher
{
    private const long MaximumPackageBytes = 128L * 1024 * 1024;
    private const string UpdaterBundleFolderName = "TOTP.Updater";
    private const string UpdaterExecutableName = "TOTP.Updater.exe";
    private static readonly TimeSpan UpdaterReadyTimeout = TimeSpan.FromSeconds(10);

    public bool IsSupported => OperatingSystem.IsWindows();

    public async Task<Result> LaunchAsync(
        PortableUpdatePackage package,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);
        cancellationToken.ThrowIfCancellationRequested();

        if (!IsSupported)
            return Result.Fail("Update installation is unavailable on this platform.");

        byte[]? packageBytes = null;
        string? runtimeDirectory = null;
        string? readySignalPath = null;
        var helperStarted = false;

        try
        {
            var packagePath = Path.GetFullPath(package.FilePath);
            if (!IsSupportedPackage(packagePath))
                return Result.Fail("The verified update package is not a supported Windows archive.");

            var installDirectory = NormalizeDirectory(runtime.InstallationDirectory);
            var executablePath = ResolveCurrentExecutable(installDirectory);
            if (executablePath is null)
                return Result.Fail("The current application installation could not be resolved safely.");

            var updaterSourceDirectory = Path.Combine(installDirectory, UpdaterBundleFolderName);
            var updaterSourceExecutable = Path.Combine(updaterSourceDirectory, UpdaterExecutableName);
            if (!IsRegularDirectory(updaterSourceDirectory)
                || !IsRegularFile(updaterSourceExecutable))
                return Result.Fail("The trusted update helper is unavailable.");

            await using var packageStream = new FileStream(
                packagePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (packageStream.Length is <= 0 or > MaximumPackageBytes)
                return Result.Fail("The update package size is invalid.");

            packageBytes = GC.AllocateUninitializedArray<byte>(checked((int)packageStream.Length));
            await packageStream.ReadExactlyAsync(packageBytes, cancellationToken);
            if (!payloadVerifier.Verify(packageBytes, package.ExpectedSignature, package.PublicKey))
                return Result.Fail("The update package signature was rejected during installer handoff.");

            runtimeDirectory = Path.Combine(
                NormalizeDirectory(runtime.TemporaryDirectory),
                $"totp-updater-runtime-{Guid.NewGuid():N}");
            await CopyTrustedBundleAsync(updaterSourceDirectory, runtimeDirectory, cancellationToken);

            var updaterRuntimeExecutable = Path.Combine(runtimeDirectory, UpdaterExecutableName);
            if (!IsRegularFile(updaterRuntimeExecutable))
                return Result.Fail("The trusted update helper could not be staged.");

            readySignalPath = Path.Combine(
                NormalizeDirectory(runtime.TemporaryDirectory),
                $"totp-updater-ready-{Guid.NewGuid():N}.signal");
            var logPath = Path.Combine(
                NormalizeDirectory(runtime.TemporaryDirectory),
                "totp-update-helper.log");
            var startInfo = CreateStartInfo(
                updaterRuntimeExecutable,
                runtimeDirectory,
                packagePath,
                installDirectory,
                executablePath,
                runtime.CurrentProcessId,
                logPath,
                readySignalPath);

            helperStarted = runtime.Start(startInfo);
            if (!helperStarted)
                return Result.Fail("The update helper process could not be started.");

            if (!await WaitForReadyAsync(readySignalPath, cancellationToken))
                return Result.Fail("The update helper did not become ready in time.");

            logger.LogInformation("Verified Windows update helper started and signaled readiness.");
            applicationLifetime.Shutdown();
            return Result.Ok();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                "Windows update helper handoff failed safely. failure_type={FailureType}",
                exception.GetType().Name);
            return Result.Fail("The Windows update helper could not be started safely.");
        }
        finally
        {
            if (packageBytes is not null) CryptographicOperations.ZeroMemory(packageBytes);
            TryDeleteFile(readySignalPath);
            if (!helperStarted) TryDeleteDirectory(runtimeDirectory);
        }
    }

    private string? ResolveCurrentExecutable(string installDirectory)
    {
        if (string.IsNullOrWhiteSpace(runtime.CurrentExecutablePath)) return null;
        var executablePath = Path.GetFullPath(runtime.CurrentExecutablePath);
        if (!IsRegularFile(executablePath)) return null;

        var executableDirectory = Path.GetDirectoryName(executablePath);
        return string.Equals(
            NormalizeDirectory(executableDirectory ?? string.Empty),
            installDirectory,
            StringComparison.OrdinalIgnoreCase)
            ? executablePath
            : null;
    }

    private static bool IsSupportedPackage(string path)
    {
        if (!string.Equals(Path.GetExtension(path), ".zip", StringComparison.OrdinalIgnoreCase))
            return false;
        return IsRegularFile(path);
    }

    private static bool IsRegularFile(string path)
    {
        if (!File.Exists(path)) return false;
        return (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0;
    }

    private static bool IsRegularDirectory(string path)
    {
        if (!Directory.Exists(path)) return false;
        return (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0;
    }

    private static ProcessStartInfo CreateStartInfo(
        string updaterExecutable,
        string workingDirectory,
        string packagePath,
        string installDirectory,
        string executablePath,
        int parentProcessId,
        string logPath,
        string readySignalPath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = updaterExecutable,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("--packagePath");
        startInfo.ArgumentList.Add(packagePath);
        startInfo.ArgumentList.Add("--targetDir");
        startInfo.ArgumentList.Add(installDirectory);
        startInfo.ArgumentList.Add("--exePath");
        startInfo.ArgumentList.Add(executablePath);
        startInfo.ArgumentList.Add("--parentPid");
        startInfo.ArgumentList.Add(parentProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("--logPath");
        startInfo.ArgumentList.Add(logPath);
        startInfo.ArgumentList.Add("--readySignalPath");
        startInfo.ArgumentList.Add(readySignalPath);
        return startInfo;
    }

    private static async Task CopyTrustedBundleAsync(
        string sourceDirectory,
        string destinationDirectory,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(destinationDirectory);
        foreach (var sourcePath in Directory.EnumerateFileSystemEntries(
                     sourceDirectory,
                     "*",
                     SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if ((File.GetAttributes(sourcePath) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("The update helper bundle contains an unsupported reparse point.");

            var relativePath = Path.GetRelativePath(sourceDirectory, sourcePath);
            var destinationPath = Path.Combine(destinationDirectory, relativePath);
            if (Directory.Exists(sourcePath))
            {
                Directory.CreateDirectory(destinationPath);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            await using var source = new FileStream(
                sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
            await using var destination = new FileStream(
                destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
            await source.CopyToAsync(destination, cancellationToken);
        }
    }

    private static async Task<bool> WaitForReadyAsync(
        string readySignalPath,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + UpdaterReadyTimeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(readySignalPath)) return true;
            await Task.Delay(100, cancellationToken);
        }

        return false;
    }

    private static string NormalizeDirectory(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static void TryDeleteFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try { File.Delete(path); } catch { }
    }

    private static void TryDeleteDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return;
        try { Directory.Delete(path, recursive: true); } catch { }
    }
}
