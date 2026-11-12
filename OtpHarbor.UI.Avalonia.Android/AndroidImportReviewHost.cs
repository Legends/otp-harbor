using Avalonia.Android;
using Avalonia.Platform;
using TOTP.Avalonia.Mobile.Presentation;

namespace TOTP.Avalonia.Android;

internal sealed class AndroidImportReviewHost(MobileShellViewModel viewModel) : AndroidNativeControlHost
{
    private NativeImportReviewRecyclerView? _reviewList;

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        var context = (parent as AndroidViewControlHandle)?.View.Context
            ?? global::Android.App.Application.Context;
        _reviewList = new NativeImportReviewRecyclerView(context, viewModel);
        return new AndroidViewControlHandle(_reviewList);
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        _reviewList?.DisposeHost();
        _reviewList = null;
        base.DestroyNativeControlCore(control);
    }
}
