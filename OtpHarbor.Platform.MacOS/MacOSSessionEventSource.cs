using Microsoft.Extensions.Logging;
using TOTP.Core.Platform;
using TOTP.Core.Services.Interfaces;
using TOTP.Core.Services.Models;

namespace TOTP.Platform.MacOS;

public sealed class MacOSSessionEventSource : IPlatformSessionEventSource, IPlatformCapabilityStatusProvider, IDisposable
{
    private readonly IMacOSSessionStateReader _reader;
    private readonly ILogger<MacOSSessionEventSource> _logger;
    private readonly TimeSpan _pollInterval;
    private readonly object _gate = new();
    private Timer? _timer;
    private PlatformSessionState? _lastState;

    public MacOSSessionEventSource(
        IMacOSSessionStateReader reader,
        ILogger<MacOSSessionEventSource> logger,
        TimeSpan? pollInterval = null)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _pollInterval = pollInterval ?? TimeSpan.FromSeconds(1);
        if (_pollInterval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(pollInterval));
    }

    public bool IsSupported => _reader.IsSupported;
    public PlatformCapabilityStatus CapabilityStatus => IsSupported
        ? PlatformCapabilityStatus.Supported
        : PlatformCapabilityStatus.PermanentlyUnavailable;
    public event EventHandler<PlatformSessionChangedEventArgs>? SessionChanged;

    public void Start()
    {
        lock (_gate)
        {
            if (_timer is not null || !IsSupported) return;
            _timer = new Timer(_ => Poll(), null, TimeSpan.Zero, _pollInterval);
        }
    }

    public void Stop()
    {
        Timer? timer;
        lock (_gate)
        {
            timer = _timer;
            _timer = null;
            _lastState = null;
        }
        timer?.Dispose();
    }

    public void Poll()
    {
        PlatformSessionState state;
        try
        {
            var locked = _reader.IsScreenLocked();
            if (locked is null) return;
            state = locked.Value ? PlatformSessionState.Locked : PlatformSessionState.Active;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                "macOS session-state polling failed safely. failure_type={FailureType}",
                exception.GetType().Name);
            return;
        }

        lock (_gate)
        {
            if (_lastState == state) return;
            _lastState = state;
        }
        SessionChanged?.Invoke(this, new PlatformSessionChangedEventArgs(state));
    }

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }
}
