using Avalonia.Media;

namespace TOTP.Avalonia.Mobile.Platform;

public interface IMobileQrImageFactory
{
    MobileQrImageHandle Create(ReadOnlyMemory<byte> pngBytes);
}

public sealed class MobileQrImageHandle(IImage image, IDisposable lifetime) : IDisposable
{
    public IImage Image { get; } = image ?? throw new ArgumentNullException(nameof(image));

    public void Dispose() => lifetime.Dispose();
}
