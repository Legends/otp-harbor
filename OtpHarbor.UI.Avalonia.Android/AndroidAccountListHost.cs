using Avalonia.Android;
using Avalonia.Platform;
using TOTP.Avalonia.Mobile.Presentation;

namespace TOTP.Avalonia.Android;

internal sealed class AndroidAccountListHost(
    MobileShellViewModel viewModel,
    NativeBrandIconCache brandIconCache,
    Action dismissAccountSearchFocus,
    Action accountListRendered) : AndroidNativeControlHost
{
    private NativeAccountRecyclerView? _accountList;
    private bool _startupTransitionCompleted;

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        var context = (parent as AndroidViewControlHandle)?.View.Context
            ?? global::Android.App.Application.Context;
        _accountList = new NativeAccountRecyclerView(
            context,
            viewModel,
            brandIconCache,
            dismissAccountSearchFocus,
            accountListRendered)
        {
            Alpha = _startupTransitionCompleted ? 1f : 0f
        };
        return new AndroidViewControlHandle(_accountList);
    }

    public void CompleteStartupTransition()
    {
        _startupTransitionCompleted = true;
        _accountList?.Animate()?.Cancel();
        if (_accountList is { } accountList) accountList.Alpha = 1f;
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        _accountList?.DisposeHost();
        _accountList = null;
        base.DestroyNativeControlCore(control);
    }
}
