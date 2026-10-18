using Android.Content;
using Android.Provider;
using FluentResults;
using Microsoft.Extensions.Logging;
using TOTP.Core.Backup;
using AndroidResult = Android.App.Result;
using FluentResult = FluentResults.Result;

namespace TOTP.Avalonia.Android;

internal sealed class AndroidAutomaticBackupDestinationAdapter(
    AndroidActivityProvider activityProvider,
    ILogger<AndroidAutomaticBackupDestinationAdapter> logger)
    : IAutomaticBackupDestinationAdapter,
    IDisposable
{
    public const string DestinationKind = "android-saf-tree-v1";
    private const int SelectTreeRequestCode = 0x4f5b;
    private const string FilePrefix = "otp-harbor-auto-";
    private const string FileExtension = ".otphbackup";
    private const string MimeType = "application/octet-stream";
    private readonly SemaphoreSlim _selectionLock = new(1, 1);

    public string Kind => DestinationKind;

    public async Task<Result<AutomaticBackupDestination?>> SelectAsync(
        CancellationToken cancellationToken = default)
    {
        await _selectionLock.WaitAsync(cancellationToken);
        try
        {
            var activity = activityProvider.GetCurrent();
            if (activity is null)
                return Fail<AutomaticBackupDestination?>(AutomaticBackupErrorCode.DestinationUnavailable, "Android activity is unavailable.");

            using var intent = new Intent(Intent.ActionOpenDocumentTree);
            intent.AddFlags(
                ActivityFlags.GrantReadUriPermission
                | ActivityFlags.GrantWriteUriPermission
                | ActivityFlags.GrantPersistableUriPermission
                | ActivityFlags.GrantPrefixUriPermission);
            TrySetInitialDocumentsLocation(intent);
            var result = await StartAsync(activity, intent, cancellationToken);
            var selected = result?.Data;
            if (selected is null) return FluentResult.Ok<AutomaticBackupDestination?>(null);

            var permissionFlags = result!.Flags
                & (ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantWriteUriPermission);
            activity.ContentResolver?.TakePersistableUriPermission(selected, permissionFlags);
            var displayName = ReadDisplayName(activity.ContentResolver, selected) ?? selected.LastPathSegment ?? "Android";
            var location = selected.ToString();
            if (string.IsNullOrWhiteSpace(location))
                return Fail<AutomaticBackupDestination?>(AutomaticBackupErrorCode.DestinationUnavailable, "Android returned an invalid backup folder URI.");
            return FluentResult.Ok<AutomaticBackupDestination?>(new AutomaticBackupDestination(
                Kind,
                location,
                displayName));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning("Android automatic backup folder selection failed with {ExceptionType}.", ex.GetType().Name);
            return Fail<AutomaticBackupDestination?>(AutomaticBackupErrorCode.DestinationUnavailable, "The Android backup folder could not be selected.");
        }
        finally
        {
            _selectionLock.Release();
        }
    }

    public async Task<FluentResult> WriteAsync(
        AutomaticBackupDestination destination,
        string fileName,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken = default)
    {
        global::Android.Net.Uri? created = null;
        try
        {
            ValidateFileName(fileName);
            var (resolver, treeUri, rootDocumentUri) = Resolve(destination);
            created = DocumentsContract.CreateDocument(resolver, rootDocumentUri, MimeType, fileName);
            if (created is null)
                return Fail(AutomaticBackupErrorCode.WriteFailed, "Android could not create the automatic backup document.");

            await using (var output = resolver.OpenOutputStream(created, "w")
                ?? throw new IOException("Android backup output stream is unavailable."))
            {
                await output.WriteAsync(payload, cancellationToken);
                await output.FlushAsync(cancellationToken);
            }

            await using var input = resolver.OpenInputStream(created)
                ?? throw new IOException("Android backup verification stream is unavailable.");
            using var expectedHash = System.Security.Cryptography.IncrementalHash.CreateHash(
                System.Security.Cryptography.HashAlgorithmName.SHA256);
            expectedHash.AppendData(payload.Span);
            var expected = expectedHash.GetHashAndReset();
            var actual = await System.Security.Cryptography.SHA256.HashDataAsync(input, cancellationToken);
            try
            {
                if (!System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(expected, actual))
                    throw new InvalidDataException("Android automatic backup write verification failed.");
            }
            finally
            {
                System.Security.Cryptography.CryptographicOperations.ZeroMemory(expected);
                System.Security.Cryptography.CryptographicOperations.ZeroMemory(actual);
            }

            GC.KeepAlive(treeUri);
            return FluentResult.Ok();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning("Android automatic backup publication failed with {ExceptionType}.", ex.GetType().Name);
            TryDelete(created);
            return Fail(AutomaticBackupErrorCode.WriteFailed, "The Android automatic backup could not be published.");
        }
    }

    public Task<Result<IReadOnlyList<AutomaticBackupStoredFile>>> ListAsync(
        AutomaticBackupDestination destination,
        CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (resolver, treeUri, _) = Resolve(destination);
            var treeDocumentId = DocumentsContract.GetTreeDocumentId(treeUri);
            var childrenUri = DocumentsContract.BuildChildDocumentsUriUsingTree(treeUri, treeDocumentId)
                ?? throw new InvalidDataException("Android backup folder children URI is invalid.");
            var files = new List<AutomaticBackupStoredFile>();
            using var cursor = resolver.Query(
                childrenUri,
                [
                    DocumentsContract.Document.ColumnDocumentId,
                    DocumentsContract.Document.ColumnDisplayName,
                    DocumentsContract.Document.ColumnLastModified
                ],
                null,
                null,
                null);
            if (cursor is null)
                return Task.FromResult(Fail<IReadOnlyList<AutomaticBackupStoredFile>>(
                    AutomaticBackupErrorCode.ReadFailed,
                    "Android backup folder could not be enumerated."));

            var idColumn = cursor.GetColumnIndex(DocumentsContract.Document.ColumnDocumentId);
            var nameColumn = cursor.GetColumnIndex(DocumentsContract.Document.ColumnDisplayName);
            var modifiedColumn = cursor.GetColumnIndex(DocumentsContract.Document.ColumnLastModified);
            while (cursor.MoveToNext())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var documentId = idColumn >= 0 ? cursor.GetString(idColumn) : null;
                var name = nameColumn >= 0 ? cursor.GetString(nameColumn) : null;
                if (string.IsNullOrWhiteSpace(documentId)
                    || string.IsNullOrWhiteSpace(name)
                    || !name.StartsWith(FilePrefix, StringComparison.Ordinal)
                    || !name.EndsWith(FileExtension, StringComparison.OrdinalIgnoreCase))
                    continue;

                var documentUri = DocumentsContract.BuildDocumentUriUsingTree(treeUri, documentId)
                    ?? throw new InvalidDataException("Android backup document URI is invalid.");
                var identifier = documentUri.ToString();
                if (string.IsNullOrWhiteSpace(identifier)) continue;
                var modified = modifiedColumn >= 0 && !cursor.IsNull(modifiedColumn)
                    ? cursor.GetLong(modifiedColumn)
                    : 0;
                files.Add(new AutomaticBackupStoredFile(
                    identifier,
                    name,
                    modified > 0
                        ? DateTimeOffset.FromUnixTimeMilliseconds(modified)
                        : DateTimeOffset.MinValue));
            }

            return Task.FromResult(FluentResult.Ok<IReadOnlyList<AutomaticBackupStoredFile>>(files));
        }
        catch (Exception ex)
        {
            logger.LogWarning("Android automatic backup enumeration failed with {ExceptionType}.", ex.GetType().Name);
            return Task.FromResult(Fail<IReadOnlyList<AutomaticBackupStoredFile>>(
                AutomaticBackupErrorCode.ReadFailed,
                "Android automatic backup generations could not be listed."));
        }
    }

    public Task<FluentResult> DeleteAsync(
        AutomaticBackupDestination destination,
        AutomaticBackupStoredFile file,
        CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateFileName(file.Name);
            var (resolver, treeUri, _) = Resolve(destination);
            var documentUri = global::Android.Net.Uri.Parse(file.Identifier)
                ?? throw new InvalidDataException("Android backup document URI is invalid.");
            if (!string.Equals(treeUri.Authority, documentUri.Authority, StringComparison.Ordinal))
                throw new InvalidDataException("Android backup document provider does not match.");

            if (!DocumentsContract.DeleteDocument(resolver, documentUri))
                throw new IOException("Android document provider did not delete the obsolete backup.");
            return Task.FromResult(FluentResult.Ok());
        }
        catch (Exception ex)
        {
            logger.LogWarning("Android automatic backup retention failed with {ExceptionType}.", ex.GetType().Name);
            return Task.FromResult(Fail(
                AutomaticBackupErrorCode.WriteFailed,
                "An obsolete Android automatic backup could not be removed."));
        }
    }

    private (ContentResolver Resolver, global::Android.Net.Uri TreeUri, global::Android.Net.Uri RootDocumentUri) Resolve(
        AutomaticBackupDestination destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (!string.Equals(destination.Kind, Kind, StringComparison.Ordinal))
            throw new InvalidDataException("Android automatic backup destination kind is invalid.");

        var activity = activityProvider.GetCurrent()
            ?? throw new InvalidOperationException("Android activity is unavailable.");
        var resolver = activity.ContentResolver
            ?? throw new InvalidOperationException("Android content resolver is unavailable.");
        var treeUri = global::Android.Net.Uri.Parse(destination.Location)
            ?? throw new InvalidDataException("Android backup tree URI is invalid.");
        if (!string.Equals(treeUri.Scheme, ContentResolver.SchemeContent, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Android backup tree URI scheme is invalid.");

        var rootId = DocumentsContract.GetTreeDocumentId(treeUri);
        var rootDocumentUri = DocumentsContract.BuildDocumentUriUsingTree(treeUri, rootId)
            ?? throw new InvalidDataException("Android backup root document URI is invalid.");
        return (resolver, treeUri, rootDocumentUri);
    }

    private async Task<Intent?> StartAsync(
        MainActivity activity,
        Intent intent,
        CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<(AndroidResult Code, Intent? Data)>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        void OnActivityResult(int requestCode, AndroidResult resultCode, Intent? data)
        {
            if (requestCode == SelectTreeRequestCode)
                completion.TrySetResult((resultCode, data));
        }

        activityProvider.GetCurrent()!.ActivityResultReceived += OnActivityResult;
        using var cancellation = cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
        try
        {
            activity.StartActivityForResult(intent, SelectTreeRequestCode);
            var result = await completion.Task;
            return result.Code == AndroidResult.Ok ? result.Data : null;
        }
        finally
        {
            var current = activityProvider.GetCurrent();
            if (current is not null) current.ActivityResultReceived -= OnActivityResult;
        }
    }

    private static string? ReadDisplayName(
        ContentResolver? resolver,
        global::Android.Net.Uri treeUri)
    {
        if (resolver is null) return null;
        var root = DocumentsContract.BuildDocumentUriUsingTree(
            treeUri,
            DocumentsContract.GetTreeDocumentId(treeUri));
        if (root is null) return null;
        using var cursor = resolver.Query(
            root,
            [DocumentsContract.Document.ColumnDisplayName],
            null,
            null,
            null);
        return cursor is not null && cursor.MoveToFirst() && !cursor.IsNull(0)
            ? cursor.GetString(0)
            : null;
    }

    private static void TrySetInitialDocumentsLocation(Intent intent)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(26)) return;

        try
        {
            var documents = DocumentsContract.BuildDocumentUri(
                "com.android.externalstorage.documents",
                "primary:Documents");
            if (documents is not null)
                intent.PutExtra(DocumentsContract.ExtraInitialUri, documents);
        }
        catch
        {
            // The initial location is only a hint. Providers may reject or ignore it.
        }
    }

    private void TryDelete(global::Android.Net.Uri? documentUri)
    {
        try
        {
            if (documentUri is not null)
            {
                var resolver = activityProvider.GetCurrent()?.ContentResolver;
                if (resolver is not null)
                    DocumentsContract.DeleteDocument(resolver, documentUri);
            }
        }
        catch
        {
        }
    }

    private static void ValidateFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)
            || fileName.Contains('/')
            || fileName.Contains('\\')
            || !fileName.StartsWith(FilePrefix, StringComparison.Ordinal)
            || !fileName.EndsWith(FileExtension, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Android automatic backup file name is invalid.");
        }
    }

    private static FluentResult Fail(AutomaticBackupErrorCode code, string message) =>
        FluentResult.Fail(new AutomaticBackupError(code, message));

    private static Result<T> Fail<T>(AutomaticBackupErrorCode code, string message) =>
        FluentResult.Fail<T>(new AutomaticBackupError(code, message));

    public void Dispose() => _selectionLock.Dispose();
}
