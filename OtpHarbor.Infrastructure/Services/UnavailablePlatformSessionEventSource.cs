using TOTP.Core.Platform;
using TOTP.Core.Services.Interfaces;
using TOTP.Core.Services.Models;

namespace TOTP.Infrastructure.Services;

public sealed class UnavailablePlatformSessionEventSource : IPlatformSessionEventSource, IPlatformCapabilityStatusProvider
{
    public bool IsSupported => false;
    public PlatformCapabilityStatus CapabilityStatus => PlatformCapabilityStatus.PermanentlyUnavailable;
    public event EventHandler<PlatformSessionChangedEventArgs>? SessionChanged
    {
        add { }
        remove { }
    }

    public void Start() { }
    public void Stop() { }
}
