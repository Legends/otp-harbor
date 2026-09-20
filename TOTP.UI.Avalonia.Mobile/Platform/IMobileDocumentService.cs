namespace TOTP.Avalonia.Mobile.Platform;

public interface IMobileDocumentService
{
    Task<MobileReadableDocument?> OpenEncryptedBackupAsync(
        CancellationToken cancellationToken = default);

    Task<MobileReadableDocument?> OpenAccountImportAsync(
        CancellationToken cancellationToken = default);

    Task<MobileReadableDocument?> OpenBrandIconPackAsync(
        CancellationToken cancellationToken = default);

    Task<MobileWritableDocument?> CreateEncryptedBackupAsync(
        string suggestedFileName,
        CancellationToken cancellationToken = default);
}

public sealed class MobileReadableDocument(Stream stream, string name = "") : IDisposable
{
    public Stream Stream { get; } = stream ?? throw new ArgumentNullException(nameof(stream));

    public string Name { get; } = name ?? string.Empty;

    public void Dispose() => Stream.Dispose();
}

public sealed class MobileWritableDocument(
    Stream stream,
    Func<CancellationToken, Task> discardAsync) : IDisposable
{
    private Stream? _stream = stream ?? throw new ArgumentNullException(nameof(stream));

    public Stream Stream => _stream
        ?? throw new ObjectDisposedException(nameof(MobileWritableDocument));

    public async Task DiscardAsync(CancellationToken cancellationToken = default)
    {
        Dispose();
        await discardAsync(cancellationToken);
    }

    public void Dispose() => Interlocked.Exchange(ref _stream, null)?.Dispose();
}
