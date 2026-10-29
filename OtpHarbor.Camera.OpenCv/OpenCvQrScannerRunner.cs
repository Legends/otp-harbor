using System.Diagnostics;
using TOTP.Core.Services.Interfaces;

namespace TOTP.Camera.OpenCv;

public sealed record QrScannerOptions(
    TimeSpan DecodeInterval,
    TimeSpan StalledFrameLimit,
    TimeSpan ReadFailureDelay,
    TimeSpan FrameDelay,
    int MaximumConsecutiveReadFailures)
{
    public static QrScannerOptions Default { get; } = new(
        TimeSpan.FromMilliseconds(120),
        TimeSpan.FromMilliseconds(1500),
        TimeSpan.FromMilliseconds(50),
        TimeSpan.FromMilliseconds(10),
        10);
}

public sealed class OpenCvQrScannerRunner : IQrScannerRunner
{
    private readonly ICameraSessionFactory _sessionFactory;
    private readonly TimeProvider _timeProvider;
    private readonly QrScannerOptions _options;

    public OpenCvQrScannerRunner(
        ICameraSessionFactory sessionFactory,
        TimeProvider? timeProvider = null,
        QrScannerOptions? options = null)
    {
        _sessionFactory = sessionFactory ?? throw new ArgumentNullException(nameof(sessionFactory));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _options = options ?? QrScannerOptions.Default;
        if (_options.MaximumConsecutiveReadFailures <= 0)
            throw new ArgumentOutOfRangeException(nameof(options));
    }

    public Task<QrScannerRunResult> RunAsync(
        CancellationToken token,
        Action<byte[]> onPreview,
        Action onFirstFrame) =>
        RunAsync(token, onPreview, static () => { }, onFirstFrame);

    public async Task<QrScannerRunResult> RunAsync(
        CancellationToken token,
        Action<byte[]> onPreview,
        Action onCameraOpened,
        Action onFirstFrame)
    {
        ArgumentNullException.ThrowIfNull(onPreview);
        ArgumentNullException.ThrowIfNull(onCameraOpened);
        ArgumentNullException.ThrowIfNull(onFirstFrame);
        token.ThrowIfCancellationRequested();

        var open = await Task.Run(_sessionFactory.OpenDefault, token).ConfigureAwait(false);
        if (open.Session is null)
            return QrScannerRunResult.Failed(Map(open.Failure));

        using var session = open.Session;
        onCameraOpened();
        var firstFrameShown = false;
        var consecutiveReadFailures = 0;
        ulong? previousFingerprint = null;
        var unchangedFrameStarted = _timeProvider.GetTimestamp();
        var decodeStarted = _timeProvider.GetTimestamp();

        while (true)
        {
            token.ThrowIfCancellationRequested();
            var shouldDecode = _timeProvider.GetElapsedTime(decodeStarted) >= _options.DecodeInterval;
            if (!session.TryRead(shouldDecode, out var frame))
            {
                consecutiveReadFailures++;
                if (consecutiveReadFailures >= _options.MaximumConsecutiveReadFailures)
                    return QrScannerRunResult.Failed(QrScannerFailureKind.DeviceLost);

                await Task.Delay(_options.ReadFailureDelay, _timeProvider, token).ConfigureAwait(false);
                continue;
            }

            consecutiveReadFailures = 0;
            if (previousFingerprint != frame.Fingerprint)
            {
                previousFingerprint = frame.Fingerprint;
                unchangedFrameStarted = _timeProvider.GetTimestamp();
            }
            else if (_timeProvider.GetElapsedTime(unchangedFrameStarted) >= _options.StalledFrameLimit)
            {
                return QrScannerRunResult.Failed(QrScannerFailureKind.Stalled);
            }

            onPreview(frame.PreviewPng);
            if (!firstFrameShown)
            {
                firstFrameShown = true;
                onFirstFrame();
            }

            if (shouldDecode)
            {
                decodeStarted = _timeProvider.GetTimestamp();
                if (!string.IsNullOrWhiteSpace(frame.DecodedText))
                    return QrScannerRunResult.Decoded(frame.DecodedText);
            }

            await Task.Delay(_options.FrameDelay, _timeProvider, token).ConfigureAwait(false);
        }
    }

    private static QrScannerFailureKind Map(CameraOpenFailure failure) => failure switch
    {
        CameraOpenFailure.PermissionDenied => QrScannerFailureKind.PermissionDenied,
        CameraOpenFailure.NoCamera => QrScannerFailureKind.NoCamera,
        CameraOpenFailure.NativeRuntimeUnavailable => QrScannerFailureKind.NativeRuntimeUnavailable,
        _ => QrScannerFailureKind.Unexpected
    };
}
