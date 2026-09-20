using Android.App;
using Android.Content;
using Android.Provider;
using TOTP.Avalonia.Mobile.Platform;
using AndroidResult = Android.App.Result;

namespace TOTP.Avalonia.Android;

internal sealed class AndroidDocumentService(AndroidActivityProvider activityProvider) :
    IMobileDocumentService,
    IDisposable
{
    private const int OpenRequestCode = 0x4f55;
    private const int CreateRequestCode = 0x4f56;
    private const int BrandIconPackRequestCode = 0x4f57;
    private const int AccountImportRequestCode = 0x4f58;
    private readonly SemaphoreSlim _operationLock = new(1, 1);

    public async Task<MobileReadableDocument?> OpenEncryptedBackupAsync(
        CancellationToken cancellationToken = default)
    {
        using var intent = new Intent(Intent.ActionOpenDocument);
        intent.AddCategory(Intent.CategoryOpenable);
        intent.SetType("application/octet-stream");
        var selected = await StartAsync(intent, OpenRequestCode, cancellationToken);
        if (selected is null) return null;

        var activity = activityProvider.GetCurrent();
        var stream = activity?.ContentResolver?.OpenInputStream(selected);
        return stream is null ? null : new MobileReadableDocument(stream);
    }

    public async Task<MobileWritableDocument?> CreateEncryptedBackupAsync(
        string suggestedFileName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(suggestedFileName);
        using var intent = new Intent(Intent.ActionCreateDocument);
        intent.AddCategory(Intent.CategoryOpenable);
        intent.SetType("application/octet-stream");
        intent.PutExtra(Intent.ExtraTitle, suggestedFileName);
        var selected = await StartAsync(intent, CreateRequestCode, cancellationToken);
        if (selected is null) return null;

        var activity = activityProvider.GetCurrent();
        var resolver = activity?.ContentResolver;
        if (resolver is null) return null;

        var stream = resolver.OpenOutputStream(selected, "w");
        if (stream is null)
        {
            resolver.Delete(selected, null, null);
            return null;
        }

        return new MobileWritableDocument(
            stream,
            _ =>
            {
                resolver.Delete(selected, null, null);
                return Task.CompletedTask;
            });
    }

    public async Task<MobileReadableDocument?> OpenAccountImportAsync(
        CancellationToken cancellationToken = default)
    {
        using var intent = new Intent(Intent.ActionOpenDocument);
        intent.AddCategory(Intent.CategoryOpenable);
        intent.SetType("*/*");
        intent.PutExtra(Intent.ExtraMimeTypes, new[]
        {
            "application/json",
            "application/octet-stream",
            "text/plain",
            "text/csv",
            "text/comma-separated-values",
            "application/csv"
        });
        var selected = await StartAsync(intent, AccountImportRequestCode, cancellationToken);
        if (selected is null) return null;

        return OpenReadableDocument(selected, "accounts.txt");
    }

    public async Task<MobileReadableDocument?> OpenBrandIconPackAsync(
        CancellationToken cancellationToken = default)
    {
        using var intent = new Intent(Intent.ActionOpenDocument);
        intent.AddCategory(Intent.CategoryOpenable);
        intent.SetType("application/zip");
        var selected = await StartAsync(intent, BrandIconPackRequestCode, cancellationToken);
        if (selected is null) return null;

        return OpenReadableDocument(selected, "simple-icons.zip");
    }

    private MobileReadableDocument? OpenReadableDocument(
        global::Android.Net.Uri selected,
        string fallbackName)
    {
        var resolver = activityProvider.GetCurrent()?.ContentResolver;
        var stream = resolver?.OpenInputStream(selected);
        if (stream is null) return null;

        try
        {
            var name = resolver?.GetType(selected) switch
            {
                "application/json" => "accounts.json",
                "application/octet-stream" => "accounts.2fas",
                "text/csv" or "text/comma-separated-values" or "application/csv" => "accounts.csv",
                _ => fallbackName
            };
            using var cursor = resolver?.Query(
                selected,
                [IOpenableColumns.DisplayName],
                null,
                null,
                null);
            if (cursor is not null && cursor.MoveToFirst())
            {
                var nameColumn = cursor.GetColumnIndex(IOpenableColumns.DisplayName);
                if (nameColumn >= 0 && !cursor.IsNull(nameColumn))
                    name = cursor.GetString(nameColumn) ?? fallbackName;
            }

            return new MobileReadableDocument(stream, name);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    private async Task<global::Android.Net.Uri?> StartAsync(
        Intent intent,
        int requestCode,
        CancellationToken cancellationToken)
    {
        await _operationLock.WaitAsync(cancellationToken);
        try
        {
            var activity = activityProvider.GetCurrent();
            if (activity is null) return null;

            var completion = new TaskCompletionSource<(AndroidResult Code, Intent? Data)>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            void OnActivityResult(int completedRequestCode, AndroidResult resultCode, Intent? data)
            {
                if (completedRequestCode == requestCode)
                    completion.TrySetResult((resultCode, data));
            }

            activity.ActivityResultReceived += OnActivityResult;
            using var cancellation = cancellationToken.Register(() =>
                completion.TrySetCanceled(cancellationToken));
            try
            {
                activity.StartActivityForResult(intent, requestCode);
                var result = await completion.Task;
                return result.Code == AndroidResult.Ok ? result.Data?.Data : null;
            }
            finally
            {
                activity.ActivityResultReceived -= OnActivityResult;
            }
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public void Dispose() => _operationLock.Dispose();
}
