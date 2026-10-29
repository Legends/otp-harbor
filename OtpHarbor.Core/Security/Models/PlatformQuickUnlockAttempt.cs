namespace TOTP.Core.Security.Models;

/// <summary>
/// Represents an expected quick-unlock outcome and owns recovered vault-key
/// bytes when successful.
/// </summary>
public sealed class PlatformQuickUnlockAttempt : IDisposable
{
    private const int VaultKeySize = 32;

    private SensitiveBuffer? _vaultKey;
    private int _disposed;

    private PlatformQuickUnlockAttempt(
        PlatformQuickUnlockStatus status,
        SensitiveBuffer? vaultKey)
    {
        Status = status;
        _vaultKey = vaultKey;
    }

    public PlatformQuickUnlockStatus Status { get; }

    public bool IsSuccess => Status == PlatformQuickUnlockStatus.Succeeded;

    public SensitiveBuffer? VaultKey
    {
        get
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            return _vaultKey;
        }
    }

    public static PlatformQuickUnlockAttempt Successful(SensitiveBuffer vaultKey)
    {
        ArgumentNullException.ThrowIfNull(vaultKey);
        if (vaultKey.Length != VaultKeySize)
        {
            throw new ArgumentException(
                $"Vault key must be exactly {VaultKeySize} bytes.",
                nameof(vaultKey));
        }

        return new PlatformQuickUnlockAttempt(PlatformQuickUnlockStatus.Succeeded, vaultKey);
    }

    public static PlatformQuickUnlockAttempt WithoutKey(PlatformQuickUnlockStatus status)
    {
        if (status is PlatformQuickUnlockStatus.Unknown or PlatformQuickUnlockStatus.Succeeded)
        {
            throw new ArgumentOutOfRangeException(
                nameof(status),
                status,
                "A non-success quick-unlock status is required.");
        }

        return new PlatformQuickUnlockAttempt(status, null);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        var vaultKey = Interlocked.Exchange(ref _vaultKey, null);
        vaultKey?.Dispose();
    }
}
