using Avalonia.Platform.Storage;

namespace TOTP.Avalonia.Desktop.Platform;

internal sealed class AvaloniaStorageFile(IStorageFile storageFile) : INativeStorageFile
{
    public string Name => storageFile.Name;
    public string? LocalPath => storageFile.TryGetLocalPath();

    public async Task<Stream> OpenReadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var stream = await storageFile.OpenReadAsync();
        if (cancellationToken.IsCancellationRequested)
        {
            await stream.DisposeAsync();
            cancellationToken.ThrowIfCancellationRequested();
        }
        return stream;
    }

    public async Task<Stream> OpenWriteAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var stream = await storageFile.OpenWriteAsync();
        if (cancellationToken.IsCancellationRequested)
        {
            await stream.DisposeAsync();
            cancellationToken.ThrowIfCancellationRequested();
        }
        return stream;
    }

    public async Task DeleteAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await storageFile.DeleteAsync();
        cancellationToken.ThrowIfCancellationRequested();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
