using Windows.Security.Credentials.UI;

namespace TOTP.Platform.Windows.Security;

public interface IHelloVerificationRequester
{
    Task<UserConsentVerificationResult> RequestAsync(
        nint windowHandle,
        string message,
        CancellationToken ct);
}
