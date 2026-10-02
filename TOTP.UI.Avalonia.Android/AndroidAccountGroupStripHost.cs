using Avalonia.Android;
using Avalonia.Platform;
using TOTP.Avalonia.Mobile.Presentation;

namespace TOTP.Avalonia.Android;

internal sealed class AndroidAccountGroupStripHost(MobileShellViewModel viewModel)
    : AndroidNativeControlHost
{
    private NativeAccountGroupRecyclerView? _groupStrip;

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        var context = (parent as AndroidViewControlHandle)?.View.Context
            ?? global::Android.App.Application.Context;
        _groupStrip = new NativeAccountGroupRecyclerView(context, viewModel);
        return new AndroidViewControlHandle(_groupStrip);
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        _groupStrip?.DisposeHost();
        _groupStrip = null;
        base.DestroyNativeControlCore(control);
    }
}
