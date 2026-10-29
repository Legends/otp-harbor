using TOTP.Core.Services.Models;

namespace TOTP.Core.Services.Interfaces;

public interface IPlatformCapabilityStatusProvider
{
    PlatformCapabilityStatus CapabilityStatus { get; }
}
