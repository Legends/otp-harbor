using Avalonia.Android;
using Avalonia.Platform;
using TOTP.Avalonia.Mobile.Presentation;

namespace TOTP.Avalonia.Android;

internal sealed class AndroidGroupAccountPickerHost(MobileShellViewModel viewModel)
    : AndroidNativeControlHost
{
    private NativeGroupAccountPickerRecyclerView? _accountPicker;

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        var context = (parent as AndroidViewControlHandle)?.View.Context
            ?? global::Android.App.Application.Context;
        _accountPicker = new NativeGroupAccountPickerRecyclerView(context, viewModel);
        return new AndroidViewControlHandle(_accountPicker);
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        _accountPicker?.DisposeHost();
        _accountPicker = null;
        base.DestroyNativeControlCore(control);
    }
}
