using Avalonia.Media;

namespace TOTP.Avalonia.Desktop.Platform;

public interface IAvaloniaQrImageFactory
{
    AvaloniaQrImageHandle Create(ReadOnlyMemory<byte> pngBytes);
}

public sealed class AvaloniaQrImageHandle(IImage image, IDisposable lifetime) : IDisposable
{
    public IImage Image { get; } = image ?? throw new ArgumentNullException(nameof(image));

    public void Dispose() => lifetime.Dispose();
}
