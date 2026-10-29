using System.IO;

namespace TOTP.Updater;

internal static class UpdateFileTransaction
{
    private const int CopyBufferSize = 1024 * 128;

    public static async Task ApplyAsync(
        IReadOnlyList<FileInfo> files,
        string stageDirectory,
        string targetDirectory,
        IProgress<InstallerProgressState> progress,
        Action<string> log,
        CancellationToken cancellationToken)
    {
        var rollbackDirectory = Path.Combine(Path.GetTempPath(), $"totp-update-rollback-{Guid.NewGuid():N}");
        var journal = new List<RollbackEntry>(files.Count);
        Directory.CreateDirectory(rollbackDirectory);

        try
        {
            var totalBytes = files.Sum(static file => file.Length);
            long copiedBytes = 0;
            var copiedFiles = 0;

            foreach (var sourceFile in files.OrderBy(static file => file.FullName, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relativePath = GetSafeRelativePath(stageDirectory, sourceFile.FullName);
                var destinationPath = Path.Combine(targetDirectory, relativePath);
                var destinationDirectory = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrWhiteSpace(destinationDirectory))
                    Directory.CreateDirectory(destinationDirectory);

                var existed = File.Exists(destinationPath);
                var backupPath = Path.Combine(rollbackDirectory, relativePath);
                if (existed)
                {
                    var backupParent = Path.GetDirectoryName(backupPath);
                    if (!string.IsNullOrWhiteSpace(backupParent))
                        Directory.CreateDirectory(backupParent);
                    File.Copy(destinationPath, backupPath, overwrite: false);
                }
                else if (Directory.Exists(destinationPath))
                {
                    throw new IOException($"A directory blocks the update destination '{relativePath}'.");
                }

                journal.Add(new RollbackEntry(destinationPath, backupPath, existed));
                copiedFiles++;
                progress.Report(CreateState(
                    $"{copiedFiles}/{files.Count}: {relativePath}",
                    UpdaterText.FileCountProgress(copiedFiles, files.Count),
                    totalBytes == 0 ? 0 : (int)Math.Clamp((copiedBytes * 100L) / totalBytes, 0, 100)));
                copiedBytes += await CopyFileWithRetriesAsync(
                    sourceFile.FullName,
                    destinationPath,
                    copiedBytes,
                    totalBytes,
                    progress,
                    cancellationToken);
            }

            progress.Report(CreateState(UpdaterText.FinalizingCopiedFiles, UpdaterText.Complete100, 100));
            log($"files copied transactionally: {files.Count}");
        }
        catch (Exception installFailure)
        {
            var rollbackFailures = RollBack(journal, log);
            if (rollbackFailures.Count > 0)
            {
                throw new AggregateException(
                    "The update failed and the previous installation could not be fully restored.",
                    new[] { installFailure }.Concat(rollbackFailures));
            }

            log("update file transaction rolled back");
            throw;
        }
        finally
        {
            TryDeleteDirectory(rollbackDirectory);
        }
    }

    private static string GetSafeRelativePath(string stageDirectory, string sourcePath)
    {
        var relative = Path.GetRelativePath(stageDirectory, sourcePath);
        if (Path.IsPathRooted(relative) ||
            relative.Equals("..", StringComparison.Ordinal) ||
            relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
        {
            throw new IOException("A staged update file is outside the package root.");
        }

        return relative;
    }

    private static List<Exception> RollBack(IReadOnlyList<RollbackEntry> journal, Action<string> log)
    {
        var failures = new List<Exception>();
        for (var index = journal.Count - 1; index >= 0; index--)
        {
            var entry = journal[index];
            try
            {
                if (entry.Existed)
                    File.Copy(entry.BackupPath, entry.DestinationPath, overwrite: true);
                else if (File.Exists(entry.DestinationPath))
                    File.Delete(entry.DestinationPath);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
                log($"rollback failed for one update file: {exception.GetType().Name}");
            }
        }

        return failures;
    }

    private static async Task<long> CopyFileWithRetriesAsync(
        string sourcePath,
        string destinationPath,
        long copiedBytesBeforeFile,
        long totalBytes,
        IProgress<InstallerProgressState> progress,
        CancellationToken cancellationToken)
    {
        const int maxAttempts = 10;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                return await CopyFileAsync(
                    sourcePath,
                    destinationPath,
                    copiedBytesBeforeFile,
                    totalBytes,
                    progress,
                    cancellationToken);
            }
            catch when (attempt < maxAttempts && !cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(500, cancellationToken);
            }
        }

        throw new IOException("Failed to replace an application file after repeated attempts.");
    }

    private static async Task<long> CopyFileAsync(
        string sourcePath,
        string destinationPath,
        long copiedBytesBeforeFile,
        long totalBytes,
        IProgress<InstallerProgressState> progress,
        CancellationToken cancellationToken)
    {
        await using var source = new FileStream(
            sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, CopyBufferSize, useAsync: true);
        await using var destination = new FileStream(
            destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, CopyBufferSize, useAsync: true);

        var buffer = new byte[CopyBufferSize];
        try
        {
            int bytesRead;
            long fileBytesCopied = 0;
            while ((bytesRead = await source.ReadAsync(buffer.AsMemory(), cancellationToken)) > 0)
            {
                await destination.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                fileBytesCopied += bytesRead;
                if (totalBytes > 0)
                {
                    var percentage = (int)Math.Clamp(
                        ((copiedBytesBeforeFile + fileBytesCopied) * 100L) / totalBytes, 0, 100);
                    progress.Report(CreateState(
                        UpdaterText.PercentComplete(percentage),
                        UpdaterText.PercentComplete(percentage),
                        percentage));
                }
            }

            return fileBytesCopied;
        }
        finally
        {
            Array.Clear(buffer);
        }
    }

    private static InstallerProgressState CreateState(string detail, string progressText, int progressValue) =>
        new()
        {
            Title = UpdaterText.InstallingUpdate,
            Status = UpdaterText.InstallingFiles,
            Detail = detail,
            ProgressText = progressText,
            IsIndeterminate = false,
            ProgressValue = progressValue,
            IsCloseEnabled = false
        };

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Best-effort cleanup. A failed rollback has already been surfaced separately.
        }
    }

    private sealed record RollbackEntry(string DestinationPath, string BackupPath, bool Existed);
}
