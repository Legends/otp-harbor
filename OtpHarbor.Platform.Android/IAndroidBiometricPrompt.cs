using TOTP.Core.Security.Models;

namespace TOTP.Platform.Android;

public interface IAndroidBiometricPrompt
{
    Task<PlatformQuickUnlockAvailability> GetAvailabilityAsync(
        AndroidQuickUnlockMode mode,
        CancellationToken cancellationToken = default);

    Task<AndroidBiometricPromptResult> AuthenticateAsync(
        AndroidQuickUnlockMode mode,
        Func<byte[]> completeCryptographicOperation,
        CancellationToken cancellationToken = default);
}
