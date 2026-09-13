using System.Security.Cryptography;
using OpenCvSharp;
using ZXing;
using ZXing.Common;

namespace TOTP.Camera.OpenCv;

internal static class ZxingQrImageDecoder
{
    public static string? Decode(Mat image)
    {
        ArgumentNullException.ThrowIfNull(image);

        using var grayscale = new Mat();
        if (image.Channels() == 1)
            image.CopyTo(grayscale);
        else
            Cv2.CvtColor(image, grayscale, ColorConversionCodes.BGR2GRAY);

        var pixelCount = checked(grayscale.Width * grayscale.Height);
        grayscale.GetArray(out byte[] pixels);
        if (pixels.Length != pixelCount)
        {
            CryptographicOperations.ZeroMemory(pixels);
            return null;
        }

        try
        {
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
                pixels,
                grayscale.Width,
                grayscale.Height,
                RGBLuminanceSource.BitmapFormat.Gray8);
            if (!string.IsNullOrWhiteSpace(decoded?.Text))
                return decoded.Text;

            var luminance = new RGBLuminanceSource(
                pixels,
                grayscale.Width,
                grayscale.Height,
                RGBLuminanceSource.BitmapFormat.Gray8);
            var globalResult = DecodeGlobalHistogram(luminance);
            if (!string.IsNullOrWhiteSpace(globalResult)) return globalResult;

            return DecodeGlobalHistogram(luminance.invert());
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pixels);
        }
    }

    private static string? DecodeGlobalHistogram(LuminanceSource source)
    {
        var reader = new ZXing.QrCode.QRCodeReader();
        try
        {
            var decoded = reader.decode(
                new BinaryBitmap(new GlobalHistogramBinarizer(source)),
                new Dictionary<DecodeHintType, object>
                {
                    [DecodeHintType.TRY_HARDER] = true,
                    [DecodeHintType.CHARACTER_SET] = "UTF-8"
                });
            return decoded?.Text;
        }
        catch (ReaderException)
        {
            return null;
        }
        finally
        {
            reader.reset();
        }
    }
}
