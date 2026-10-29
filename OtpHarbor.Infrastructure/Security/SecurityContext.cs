using System;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using TOTP.Core.Security.Interfaces;

namespace TOTP.Infrastructure.Security;

public sealed class SecurityContext : ISecurityContext, IDisposable
{
    private const int VaultKeySize = 32;

    private byte[]? _rawDek;
    private GCHandle _memoryHandle;

    public bool IsUnlocked => _rawDek != null;

    /// <summary>
    /// Securely stores the Data Encryption Key in RAM.
    /// Uses Memory Pinning to prevent the GC from leaving plain-text copies in RAM.
    /// </summary>
    public void SetDek(byte[] dek)
    {
        ArgumentNullException.ThrowIfNull(dek);
        if (dek.Length != VaultKeySize)
            throw new ArgumentException($"DEK must be exactly {VaultKeySize} bytes.", nameof(dek));

        // 1. Clean up any existing key before setting a new one
        Lock();

        // 2. Clone and pin the owned copy. The caller retains ownership of its input
        // and must clear that separate buffer at its own boundary.
        _rawDek = (byte[])dek.Clone();
        _memoryHandle = GCHandle.Alloc(_rawDek, GCHandleType.Pinned);
    }

    /// <summary>
    /// Returns a copy of the active DEK.
    /// Callers must clear the returned buffer after use.
    /// </summary>
    public byte[] GetDekCopy()
    {
        return _rawDek != null
            ? (byte[])_rawDek.Clone()
            : throw new InvalidOperationException("Vault is locked. Access denied.");
    }

    /// <summary>
    /// Securely wipes the key from memory and unpins the handle.
    /// </summary>
    public void Lock()
    {
        if (_rawDek != null)
        {
            // 1. Zero-out the memory contents
            CryptographicOperations.ZeroMemory(_rawDek);

            // 2. Release the handle so the GC can reclaim the empty array
            if (_memoryHandle.IsAllocated)
            {
                _memoryHandle.Free();
            }

            _rawDek = null;
        }
    }

    public void Dispose()
    {
        Lock();
    }
}
