using System.Globalization;
using System.Security.Cryptography;
using Avalonia.Platform.Storage;
using FluentResults;
using Microsoft.Extensions.Logging;
using TOTP.Avalonia.Desktop.Localization;
using TOTP.Core.Backup;

namespace TOTP.Avalonia.Desktop.Platform;

public sealed class AvaloniaAutomaticBackupDestinationAdapter(
    AvaloniaWindowCoordinator windowCoordinator,
    IAvaloniaLocalizationService localization,
    ILogger<AvaloniaAutomaticBackupDestinationAdapter> logger)
    : IAutomaticBackupDestinationAdapter
{
    public const string DestinationKind = "desktop-folder-v1";
    private const string FilePrefix = "otp-harbor-auto-";
    private const string FileExtension = ".otphbackup";

    public string Kind => DestinationKind;

    public async Task<Result<AutomaticBackupDestination?>> SelectAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var provider = windowCoordinator.GetRequiredDialogOwner().StorageProvider;
            if (!provider.CanPickFolder)
                return Fail<AutomaticBackupDestination?>(AutomaticBackupErrorCode.DestinationUnavailable, "Folder selection is unavailable.");

            var folders = await provider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = localization.GetString(AvaloniaStringKeys.SelectAutomaticBackupFolder),
                AllowMultiple = false
            });
            cancellationToken.ThrowIfCancellationRequested();
            if (folders.Count != 1) return Result.Ok<AutomaticBackupDestination?>(null);

            var path = folders[0].TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(path))
                return Fail<AutomaticBackupDestination?>(AutomaticBackupErrorCode.DestinationUnavailable, "The selected folder is not locally writable.");

            var fullPath = Path.GetFullPath(path);
            return Result.Ok<AutomaticBackupDestination?>(new AutomaticBackupDestination(
                Kind,
                fullPath,
                folders[0].Name));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning("Automatic backup folder selection failed with {ExceptionType}.", ex.GetType().Name);
            return Fail<AutomaticBackupDestination?>(AutomaticBackupErrorCode.DestinationUnavailable, "The automatic backup folder could not be selected.");
        }
    }

    public async Task<Result> WriteAsync(
        AutomaticBackupDestination destination,
        string fileName,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken = default)
    {
        string? temporaryPath = null;
        try
        {
            var folder = ValidateDestination(destination);
            ValidateFileName(fileName);
            if (!Directory.Exists(folder))
                return Fail(AutomaticBackupErrorCode.DestinationUnavailable, "The automatic backup folder is unavailable.");

            var finalPath = Path.Combine(folder, fileName);
            temporaryPath = Path.Combine(folder, $".{fileName}.{Guid.NewGuid():N}.tmp");
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                81920,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(payload, cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            var actual = await File.ReadAllBytesAsync(temporaryPath, cancellationToken);
            try
            {
                if (!CryptographicOperations.FixedTimeEquals(payload.Span, actual))
                    throw new InvalidDataException("Automatic backup write verification failed.");
            }
            finally
            {
                CryptographicOperations.ZeroMemory(actual);
            }

            File.Move(temporaryPath, finalPath, overwrite: false);
            temporaryPath = null;
            return Result.Ok();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning("Automatic backup publication failed with {ExceptionType}.", ex.GetType().Name);
            return Fail(AutomaticBackupErrorCode.WriteFailed, "The automatic backup could not be published.");
        }
        finally
        {
            TryDelete(temporaryPath);
        }
    }

    public Task<Result<IReadOnlyList<AutomaticBackupStoredFile>>> ListAsync(
        AutomaticBackupDestination destination,
        CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var folder = ValidateDestination(destination);
            if (!Directory.Exists(folder))
                return Task.FromResult(Fail<IReadOnlyList<AutomaticBackupStoredFile>>(
                    AutomaticBackupErrorCode.DestinationUnavailable,
                    "The automatic backup folder is unavailable."));

            IReadOnlyList<AutomaticBackupStoredFile> files = Directory
                .EnumerateFiles(folder, $"{FilePrefix}*{FileExtension}", SearchOption.TopDirectoryOnly)
                .Select(path =>
                {
                    var name = Path.GetFileName(path);
                    var info = new FileInfo(path);
                    return new AutomaticBackupStoredFile(
                        path,
                        name,
                        TryParseCreatedUtc(name, out var createdUtc)
                            ? createdUtc
                            : new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero));
                })
                .ToList();
            return Task.FromResult(Result.Ok(files));
        }
        catch (Exception ex)
        {
            logger.LogWarning("Automatic backup enumeration failed with {ExceptionType}.", ex.GetType().Name);
            return Task.FromResult(Fail<IReadOnlyList<AutomaticBackupStoredFile>>(
                AutomaticBackupErrorCode.ReadFailed,
                "Automatic backup generations could not be listed."));
        }
    }

    public Task<Result> DeleteAsync(
        AutomaticBackupDestination destination,
        AutomaticBackupStoredFile file,
        CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var folder = ValidateDestination(destination);
            ValidateFileName(file.Name);
            var expected = Path.GetFullPath(Path.Combine(folder, file.Name));
            var actual = Path.GetFullPath(file.Identifier);
            if (!string.Equals(expected, actual, PathComparison))
                return Task.FromResult(Fail(AutomaticBackupErrorCode.DestinationUnavailable, "Automatic backup file identity is invalid."));

            if (File.Exists(actual)) File.Delete(actual);
            return Task.FromResult(Result.Ok());
        }
        catch (Exception ex)
        {
            logger.LogWarning("Automatic backup retention failed with {ExceptionType}.", ex.GetType().Name);
            return Task.FromResult(Fail(AutomaticBackupErrorCode.WriteFailed, "An obsolete automatic backup could not be removed."));
        }
    }

    private static string ValidateDestination(AutomaticBackupDestination destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (!string.Equals(destination.Kind, DestinationKind, StringComparison.Ordinal))
            throw new InvalidDataException("Automatic backup destination kind is invalid.");
        return Path.GetFullPath(destination.Location);
    }

    private static void ValidateFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)
            || !string.Equals(fileName, Path.GetFileName(fileName), StringComparison.Ordinal)
            || !fileName.StartsWith(FilePrefix, StringComparison.Ordinal)
            || !fileName.EndsWith(FileExtension, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Automatic backup file name is invalid.");
        }
    }

    private static bool TryParseCreatedUtc(string fileName, out DateTimeOffset createdUtc)
    {
        createdUtc = default;
        if (!fileName.StartsWith(FilePrefix, StringComparison.Ordinal)
            || fileName.Length < FilePrefix.Length + 20)
            return false;

        return DateTimeOffset.TryParseExact(
            fileName.AsSpan(FilePrefix.Length, 19),
            "yyyyMMdd'T'HHmmssfff'Z'",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out createdUtc);
    }

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static void TryDelete(string? path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path)) File.Delete(path);
        }
        catch
        {
        }
    }

    private static Result Fail(AutomaticBackupErrorCode code, string message) =>
        Result.Fail(new AutomaticBackupError(code, message));

    private static Result<T> Fail<T>(AutomaticBackupErrorCode code, string message) =>
        Result.Fail<T>(new AutomaticBackupError(code, message));
}
