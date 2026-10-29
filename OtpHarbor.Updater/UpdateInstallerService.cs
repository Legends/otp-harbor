using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace TOTP.Updater;

internal sealed class UpdateInstallerService(UpdateInstallArguments arguments)
{
    private static readonly TimeSpan ParentCloseGracePeriod = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ProcessExitGracePeriod = TimeSpan.FromSeconds(20);
    private bool _readySignalWritten;

    public async Task RunAsync(IProgress<InstallerProgressState> progress, CancellationToken cancellationToken = default)
    {
        CleanupOldTemporaryFolders();
        Directory.CreateDirectory(Path.GetDirectoryName(arguments.LogPath)!);
        Log("updater started");
        SignalReady();

        progress.Report(CreateState(UpdaterText.InstallingUpdate, UpdaterText.ClosingApp, arguments.PackagePath, string.Empty, true, 0, false));
        await CloseParentApplicationAsync(arguments.ParentProcessId, cancellationToken);

        progress.Report(CreateState(UpdaterText.InstallingUpdate, UpdaterText.WaitingForAppClose, arguments.PackagePath, string.Empty, true, 0, false));
        await WaitForApplicationProcessesExitAsync(cancellationToken);

        progress.Report(CreateState(UpdaterText.InstallingUpdate, UpdaterText.StagingPackage, arguments.PackagePath, string.Empty, true, 0, false));
        var stageDirectory = Path.Combine(Path.GetTempPath(), $"totp-update-stage-{Guid.NewGuid():N}");

        try
        {
            await StagePackageAsync(stageDirectory, cancellationToken);
            var files = EnumerateStageFiles(stageDirectory);

            progress.Report(CreateState(UpdaterText.InstallingUpdate, UpdaterText.InstallingFiles, UpdaterText.FilesQueued(files.Count), UpdaterText.ItemsQueued(files.Count), false, 0, false));
            await UpdateFileTransaction.ApplyAsync(
                files,
                stageDirectory,
                arguments.TargetDirectory,
                progress,
                Log,
                cancellationToken);

            var relaunchTarget = Path.Combine(arguments.TargetDirectory, Path.GetFileName(arguments.ExecutablePath));
            progress.Report(CreateState(UpdaterText.InstallingUpdate, UpdaterText.RelaunchingApp, relaunchTarget, UpdaterText.Complete100, false, 100, false));
            RelaunchApplication();
            Log("application relaunched");
            ScheduleSelfCleanup();
        }
        finally
        {
            TryDeleteDirectory(stageDirectory);
        }
    }

    public void LogFailure(Exception exception)
    {
        Log($"updater failed; exceptionType={exception.GetType().FullName}");
    }

    private static InstallerProgressState CreateState(
        string title,
        string status,
        string detail,
        string progressText,
        bool isIndeterminate,
        int progressValue,
        bool isCloseEnabled)
    {
        return new InstallerProgressState
        {
            Title = title,
            Status = status,
            Detail = detail,
            ProgressText = progressText,
            IsIndeterminate = isIndeterminate,
            ProgressValue = progressValue,
            IsCloseEnabled = isCloseEnabled
        };
    }

    private async Task WaitForApplicationProcessesExitAsync(CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.Add(ProcessExitGracePeriod);
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var runningProcesses = GetRunningApplicationProcesses();
            if (runningProcesses.Count == 0 && !IsProcessStillRunning(arguments.ParentProcessId))
            {
                Log("all target application processes exited");
                return;
            }

            await Task.Delay(200, cancellationToken);
        }

        var remainingProcesses = GetRunningApplicationProcesses();
        foreach (var process in remainingProcesses)
        {
            try
            {
                Log($"force killing lingering process: pid={process.Id} name={process.ProcessName}");
                // Do not kill the entire tree here: this updater is launched by the app process,
                // so killing the parent's tree can include this updater process itself.
                process.Kill();
                await process.WaitForExitAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                Log($"failed to kill lingering process; pid={process.Id} exceptionType={ex.GetType().FullName}");
            }
            finally
            {
                process.Dispose();
            }
        }

