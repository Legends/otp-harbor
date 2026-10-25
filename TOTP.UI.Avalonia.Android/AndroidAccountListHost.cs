using Avalonia.Android;
using Avalonia.Platform;
using TOTP.Avalonia.Mobile.Presentation;

namespace TOTP.Avalonia.Android;

internal sealed class AndroidAccountListHost(
    MobileShellViewModel viewModel,
    NativeBrandIconCache brandIconCache,
    Action dismissAccountSearchFocus) : AndroidNativeControlHost
{
    private NativeAccountRecyclerView? _accountList;

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        var context = (parent as AndroidViewControlHandle)?.View.Context
            ?? global::Android.App.Application.Context;
        _accountList = new NativeAccountRecyclerView(
            context,
            viewModel,
            brandIconCache,
            dismissAccountSearchFocus);
        return new AndroidViewControlHandle(_accountList);
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        _accountList?.DisposeHost();
        _accountList = null;
        base.DestroyNativeControlCore(control);
    }
}
