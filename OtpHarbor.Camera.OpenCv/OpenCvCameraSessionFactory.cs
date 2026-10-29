using System.Security;
using OpenCvSharp;

namespace TOTP.Camera.OpenCv;

public sealed class OpenCvCameraSessionFactory(ICameraAccessProbe? accessProbe = null) : ICameraSessionFactory
{
    public CameraSessionOpenResult OpenDefault()
    {
        CameraAccessStatus access;
        try
        {
            access = accessProbe?.Probe() ?? CameraAccessStatus.Unknown;
        }
        catch
        {
            return CameraSessionOpenResult.Failed(CameraOpenFailure.Unexpected);
        }
        if (access == CameraAccessStatus.PermissionDenied)
            return CameraSessionOpenResult.Failed(CameraOpenFailure.PermissionDenied);
        if (access == CameraAccessStatus.NoCamera)
            return CameraSessionOpenResult.Failed(CameraOpenFailure.NoCamera);

        VideoCapture? capture = null;
        try
        {
            capture = new VideoCapture();
            var opened = capture.IsOpened();

            if (!opened && OperatingSystem.IsWindows())
            {
                try
                {
                    opened = capture.Open(0, VideoCaptureAPIs.DSHOW);
                }
                catch (OpenCVException)
                {
                    opened = false;
                }
            }

            if (!opened)
                opened = capture.Open(0);

            if (!opened || !capture.IsOpened())
            {
                capture.Dispose();
                return CameraSessionOpenResult.Failed(CameraOpenFailure.NoCamera);
            }

            ConfigureCapture(capture);
            var session = new OpenCvCameraSession(capture, new QRCodeDetector());
            capture = null;
            return CameraSessionOpenResult.Success(session);
        }
        catch (UnauthorizedAccessException)
        {
            capture?.Dispose();
            return CameraSessionOpenResult.Failed(CameraOpenFailure.PermissionDenied);
        }
        catch (SecurityException)
        {
            capture?.Dispose();
            return CameraSessionOpenResult.Failed(CameraOpenFailure.PermissionDenied);
        }
        catch (Exception ex) when (IsNativeRuntimeFailure(ex))
        {
            capture?.Dispose();
            return CameraSessionOpenResult.Failed(CameraOpenFailure.NativeRuntimeUnavailable);
        }
        catch
        {
            capture?.Dispose();
            return CameraSessionOpenResult.Failed(CameraOpenFailure.Unexpected);
        }
    }

    private static void ConfigureCapture(VideoCapture capture)
    {
        TrySet(capture, VideoCaptureProperties.FourCC, FourCC.MJPG);
        TrySet(capture, VideoCaptureProperties.FrameWidth, 1280);
        TrySet(capture, VideoCaptureProperties.FrameHeight, 720);
        TrySet(capture, VideoCaptureProperties.Fps, 30);
        TrySet(capture, VideoCaptureProperties.BufferSize, 1);
    }

    private static void TrySet(VideoCapture capture, VideoCaptureProperties property, double value)
    {
        try
        {
            capture.Set(property, value);
        }
        catch (OpenCVException)
        {
            // Optional tuning is backend-dependent.
        }
    }

    private static bool IsNativeRuntimeFailure(Exception exception) =>
        exception is DllNotFoundException
        or BadImageFormatException
        or TypeInitializationException;
}
