using System.Security.Cryptography;
using TOTP.Camera.OpenCv;
using TOTP.Core.Services.Interfaces;
using TOTP.Infrastructure.Services;

namespace TOTP.Tests.Services;

public sealed class QrImageDecoderTests
{
    [Fact]
    public async Task DecodeAsync_WhenImageContainsAccountQr_ReturnsExactPayload()
    {
        const string payload =
            "otpauth://totp/Example:alice?secret=JBSWY3DPEHPK3PXP&issuer=Example";
        var png = new QrCodeService().GenerateQr(payload);
        try
        {
            var sut = new OpenCvQrImageDecoder();
            await using var stream = new MemoryStream(png, writable: false);

            var result = await sut.DecodeAsync(
                stream,
                TestContext.Current.CancellationToken);

            Assert.True(result.IsDecoded);
            Assert.Equal(payload, result.Payload);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(png);
        }
    }

    [Fact]
    public async Task DecodeAsync_WhenFileIsNotAnImage_RejectsIt()
    {
        var sut = new OpenCvQrImageDecoder();
        await using var stream = new MemoryStream([1, 2, 3]);

        var result = await sut.DecodeAsync(
            stream,
            TestContext.Current.CancellationToken);

        Assert.False(result.IsDecoded);
        Assert.Equal(QrImageDecodeStatus.InvalidImage, result.Status);
        Assert.Null(result.Payload);
    }
}
