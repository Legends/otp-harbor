using OpenCvSharp;

namespace TOTP.Camera.OpenCv;

internal static class OpenCvQrDecoder
{
    private const int EnhancedDecodeLongEdge = 1920;

    public static string? Decode(Mat frame, QRCodeDetector detector)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(detector);

        var decoded = Detect(frame, detector);
        if (!string.IsNullOrWhiteSpace(decoded)) return decoded;

        using var grayscale = new Mat();
        Cv2.CvtColor(frame, grayscale, ColorConversionCodes.BGR2GRAY);
        decoded = Detect(grayscale, detector);
        if (!string.IsNullOrWhiteSpace(decoded)) return decoded;

        using var threshold = new Mat();
        Cv2.Threshold(
            grayscale,
            threshold,
            0,
            255,
            ThresholdTypes.Binary | ThresholdTypes.Otsu);
        decoded = Detect(threshold, detector);
        if (!string.IsNullOrWhiteSpace(decoded)) return decoded;

        var longestEdge = Math.Max(grayscale.Width, grayscale.Height);
        if (longestEdge <= 0 || longestEdge >= EnhancedDecodeLongEdge) return null;

        var scale = (double)EnhancedDecodeLongEdge / longestEdge;
        using var enlarged = new Mat();
        Cv2.Resize(
            grayscale,
            enlarged,
            new Size(),
            scale,
            scale,
            InterpolationFlags.Cubic);
        decoded = Detect(enlarged, detector);
        return string.IsNullOrWhiteSpace(decoded) ? null : decoded;
    }

    private static string? Detect(Mat frame, QRCodeDetector detector) =>
        detector.DetectAndDecode(frame, out _);
}
