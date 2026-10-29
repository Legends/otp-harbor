using Avalonia.Input.Platform;

namespace TOTP.Avalonia.Desktop.Platform;

public sealed class AvaloniaClipboardAccessor
{
    public IClipboard? Current { get; private set; }

    public void Set(IClipboard clipboard) =>
        Current = clipboard ?? throw new ArgumentNullException(nameof(clipboard));
}
