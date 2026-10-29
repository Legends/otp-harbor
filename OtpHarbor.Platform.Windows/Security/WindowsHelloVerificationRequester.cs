using Windows.Security.Credentials.UI;

namespace TOTP.Platform.Windows.Security;

public sealed class WindowsHelloVerificationRequester : IHelloVerificationRequester
{
    public async Task<UserConsentVerificationResult> RequestAsync(
        nint windowHandle,
        string message,
        CancellationToken ct)
    {
        var operation = windowHandle != nint.Zero && OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)
            ? UserConsentVerifierInterop.RequestVerificationForWindowAsync(windowHandle, message)
            : UserConsentVerifier.RequestVerificationAsync(message);

        return await operation.AsTask(ct);
    }
}
