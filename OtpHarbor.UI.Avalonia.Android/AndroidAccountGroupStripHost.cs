using Avalonia.Android;
using Avalonia.Platform;
using TOTP.Avalonia.Mobile.Presentation;

namespace TOTP.Avalonia.Android;

internal sealed class AndroidAccountGroupStripHost(
    MobileShellViewModel viewModel,
    Action dismissAccountSearchFocus)
    : AndroidNativeControlHost
{
    private NativeAccountGroupRecyclerView? _groupStrip;
    private bool _startupTransitionCompleted;

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        var context = (parent as AndroidViewControlHandle)?.View.Context
            ?? global::Android.App.Application.Context;
        _groupStrip = new NativeAccountGroupRecyclerView(
            context,
            viewModel,
            dismissAccountSearchFocus)
        {
            Alpha = _startupTransitionCompleted ? 1f : 0f
        };
        return new AndroidViewControlHandle(_groupStrip);
    }

    public void CompleteStartupTransition()
    {
        _startupTransitionCompleted = true;
        _groupStrip?.Animate()?.Cancel();
        if (_groupStrip is { } groupStrip) groupStrip.Alpha = 1f;
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        _groupStrip?.DisposeHost();
        _groupStrip = null;
        base.DestroyNativeControlCore(control);
    }
}
