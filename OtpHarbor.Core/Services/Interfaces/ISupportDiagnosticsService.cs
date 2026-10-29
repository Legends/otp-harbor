using TOTP.Core.Services.Models;

namespace TOTP.Core.Services.Interfaces;

public interface ISupportDiagnosticsService
{
    SupportDiagnosticsSnapshot Capture();
}