        var stillRunning = GetRunningApplicationProcesses();
        if (stillRunning.Count > 0)
        {
            var processSummary = string.Join(", ", stillRunning.Select(p => $"{p.Id}:{p.ProcessName}"));
            foreach (var process in stillRunning)
            {
                process.Dispose();
            }

            throw new IOException($"Application processes still running after close attempts: {processSummary}");
        }
    }

    private async Task CloseParentApplicationAsync(int parentProcessId, CancellationToken cancellationToken)
    {
        try
        {
            using var process = Process.GetProcessById(parentProcessId);
            if (process.HasExited)
            {
                Log($"parent process already closed before close request: {parentProcessId}");
                return;
            }

            process.CloseMainWindow();
            Log($"parent process close requested: {parentProcessId}");

            var deadline = DateTimeOffset.UtcNow.Add(ParentCloseGracePeriod);
            while (!process.HasExited && DateTimeOffset.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Delay(100, cancellationToken);
            }

            if (process.HasExited)
            {
                Log($"parent process exited after close request: {parentProcessId}");
                return;
            }

            Log($"parent process still running after close request; force kill requested: {parentProcessId}");
            process.Kill();
            await process.WaitForExitAsync(cancellationToken);
            Log($"parent process exited after force kill: {parentProcessId}");
        }
        catch (ArgumentException)
        {
            Log($"parent process already exited before close request: {parentProcessId}");
        }
    }

    private List<Process> GetRunningApplicationProcesses()
    {
        var processName = Path.GetFileNameWithoutExtension(arguments.ExecutablePath);
        var executablePath = Path.GetFullPath(arguments.ExecutablePath);

        return Process.GetProcessesByName(processName)
            .Where(process => process.Id != Environment.ProcessId)
            .Where(process => IsMatchingApplicationProcess(process, executablePath))
            .ToList();
    }

    private static bool IsMatchingApplicationProcess(Process process, string executablePath)
    {
        try
        {
            var processPath = process.MainModule?.FileName;
            if (string.IsNullOrWhiteSpace(processPath))
            {
                return false;
            }

            return string.Equals(
                Path.GetFullPath(processPath),
                executablePath,
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsProcessStillRunning(int processId)
    {
        if (processId <= 0)
        {
            return false;
        }

        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private async Task StagePackageAsync(string stageDirectory, CancellationToken cancellationToken)
    {
        TryDeleteDirectory(stageDirectory);
        Directory.CreateDirectory(stageDirectory);

        if (Directory.Exists(arguments.PackagePath))
        {
            await CopyDirectoryAsync(arguments.PackagePath, stageDirectory, cancellationToken);
            Log("package directory copied to stage");
            return;
        }

        if (!File.Exists(arguments.PackagePath))
        {
            throw new FileNotFoundException("The downloaded update package was not found.", arguments.PackagePath);
        }

        var tempZipPath = Path.Combine(Path.GetTempPath(), $"totp-update-package-{Guid.NewGuid():N}.zip");
        try
        {
            File.Copy(arguments.PackagePath, tempZipPath, overwrite: true);
            ZipFile.ExtractToDirectory(tempZipPath, stageDirectory, overwriteFiles: true);
            Log("package archive extracted to stage");
        }
        finally
        {
            TryDeleteFile(tempZipPath);
        }
    }

    private static List<FileInfo> EnumerateStageFiles(string stageDirectory)
    {
        return Directory.EnumerateFiles(stageDirectory, "*", SearchOption.AllDirectories)
            .Select(path => new FileInfo(path))
            .ToList();
    }

    private static async Task CopyDirectoryAsync(string sourceDirectory, string destinationDirectory, CancellationToken cancellationToken)
    {
        foreach (var directory in Directory.EnumerateDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDirectory, directory);
            Directory.CreateDirectory(Path.Combine(destinationDirectory, relative));
        }

        foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var relative = Path.GetRelativePath(sourceDirectory, file);
            var destination = Path.Combine(destinationDirectory, relative);
            var parent = Path.GetDirectoryName(destination);
            if (!string.IsNullOrWhiteSpace(parent))
            {
                Directory.CreateDirectory(parent);
            }

            await using var source = File.OpenRead(file);
            await using var target = File.Create(destination);
            await source.CopyToAsync(target, cancellationToken);
        }
    }

    private void RelaunchApplication()
    {
        var targetExecutablePath = Path.Combine(arguments.TargetDirectory, Path.GetFileName(arguments.ExecutablePath));
        if (!File.Exists(targetExecutablePath))
        {
            throw new FileNotFoundException("The updated application executable was not found.", targetExecutablePath);
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = targetExecutablePath,
            WorkingDirectory = arguments.TargetDirectory,
            UseShellExecute = true
        });
    }

    private void ScheduleSelfCleanup()
    {
        var runtimeDirectory = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        if (!runtimeDirectory.Contains("totp-updater-runtime-", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            var command = $"/c timeout /t 2 /nobreak > nul & rd /s /q \"{runtimeDirectory}\"";
            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = command,
                CreateNoWindow = true,
                UseShellExecute = false
            });
        }
        catch
        {
            // best-effort cleanup only
        }
    }

    private static void CleanupOldTemporaryFolders()
    {
        var tempPath = Path.GetTempPath();
        foreach (var directory in Directory.EnumerateDirectories(tempPath, "totp-updater-runtime-*", SearchOption.TopDirectoryOnly))
        {
            try
            {
                var info = new DirectoryInfo(directory);
                if (DateTime.UtcNow - info.CreationTimeUtc < TimeSpan.FromDays(1))
                {
                    continue;
                }

                TryDeleteDirectory(directory);
            }
            catch
            {
                // best-effort cleanup only
            }
        }
    }

    private void Log(string message)
    {
        var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}";
        File.AppendAllText(arguments.LogPath, line, Encoding.UTF8);
    }

    private void SignalReady()
    {
        if (_readySignalWritten || string.IsNullOrWhiteSpace(arguments.ReadySignalPath))
        {
            return;
        }

        File.WriteAllText(arguments.ReadySignalPath, "ready", Encoding.UTF8);
        _readySignalWritten = true;
        Log($"ready signal written: {arguments.ReadySignalPath}");
    }

    private static void TryDeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private static void TryDeleteFile(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
