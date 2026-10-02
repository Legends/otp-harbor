using Avalonia.Android;
using Avalonia.Platform;
using TOTP.Avalonia.Mobile.Presentation;

namespace TOTP.Avalonia.Android;

internal sealed class AndroidAccountListHost(MobileShellViewModel viewModel) : AndroidNativeControlHost
{
    private NativeAccountRecyclerView? _accountList;

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        var context = (parent as AndroidViewControlHandle)?.View.Context
            ?? global::Android.App.Application.Context;
        _accountList = new NativeAccountRecyclerView(context, viewModel);
        return new AndroidViewControlHandle(_accountList);
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        _accountList?.DisposeHost();
        _accountList = null;
        base.DestroyNativeControlCore(control);
    }
}
