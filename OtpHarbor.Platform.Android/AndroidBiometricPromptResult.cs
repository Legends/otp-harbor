using System.Security.Cryptography;
using TOTP.Core.Security.Models;

namespace TOTP.Platform.Android;

public sealed class AndroidBiometricPromptResult : IDisposable
{
    private bool _disposed;

    private AndroidBiometricPromptResult(
        PlatformQuickUnlockStatus status,
        byte[]? output)
    {
        Status = status;
        Output = output;
    }

    public PlatformQuickUnlockStatus Status { get; }
    public byte[]? Output { get; }

    public static AndroidBiometricPromptResult Successful(
        byte[] output)
    {
        ArgumentNullException.ThrowIfNull(output);
        return new AndroidBiometricPromptResult(
            PlatformQuickUnlockStatus.Succeeded,
            output);
    }

    public static AndroidBiometricPromptResult Failed(PlatformQuickUnlockStatus status)
    {
        if (status is PlatformQuickUnlockStatus.Unknown or PlatformQuickUnlockStatus.Succeeded)
            throw new ArgumentOutOfRangeException(nameof(status));
        return new AndroidBiometricPromptResult(status, null);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (Output is not null) CryptographicOperations.ZeroMemory(Output);
    }
}
