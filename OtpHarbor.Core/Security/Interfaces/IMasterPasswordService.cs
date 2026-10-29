using TOTP.Core.Security.Models;

namespace TOTP.Core.Security.Interfaces;

public interface IMasterPasswordService
{
    // Generates a new salt and wraps the DEK for initial setup
    Task<(byte[] WrappedDek, byte[] Salt, int Iterations, int MemorySize, byte[] Nonce)>
        WrapKeyAsync(byte[] rawDek, string password);

    // Attempts to recover the DEK using the password; returns null if incorrect
    Task<byte[]?> UnwrapKeyAsync(byte[] wrappedDek, string password, byte[] salt, int iterations, int memorySize, byte[] nonce);

    Task<PasswordKeyWrapperV2> WrapKeyV2Async(
        byte[] rawDek,
        string password,
        CancellationToken cancellationToken = default);

    Task<byte[]?> UnwrapKeyV2Async(
        PasswordKeyWrapperV2 wrapper,
        string password,
        CancellationToken cancellationToken = default);
}
