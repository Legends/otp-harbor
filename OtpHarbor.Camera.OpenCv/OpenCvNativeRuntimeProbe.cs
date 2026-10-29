using OpenCvSharp;

namespace TOTP.Camera.OpenCv;

public enum OpenCvNativeRuntimeProbeFailure
{
    None = 0,
    RuntimeLoad,
    DecoderInitialization,
    CaptureInitialization
}

public sealed record OpenCvNativeRuntimeProbeResult(
    bool IsAvailable,
    string Version,
    OpenCvNativeRuntimeProbeFailure Failure);

public static class OpenCvNativeRuntimeProbe
{
    public static OpenCvNativeRuntimeProbeResult Probe()
    {
        var version = string.Empty;
        try
        {
            version = Cv2.GetVersionString() ?? string.Empty;
        }
        catch (Exception)
        {
            return new(false, string.Empty, OpenCvNativeRuntimeProbeFailure.RuntimeLoad);
        }

        try
        {
            using var detector = new QRCodeDetector();
        }
        catch (Exception)
        {
            return new(false, version, OpenCvNativeRuntimeProbeFailure.DecoderInitialization);
        }

        try
        {
            using var capture = new VideoCapture();
            return string.IsNullOrWhiteSpace(version)
                ? new(false, string.Empty, OpenCvNativeRuntimeProbeFailure.RuntimeLoad)
                : new(true, version, OpenCvNativeRuntimeProbeFailure.None);
        }
        catch (Exception)
        {
            return new(false, version, OpenCvNativeRuntimeProbeFailure.CaptureInitialization);
        }
    }
}
