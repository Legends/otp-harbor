using FluentResults;
using Microsoft.Extensions.Logging;
using TOTP.Core.Services.Interfaces;
using TOTP.Core.Services.Models;
using Windows.ApplicationModel.Email;
using Windows.Storage;
using Windows.Storage.Streams;

namespace TOTP.Platform.Windows;

public interface IWindowsEmailComposer
{
    Task OpenDraftWithAttachmentAsync(
        DiagnosticLogEmailRequest request,
        string snapshotPath,
        CancellationToken cancellationToken = default);

    Task OpenDraftWithoutAttachmentAsync(
        DiagnosticLogEmailRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class WindowsEmailComposer : IWindowsEmailComposer
{
    public async Task OpenDraftWithAttachmentAsync(
        DiagnosticLogEmailRequest request,
        string snapshotPath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var snapshot = await StorageFile.GetFileFromPathAsync(snapshotPath);
        cancellationToken.ThrowIfCancellationRequested();

        var message = new EmailMessage
        {
            Subject = request.Subject,
            Body = request.Body
        };
        message.To.Add(new EmailRecipient(request.Recipient));
        message.Attachments.Add(new EmailAttachment(
            request.AttachmentFileName,
            RandomAccessStreamReference.CreateFromFile(snapshot)));
        await EmailManager.ShowComposeNewEmailAsync(message);
    }

    public Task OpenDraftWithoutAttachmentAsync(
        DiagnosticLogEmailRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var uri = $"mailto:{Uri.EscapeDataString(request.Recipient)}"
            + $"?subject={Uri.EscapeDataString(request.Subject)}"
            + $"&body={Uri.EscapeDataString(request.Body)}";
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri)
        {
            UseShellExecute = true
        });
        return Task.CompletedTask;
    }
}

public sealed class WindowsDiagnosticLogEmailService(
    IPlatformApplicationPaths applicationPaths,
    IPlatformFileSecurity fileSecurity,
    IWindowsEmailComposer emailComposer,
    ILogger<WindowsDiagnosticLogEmailService>? logger = null) : IDiagnosticLogEmailService
{
    internal const long MaximumAttachmentBytes = 10 * 1024 * 1024;
    private readonly string _logDirectory = Path.GetFullPath(applicationPaths.LogDirectory);
    private readonly string _snapshotDirectory = Path.GetFullPath(Path.Combine(
        applicationPaths.ApplicationDataDirectory, "DiagnosticLogShare"));

    public async Task<Result<DiagnosticLogEmailOutcome>> OpenDraftAsync(
        DiagnosticLogEmailRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        string? snapshotPath = null;
        try
        {
            var sourcePath = ValidateSourcePath(request.SourcePath);
            var attachmentName = ValidateAttachmentName(request.AttachmentFileName);
            ValidateEmailFields(request);
            PrepareSnapshotDirectory();
            DeleteExpiredSnapshots();

            snapshotPath = Path.Combine(
                _snapshotDirectory,
                $"{Path.GetFileNameWithoutExtension(attachmentName)}-{Guid.NewGuid():N}.log");
            await CopySnapshotAsync(sourcePath, snapshotPath, cancellationToken).ConfigureAwait(false);
            fileSecurity.RestrictFileToCurrentUser(snapshotPath);
            var normalizedRequest = request with { AttachmentFileName = attachmentName };
            try
            {
                await emailComposer.OpenDraftWithAttachmentAsync(
                    normalizedRequest,
                    snapshotPath,
                    cancellationToken).ConfigureAwait(false);
                logger?.LogInformation("Diagnostic email draft opened with attachment.");
                return Result.Ok(DiagnosticLogEmailOutcome.AttachmentIncluded);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger?.LogDebug("Diagnostic attachment handoff unavailable. Exception type: {ExceptionType}.", exception.GetType().Name);
                TryDelete(snapshotPath);
                snapshotPath = null;
                await emailComposer.OpenDraftWithoutAttachmentAsync(
                    normalizedRequest,
                    cancellationToken).ConfigureAwait(false);
                logger?.LogInformation("Diagnostic email draft opened with manual attachment fallback.");
                return Result.Ok(DiagnosticLogEmailOutcome.ManualAttachmentRequired);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger?.LogError("Diagnostic email draft failed. Exception type: {ExceptionType}.", ex.GetType().Name);
            if (snapshotPath is not null) TryDelete(snapshotPath);
            return Result.Fail<DiagnosticLogEmailOutcome>(new ExceptionalError(
                "The diagnostic email draft could not be opened.", ex));
        }
    }

    private string ValidateSourcePath(string sourcePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        var fullPath = Path.GetFullPath(sourcePath);
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!string.Equals(Path.GetDirectoryName(fullPath), _logDirectory, comparison))
            throw new UnauthorizedAccessException("The diagnostic log is outside the log directory.");

        var info = new FileInfo(fullPath);
        if (!info.Exists
            || (info.Attributes & System.IO.FileAttributes.ReparsePoint) != 0
            || info.Length is <= 0 or > MaximumAttachmentBytes)
        {
            throw new InvalidDataException("The diagnostic log is unavailable or too large.");
        }

        return fullPath;
    }

    private static string ValidateAttachmentName(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        var safeName = Path.GetFileName(fileName);
        if (!string.Equals(safeName, fileName, StringComparison.Ordinal)
            || !string.Equals(Path.GetExtension(safeName), ".log", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The diagnostic attachment name is invalid.");
        }

        return safeName;
    }

    private static void ValidateEmailFields(DiagnosticLogEmailRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Recipient)
            || string.IsNullOrWhiteSpace(request.Subject)
            || string.IsNullOrWhiteSpace(request.Body))
        {
            throw new InvalidDataException("The diagnostic email fields are incomplete.");
        }
    }

    private void PrepareSnapshotDirectory()
    {
        Directory.CreateDirectory(_snapshotDirectory);
        if ((File.GetAttributes(_snapshotDirectory) & System.IO.FileAttributes.ReparsePoint) != 0)
            throw new UnauthorizedAccessException("The diagnostic snapshot directory is a reparse point.");
        fileSecurity.RestrictDirectoryToCurrentUser(_snapshotDirectory);
    }

    private void DeleteExpiredSnapshots()
    {
        var cutoff = DateTime.UtcNow.AddDays(-1);
        foreach (var path in Directory.EnumerateFiles(
                     _snapshotDirectory, "*.log", SearchOption.TopDirectoryOnly))
        {
            try
            {
                var info = new FileInfo(path);
                if ((info.Attributes & System.IO.FileAttributes.ReparsePoint) == 0
                    && info.LastWriteTimeUtc < cutoff)
                {
                    info.Delete();
                }
            }
            catch
            {
                // A draft may still hold the previous snapshot open.
            }
        }
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
        await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
        await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
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
