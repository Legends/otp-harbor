using Microsoft.Win32;
using TOTP.Core.Platform;
using TOTP.Core.Services.Interfaces;
using TOTP.Core.Services.Models;

namespace TOTP.Platform.Windows;

public sealed class WindowsSessionEventSource : IPlatformSessionEventSource, IPlatformCapabilityStatusProvider
{
    private readonly object _gate = new();
    private bool _started;

    public bool IsSupported => OperatingSystem.IsWindows();
    public PlatformCapabilityStatus CapabilityStatus => IsSupported
        ? PlatformCapabilityStatus.Supported
        : PlatformCapabilityStatus.PermanentlyUnavailable;
    public event EventHandler<PlatformSessionChangedEventArgs>? SessionChanged;

    public void Start()
    {
        lock (_gate)
        {
            if (_started || !IsSupported) return;
            SystemEvents.SessionSwitch += OnSessionSwitch;
            _started = true;
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            if (!_started) return;
            SystemEvents.SessionSwitch -= OnSessionSwitch;
            _started = false;
        }
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs args)
    {
        var state = args.Reason switch
        {
            SessionSwitchReason.SessionLock => PlatformSessionState.Locked,
            SessionSwitchReason.SessionUnlock or
                SessionSwitchReason.SessionLogon or
                SessionSwitchReason.ConsoleConnect or
                SessionSwitchReason.RemoteConnect => PlatformSessionState.Active,
            SessionSwitchReason.SessionLogoff or
                SessionSwitchReason.ConsoleDisconnect or
                SessionSwitchReason.RemoteDisconnect => PlatformSessionState.Disconnected,
            _ => PlatformSessionState.Unknown
        };
        SessionChanged?.Invoke(this, new PlatformSessionChangedEventArgs(state));
    }
}
