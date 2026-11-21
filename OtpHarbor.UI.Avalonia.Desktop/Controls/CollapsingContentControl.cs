using Avalonia;
using Avalonia.Controls;

namespace TOTP.Avalonia.Desktop.Controls;

public sealed class CollapsingContentControl : TransitioningContentControl
{
    private double? _originalHeight;
    internal int HeightPinVersion { get; private set; }
    internal double UnpinnedHeight => _originalHeight ?? Height;
    protected override Type StyleKeyOverride => typeof(TransitioningContentControl);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        if (change.Property == ContentProperty && change.OldValue is not null && change.NewValue is not null
            && VisualRoot is not null && Bounds.Height > 0)
        {
            // The native presenter measures the new content before its transition starts.
            _originalHeight ??= Height;
            HeightPinVersion++;
            SetCurrentValue(HeightProperty, Bounds.Height);
        }
        base.OnPropertyChanged(change);
    }

    internal void ReleaseHeightPin(int version, double? originalHeight = null)
    {
        if (version != HeightPinVersion || (originalHeight ?? _originalHeight) is not { } height) return;
        _originalHeight = null;
        SetCurrentValue(HeightProperty, height);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        ReleaseHeightPin(HeightPinVersion);
    }
}
