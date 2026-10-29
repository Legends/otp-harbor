namespace TOTP.Avalonia.Desktop.Platform;

/// <summary>
/// Path-independent access to a file selected through the operating system picker.
/// The local path is optional because sandboxed providers may expose only streams.
/// </summary>
public interface INativeStorageFile : IAsyncDisposable
{
    string Name { get; }
    string? LocalPath { get; }
    Task<Stream> OpenReadAsync(CancellationToken cancellationToken = default);
    Task<Stream> OpenWriteAsync(CancellationToken cancellationToken = default);
    Task DeleteAsync(CancellationToken cancellationToken = default);
}
