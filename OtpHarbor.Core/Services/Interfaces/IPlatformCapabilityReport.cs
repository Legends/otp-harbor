using TOTP.Core.Services.Models;

namespace TOTP.Core.Services.Interfaces;

public interface IPlatformCapabilityReport
{
    Task<IReadOnlyList<PlatformCapability>> CaptureAsync(
        CancellationToken cancellationToken = default);
}
