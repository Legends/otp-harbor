using System.Threading;
using System.Threading.Tasks;
using TOTP.Core.Enums;
using TOTP.Core.Security.Models;

namespace TOTP.Core.Security.Interfaces;

public interface IHelloGate
{
    Task<PlatformQuickUnlockAvailability> GetAvailabilityAsync(CancellationToken ct = default);
    Task<bool> IsAvailableAsync(CancellationToken ct = default);
    IDisposable BeginPromptSession();
    Task<AuthorizationResult> RequestVerificationAsync(CancellationToken ct = default);

    // Hardware-bound persistence methods
    Task<byte[]> ProtectKeyAsync(byte[] rawDek, string keyId, CancellationToken ct = default);
    Task<byte[]?> UnprotectKeyAsync(byte[] wrappedDek, string keyId, CancellationToken ct = default);
    Task RemoveKeyAsync(string keyId, CancellationToken ct = default);
}
