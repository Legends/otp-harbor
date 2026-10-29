using TOTP.Core.Security.Models;

namespace TOTP.Platform.MacOS.Security;

public enum MacOSKeychainNativeStatus
{
    Success,
    NotFound,
    Cancelled,
    AccessDenied,
    NotConfigured,
    NotSupported,
    TemporarilyUnavailable,
    Failed
}

public sealed record MacOSKeychainReadResult(
    MacOSKeychainNativeStatus Status,
    byte[]? Secret = null);

public interface IMacOSKeychainNative
{
    PlatformSecretStoreAvailability GetAvailability();
    MacOSKeychainNativeStatus Store(string secretReference, ReadOnlyMemory<byte> secret);
    MacOSKeychainReadResult Retrieve(string secretReference);
    MacOSKeychainNativeStatus Delete(string secretReference);
}
