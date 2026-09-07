using System.Security.Cryptography;
using Android.App;
using Android.Content;
using Android.Graphics;
using Android.Provider;
using AndroidX.Core.Content;
using Microsoft.Extensions.Logging;
using TOTP.Avalonia.Mobile.Platform;
using ZXing;
using ZXing.Common;
using AndroidResult = Android.App.Result;

namespace TOTP.Avalonia.Android;

internal sealed class AndroidQrScanner(
    AndroidActivityProvider activityProvider,
    ILogger<AndroidQrScanner> logger) : IMobileQrScanner
{
    private const int CaptureRequestCode = 0x4f54;
    private const string CaptureDirectoryName = "qr-captures";

    public async Task<MobileQrScanResult> ScanAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var activity = activityProvider.GetCurrent();
        if (activity is null) return MobileQrScanResult.Unavailable;

        using var cameraIntent = new Intent(MediaStore.ActionImageCapture);
        var packageManager = activity.PackageManager;
        if (packageManager is null || cameraIntent.ResolveActivity(packageManager) is null)
            return MobileQrScanResult.Unavailable;

        string? capturePath = null;
        global::Android.Net.Uri? captureUri = null;

        var completion = new TaskCompletionSource<(AndroidResult Code, Intent? Data)>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        void OnActivityResult(int requestCode, AndroidResult resultCode, Intent? data)
        {
            if (requestCode == CaptureRequestCode)
                completion.TrySetResult((resultCode, data));
        }

        activity.ActivityResultReceived += OnActivityResult;
        using var cancellation = cancellationToken.Register(() =>
            completion.TrySetCanceled(cancellationToken));
        try
        {
            var captureDirectory = System.IO.Path.Combine(
                activity.CacheDir!.AbsolutePath,
                CaptureDirectoryName);
            Directory.CreateDirectory(captureDirectory);
            capturePath = System.IO.Path.Combine(captureDirectory, $"{Guid.NewGuid():N}.jpg");
            using var captureFile = new Java.IO.File(capturePath);
            captureUri = FileProvider.GetUriForFile(
                activity,
                $"{activity.PackageName}.fileprovider",
                captureFile);
            cameraIntent.PutExtra(MediaStore.ExtraOutput, captureUri);
            cameraIntent.ClipData = ClipData.NewRawUri(string.Empty, captureUri);
            cameraIntent.AddFlags(
                ActivityFlags.GrantReadUriPermission |
                ActivityFlags.GrantWriteUriPermission);

            activity.StartActivityForResult(cameraIntent, CaptureRequestCode);
            var capture = await completion.Task;
            if (capture.Code != AndroidResult.Ok) return MobileQrScanResult.Cancelled;

            return Decode(capturePath);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                "Android QR capture failed with {ExceptionType}.",
                exception.GetType().Name);
            return MobileQrScanResult.Failed;
        }
        finally
        {
            activity.ActivityResultReceived -= OnActivityResult;
            RevokeCaptureAccess(activity, captureUri);
            DeleteCapture(capturePath);
        }
    }

    private void RevokeCaptureAccess(Activity activity, global::Android.Net.Uri? captureUri)
    {
        if (captureUri is null) return;

        try
        {
            activity.RevokeUriPermission(
                captureUri,
                ActivityFlags.GrantReadUriPermission |
                ActivityFlags.GrantWriteUriPermission);
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                "Android QR capture permission cleanup failed with {ExceptionType}.",
                exception.GetType().Name);
        }
    }

    private void DeleteCapture(string? capturePath)
    {
        if (string.IsNullOrEmpty(capturePath)) return;

        try
        {
            if (File.Exists(capturePath)) File.Delete(capturePath);
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                "Android QR capture cleanup failed with {ExceptionType}.",
                exception.GetType().Name);
        }
    }

    private static MobileQrScanResult Decode(string capturePath)
    {
        var encodedBytes = new FileInfo(capturePath).Length;
        using var bounds = new BitmapFactory.Options { InJustDecodeBounds = true };
        using var ignored = BitmapFactory.DecodeFile(capturePath, bounds);
        var plan = MobileQrCapturePolicy.CreatePlan(
            encodedBytes,
            bounds.OutWidth,
            bounds.OutHeight);
        if (!plan.IsAccepted) return MobileQrScanResult.Failed;

        using var options = new BitmapFactory.Options
        {
            InSampleSize = plan.SampleSize,
            InPreferredConfig = Bitmap.Config.Argb8888
        };
        using var bitmap = BitmapFactory.DecodeFile(capturePath, options);
        return bitmap is null ? MobileQrScanResult.Failed : Decode(bitmap);
    }

    private static MobileQrScanResult Decode(Bitmap bitmap)
    {
        var width = bitmap.Width;
        var height = bitmap.Height;
        if (width <= 0 || height <= 0 ||
            (long)width * height > MobileQrCapturePolicy.MaximumDecodedPixels)
            return MobileQrScanResult.Failed;

        foreach (var size in MobileQrCapturePolicy.CreateDecodeSizes(width, height))
        {
            if (size.Width == width && size.Height == height)
            {
                var originalResult = DecodeSingle(bitmap);
                if (originalResult.Status == MobileQrScanStatus.Success)
                    return originalResult;
                continue;
            }

            using var scaled = Bitmap.CreateScaledBitmap(
                bitmap,
                size.Width,
                size.Height,
                false);
            if (scaled is null) continue;

            var scaledResult = DecodeSingle(scaled);
            if (scaledResult.Status == MobileQrScanStatus.Success)
                return scaledResult;
        }

        return MobileQrScanResult.Failed;
    }

    private static MobileQrScanResult DecodeSingle(Bitmap bitmap)
    {
        var width = bitmap.Width;
        var height = bitmap.Height;
        var pixelCount = checked(width * height);

        var pixels = new int[pixelCount];
        byte[]? rgb = null;
        try
        {
            bitmap.GetPixels(pixels, 0, width, 0, 0, width, height);
            rgb = new byte[checked(pixelCount * 3)];
            for (var index = 0; index < pixelCount; index++)
            {
                var color = pixels[index];
                var offset = index * 3;
                rgb[offset] = (byte)((color >> 16) & 0xff);
                rgb[offset + 1] = (byte)((color >> 8) & 0xff);
                rgb[offset + 2] = (byte)(color & 0xff);
            }

            var reader = new BarcodeReaderGeneric
            {
                AutoRotate = true,
                Options = new DecodingOptions
                {
                    PossibleFormats = [BarcodeFormat.QR_CODE],
                    TryHarder = true,
                    TryInverted = true
                }
            };
            var decoded = reader.Decode(
                rgb,
                width,
                height,
                RGBLuminanceSource.BitmapFormat.RGB24);
            return string.IsNullOrWhiteSpace(decoded?.Text)
                ? MobileQrScanResult.Failed
                : MobileQrScanResult.Successful(decoded.Text);
        }
        finally
        {
            Array.Clear(pixels);
            if (rgb is not null) CryptographicOperations.ZeroMemory(rgb);
        }
    }
}
