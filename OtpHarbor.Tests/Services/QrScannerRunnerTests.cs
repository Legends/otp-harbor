using System.Security.Cryptography;
using TOTP.Camera.OpenCv;
using TOTP.Core.Services.Interfaces;

namespace TOTP.Tests.Services;

public sealed class QrScannerRunnerTests
{
    [Theory]
    [InlineData(CameraOpenFailure.PermissionDenied, QrScannerFailureKind.PermissionDenied)]
    [InlineData(CameraOpenFailure.NoCamera, QrScannerFailureKind.NoCamera)]
    [InlineData(CameraOpenFailure.NativeRuntimeUnavailable, QrScannerFailureKind.NativeRuntimeUnavailable)]
    [InlineData(CameraOpenFailure.Unexpected, QrScannerFailureKind.Unexpected)]
    public async Task RunAsync_WhenCameraCannotOpen_ReturnsTypedFailure(
        CameraOpenFailure openFailure,
        QrScannerFailureKind expected)
    {
        var sut = CreateSut(CameraSessionOpenResult.Failed(openFailure));

        var result = await sut.RunAsync(
            TestContext.Current.CancellationToken,
            _ => { },
            () => { });

        Assert.Equal(expected, result.Failure);
        Assert.False(result.IsDecoded);
    }

    [Fact]
    public async Task RunAsync_WhenDecodeSucceeds_EmitsPreviewAndReturnsDecodedText()
    {
        var session = new FakeCameraSession([
            new CameraFrame([1, 2, 3], 1, "otpauth://totp/demo?secret=synthetic")
        ]);
        var sut = CreateSut(CameraSessionOpenResult.Success(session));
        byte[]? preview = null;
        var firstFrameCalls = 0;
        var lifecycle = new List<string>();

        var result = await sut.RunAsync(
            TestContext.Current.CancellationToken,
            bytes => preview = bytes,
            () => lifecycle.Add("opened"),
            () =>
            {
                lifecycle.Add("frame");
                firstFrameCalls++;
            });

        Assert.True(result.IsDecoded);
        Assert.Equal("otpauth://totp/demo?secret=synthetic", result.DecodedText);
        Assert.Equal([1, 2, 3], preview);
        Assert.Equal(1, firstFrameCalls);
        Assert.Equal(["opened", "frame"], lifecycle);
        Assert.True(session.DisposeCalled);
        CryptographicOperations.ZeroMemory(preview!);
    }

    [Fact]
    public async Task RunAsync_WhenCancelled_DisposesSession()
    {
        using var cts = new CancellationTokenSource();
        var session = new FakeCameraSession([
            new CameraFrame([1], 1, null)
        ], repeatLast: true);
        var sut = CreateSut(CameraSessionOpenResult.Success(session));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            sut.RunAsync(
                cts.Token,
                bytes =>
                {
                    CryptographicOperations.ZeroMemory(bytes);
                    cts.Cancel();
                },
                () => { }));

        Assert.True(session.DisposeCalled);
    }

    [Fact]
    public async Task RunAsync_WhenReadsFail_ReturnsDeviceLostAndDisposesSession()
    {
        var session = new FakeCameraSession([]);
        var sut = CreateSut(
            CameraSessionOpenResult.Success(session),
            QrScannerOptions.Default with
            {
                MaximumConsecutiveReadFailures = 2,
                ReadFailureDelay = TimeSpan.Zero
            });

        var result = await sut.RunAsync(
            TestContext.Current.CancellationToken,
            _ => { },
            () => { });

        Assert.Equal(QrScannerFailureKind.DeviceLost, result.Failure);
        Assert.True(session.DisposeCalled);
    }

    [Fact]
    public async Task RunAsync_WhenFrameNeverChanges_ReturnsStalledAndDisposesSession()
    {
        var session = new FakeCameraSession([
            new CameraFrame([1], 42, null)
        ], repeatLast: true);
        var sut = CreateSut(
            CameraSessionOpenResult.Success(session),
            QrScannerOptions.Default with
            {
                DecodeInterval = TimeSpan.Zero,
                StalledFrameLimit = TimeSpan.FromMilliseconds(15),
                FrameDelay = TimeSpan.FromMilliseconds(10)
            });

        var result = await sut.RunAsync(
            TestContext.Current.CancellationToken,
            bytes => CryptographicOperations.ZeroMemory(bytes),
            () => { });

        Assert.Equal(QrScannerFailureKind.Stalled, result.Failure);
        Assert.True(session.DisposeCalled);
    }

    private static OpenCvQrScannerRunner CreateSut(
        CameraSessionOpenResult openResult,
        QrScannerOptions? options = null) =>
        new(
            new FakeCameraSessionFactory(openResult),
            TimeProvider.System,
            options ?? QrScannerOptions.Default with
            {
                DecodeInterval = TimeSpan.Zero,
                FrameDelay = TimeSpan.Zero
            });

    private sealed class FakeCameraSessionFactory(CameraSessionOpenResult result) : ICameraSessionFactory
    {
        public CameraSessionOpenResult OpenDefault() => result;
    }

    private sealed class FakeCameraSession(
        IReadOnlyList<CameraFrame> frames,
        bool repeatLast = false) : ICameraSession
    {
        private int _index;
        public bool DisposeCalled { get; private set; }

        public bool TryRead(bool decodeQr, out CameraFrame frame)
        {
            if (_index < frames.Count)
            {
                frame = frames[_index++];
                return true;
            }

            if (repeatLast && frames.Count > 0)
            {
                var last = frames[^1];
                frame = last with { PreviewPng = last.PreviewPng.ToArray() };
                return true;
            }

            frame = default;
            return false;
        }

        public void Dispose() => DisposeCalled = true;
    }
}
