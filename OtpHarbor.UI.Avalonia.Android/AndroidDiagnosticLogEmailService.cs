using Android.Content;
using AndroidX.Core.Content;
using FluentResults;
using TOTP.Core.Services.Interfaces;
using TOTP.Core.Services.Models;

namespace TOTP.Avalonia.Android;

internal sealed class AndroidDiagnosticLogEmailService(
    AndroidActivityProvider activityProvider,
    IPlatformApplicationPaths applicationPaths) : IDiagnosticLogEmailService
{
    private const long MaximumAttachmentBytes = 10 * 1024 * 1024;
    private const string ShareDirectoryName = "diagnostic-log-share";
    private readonly string _logDirectory = Path.GetFullPath(applicationPaths.LogDirectory);

    public async Task<Result<DiagnosticLogEmailOutcome>> OpenDraftAsync(
        DiagnosticLogEmailRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        string? snapshotPath = null;
        try
        {
            var activity = activityProvider.GetCurrent()
                ?? throw new InvalidOperationException("The Android activity is unavailable.");
            var sourcePath = ValidateSourcePath(request.SourcePath);
            var attachmentName = ValidateRequest(request);
            var cachePath = activity.CacheDir?.AbsolutePath;
            if (string.IsNullOrWhiteSpace(cachePath))
                throw new DirectoryNotFoundException("The Android cache directory is unavailable.");

            var shareDirectory = Path.GetFullPath(Path.Combine(cachePath, ShareDirectoryName));
            Directory.CreateDirectory(shareDirectory);
            DeleteExpiredSnapshots(shareDirectory);
            snapshotPath = Path.Combine(shareDirectory, attachmentName);
            if (File.Exists(snapshotPath)) TryDelete(snapshotPath);
            await CopySnapshotAsync(sourcePath, snapshotPath, cancellationToken);

            var contentUri = FileProvider.GetUriForFile(
                activity,
                $"{activity.PackageName}.fileprovider",
                new Java.IO.File(snapshotPath));
            using var intent = new Intent(Intent.ActionSend);
            intent.SetType("text/plain");
            intent.PutExtra(Intent.ExtraEmail, new[] { request.Recipient });
            intent.PutExtra(Intent.ExtraSubject, request.Subject);
            intent.PutExtra(Intent.ExtraText, request.Body);
            intent.PutExtra(Intent.ExtraStream, contentUri);
            intent.ClipData = ClipData.NewRawUri(attachmentName, contentUri);
            intent.AddFlags(ActivityFlags.GrantReadUriPermission);
            using var chooser = Intent.CreateChooser(intent, request.ChooserTitle)
                ?? throw new InvalidOperationException("The Android share chooser is unavailable.");
            chooser.AddFlags(ActivityFlags.GrantReadUriPermission);
            activity.StartActivity(chooser);
            return FluentResults.Result.Ok(DiagnosticLogEmailOutcome.AttachmentIncluded);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (snapshotPath is not null) TryDelete(snapshotPath);
            return FluentResults.Result.Fail<DiagnosticLogEmailOutcome>(new ExceptionalError(
                "The diagnostic email draft could not be opened.", ex));
        }
    }

    private string ValidateSourcePath(string sourcePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        var fullPath = Path.GetFullPath(sourcePath);
        if (!string.Equals(Path.GetDirectoryName(fullPath), _logDirectory, StringComparison.Ordinal))
            throw new UnauthorizedAccessException("The diagnostic log is outside the log directory.");
        var info = new FileInfo(fullPath);
        if (!info.Exists
            || (info.Attributes & FileAttributes.ReparsePoint) != 0
            || info.Length is <= 0 or > MaximumAttachmentBytes)
        {
            throw new InvalidDataException("The diagnostic log is unavailable or too large.");
        }

        return fullPath;
    }

    private static string ValidateRequest(DiagnosticLogEmailRequest request)
    {
        var attachmentName = Path.GetFileName(request.AttachmentFileName);
        if (string.IsNullOrWhiteSpace(attachmentName)
            || !string.Equals(attachmentName, request.AttachmentFileName, StringComparison.Ordinal)
            || !string.Equals(Path.GetExtension(attachmentName), ".log", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(request.Recipient)
            || string.IsNullOrWhiteSpace(request.Subject)
            || string.IsNullOrWhiteSpace(request.Body)
            || string.IsNullOrWhiteSpace(request.ChooserTitle))
        {
            throw new InvalidDataException("The diagnostic email request is invalid.");
        }

        return attachmentName;
    }

    private static async Task CopySnapshotAsync(
        string sourcePath,
        string snapshotPath,
        CancellationToken cancellationToken)
    {
        await using var source = new FileStream(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var destination = new FileStream(
            snapshotPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.Read,
            81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await source.CopyToAsync(destination, cancellationToken);
        await destination.FlushAsync(cancellationToken);
    }

    private static void DeleteExpiredSnapshots(string directory)
    {
        var cutoff = DateTime.UtcNow.AddDays(-1);
        foreach (var path in Directory.EnumerateFiles(directory, "*.log", SearchOption.TopDirectoryOnly))
        {
            try
            {
                var info = new FileInfo(path);
                if ((info.Attributes & FileAttributes.ReparsePoint) == 0
                    && info.LastWriteTimeUtc < cutoff)
                {
                    info.Delete();
                }
            }
            catch
            {
                // The receiving application may still be reading the snapshot.
            }
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
        }
    }
}
