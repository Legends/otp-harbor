using Microsoft.Extensions.Logging;
using TOTP.Core.Platform;
using TOTP.Core.Services.Interfaces;
using TOTP.Core.Services.Models;

namespace TOTP.Platform.Linux;

public sealed class LinuxSessionEventSource(
    ILinuxSessionMonitorRuntime runtime,
    ILogger<LinuxSessionEventSource> logger) : IPlatformSessionEventSource, IPlatformCapabilityStatusProvider
{
    private readonly object _gate = new();
    private ILinuxSessionMonitor? _monitor;
    private bool _awaitingBoolean;
    private bool _stopping;
    private PlatformSessionState? _lastState;

    public bool IsSupported => runtime.IsSupported;
    public PlatformCapabilityStatus CapabilityStatus => runtime.CapabilityStatus;
    public event EventHandler<PlatformSessionChangedEventArgs>? SessionChanged;

    public void Start()
    {
        lock (_gate)
        {
            if (_monitor is not null || !IsSupported) return;
            _stopping = false;
            _monitor = runtime.Start(ProcessLine, OnMonitorExited);
        }
    }

    public void Stop()
    {
        ILinuxSessionMonitor? monitor;
        lock (_gate)
        {
            _stopping = true;
            monitor = _monitor;
            _monitor = null;
            _awaitingBoolean = false;
            _lastState = null;
        }
        monitor?.Dispose();
    }

    public void ProcessLine(string line)
    {
        PlatformSessionState? state = null;
        lock (_gate)
        {
            if (IsActiveChangedHeader(line))
            {
                _awaitingBoolean = true;
                return;
            }

            if (!_awaitingBoolean) return;
            var trimmed = line.Trim();
            if (string.Equals(trimmed, "boolean true", StringComparison.Ordinal))
                state = PlatformSessionState.Locked;
            else if (string.Equals(trimmed, "boolean false", StringComparison.Ordinal))
                state = PlatformSessionState.Active;
            else if (trimmed.StartsWith("signal ", StringComparison.Ordinal))
                _awaitingBoolean = false;

            if (state is null) return;
            _awaitingBoolean = false;
            if (_lastState == state) return;
            _lastState = state;
        }

        SessionChanged?.Invoke(this, new PlatformSessionChangedEventArgs(state.Value));
    }

    private static bool IsActiveChangedHeader(string line) =>
        line.StartsWith("signal ", StringComparison.Ordinal)
        && line.Contains("member=ActiveChanged", StringComparison.Ordinal)
        && (line.Contains("interface=org.freedesktop.ScreenSaver", StringComparison.Ordinal)
            || line.Contains("interface=org.gnome.ScreenSaver", StringComparison.Ordinal));

    private void OnMonitorExited()
    {
        lock (_gate)
        {
            if (_stopping) return;
            _monitor = null;
        }
        logger.LogWarning("Linux session-lock monitor exited unexpectedly; session locking is temporarily unavailable.");
    }
}
