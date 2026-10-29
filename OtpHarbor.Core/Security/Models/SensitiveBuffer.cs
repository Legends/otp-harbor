using System.Security.Cryptography;

namespace TOTP.Core.Security.Models;

/// <summary>
/// Owns a copy of sensitive bytes and clears that copy on disposal.
/// </summary>
public sealed class SensitiveBuffer : IDisposable
{
    private byte[]? _buffer;

    private SensitiveBuffer(byte[] buffer)
    {
        _buffer = buffer;
    }

    public int Length => GetBuffer().Length;

    public ReadOnlyMemory<byte> Memory => GetBuffer();

    public static SensitiveBuffer CopyFrom(ReadOnlySpan<byte> source)
    {
        if (source.IsEmpty)
        {
            throw new ArgumentException("Sensitive buffer cannot be empty.", nameof(source));
        }

        return new SensitiveBuffer(source.ToArray());
    }

    public void Dispose()
    {
        var buffer = Interlocked.Exchange(ref _buffer, null);
        if (buffer is not null)
        {
            CryptographicOperations.ZeroMemory(buffer);
        }
    }

    private byte[] GetBuffer() =>
        _buffer ?? throw new ObjectDisposedException(nameof(SensitiveBuffer));
}
