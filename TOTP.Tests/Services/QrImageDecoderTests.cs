using System.Security.Cryptography;
using TOTP.Camera.OpenCv;
using TOTP.Core.Services.Interfaces;
using TOTP.Infrastructure.Services;
using TOTP.Tests.TestData;

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
    public async Task DecodeAsync_WhenImageContainsDenseGoogleMigrationQr_ReturnsExactPayload()
    {
        var payload = GoogleAuthenticatorMigrationTestData.TenAccountPayload;
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
            var validation = new QrPayloadValidator().Validate(result.Payload!);
            Assert.True(validation.IsValid);
            Assert.Equal(QrPayloadKind.GoogleAuthenticatorMigration, validation.Kind);
            Assert.Equal(10, validation.AccountCount);
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
