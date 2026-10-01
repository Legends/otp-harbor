using Android.Content;
using Microsoft.Extensions.Logging;
using TOTP.Avalonia.Mobile.Localization;
using TOTP.Avalonia.Mobile.Platform;
using AndroidResult = Android.App.Result;

namespace TOTP.Avalonia.Android;

internal sealed class AndroidQrScanner(
    AndroidActivityProvider activityProvider,
    MobileStringCatalog strings,
    ILogger<AndroidQrScanner> logger) : IMobileQrScanner
{
    private const int ScanRequestCode = 0x4f54;

    public async Task<MobileQrScanResult> ScanAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var activity = activityProvider.GetCurrent();
        if (activity is null) return MobileQrScanResult.Unavailable;

        var completion = new TaskCompletionSource<(AndroidResult Code, Intent? Data)>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        void OnActivityResult(int requestCode, AndroidResult resultCode, Intent? data)
        {
            if (requestCode == ScanRequestCode)
                completion.TrySetResult((resultCode, data));
        }

        activity.ActivityResultReceived += OnActivityResult;
        using var cancellation = cancellationToken.Register(() =>
            completion.TrySetCanceled(cancellationToken));
        try
        {
            using var intent = new Intent(activity, typeof(LiveQrScannerActivity));
            intent.PutExtra(
                LiveQrScannerActivity.InstructionExtra,
                strings.Get(MobileStringKeys.ScanQr));
            intent.PutExtra(
                LiveQrScannerActivity.CancelExtra,
                strings.Get(MobileStringKeys.Cancel));

            activity.IsInternalQrScannerActive = true;
            activity.StartActivityForResult(intent, ScanRequestCode);
            var scan = await completion.Task;
            if (scan.Code != AndroidResult.Ok)
            {
                return scan.Data?.GetStringExtra(LiveQrScannerActivity.StatusExtra) switch
                {
                    LiveQrScannerActivity.UnavailableStatus => MobileQrScanResult.Unavailable,
                    LiveQrScannerActivity.FailedStatus => MobileQrScanResult.Failed,
                    _ => MobileQrScanResult.Cancelled
                };
            }

            var payload = scan.Data?.GetStringExtra(LiveQrScannerActivity.PayloadExtra);
            return string.IsNullOrWhiteSpace(payload)
                ? MobileQrScanResult.Failed
                : MobileQrScanResult.Successful(payload);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                "Android live QR scanner failed with {ExceptionType}.",
                exception.GetType().Name);
            return MobileQrScanResult.Failed;
        }
        finally
        {
            activity.IsInternalQrScannerActive = false;
            activity.ActivityResultReceived -= OnActivityResult;
        }
    }
}
