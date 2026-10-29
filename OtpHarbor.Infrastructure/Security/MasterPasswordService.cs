using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NSec.Cryptography;
using TOTP.Core.Security.Interfaces;
using TOTP.Core.Security.Models;

namespace TOTP.Infrastructure.Security;

/// <summary>
/// Corrected MasterPasswordService for NSec unit constraints.
/// NSec uses Kibibytes (KiB) for MemorySize, not Bytes.
/// </summary>
public sealed class MasterPasswordService : IMasterPasswordService
{
    private readonly ILogger<MasterPasswordService> _logger;

    // 2026 Enterprise Security Standards
    private const int DefaultPasses = 3;

    // NSec EXPECTS KiB. 64 * 1024 KiB = 64 MB.
    // 64 MB is the libsodium "Moderate" limit.
    private const int DefaultMemorySizeKiB = 64 * 1024;

    // Using 1 for parallelism is the most stable setting for libsodium wrappers
    private const int DefaultParallelism = 1;

    private const int SaltSize = 16;
    private const int NonceSize = 12;
    private const int MinPasses = 1;
    private const int MaxPasses = 10;
    private const int MinMemorySizeKiB = 8;
    private const int MaxMemorySizeKiB = 256 * 1024;
    private const int V2MinPasses = DefaultPasses;
    private const int V2MinMemorySizeKiB = DefaultMemorySizeKiB;
    private const int MinParallelism = 1;
    private const int MaxParallelism = 1;
    private const int DekSize = 32;
    private const int AesGcmTagSize = 16;

    private static readonly byte[] V2AssociatedData =
        Encoding.UTF8.GetBytes(AesGcmWrappedKeyV2.AssociatedDataContext);

    public MasterPasswordService(ILogger<MasterPasswordService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<(byte[] WrappedDek, byte[] Salt, int Iterations, int MemorySize, byte[] Nonce)>
        WrapKeyAsync(byte[] rawDek, string password)
    {
        return await Task.Run(() =>
        {
            try
            {
                ValidateInputs(rawDek, password);

                _logger.LogDebug("Deriving KEK: Passes={Passes}, Mem={Mem}KiB, Parallelism={P}",
                    DefaultPasses, DefaultMemorySizeKiB, DefaultParallelism);

                byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
                byte[] nonce = RandomNumberGenerator.GetBytes(NonceSize);

                // 1. Setup Argon2id Parameters
                var argonParams = new Argon2Parameters
                {
                    NumberOfPasses = DefaultPasses,
                    MemorySize = DefaultMemorySizeKiB, // Verified: This must be KiB
                    DegreeOfParallelism = DefaultParallelism
                };

                // 2. Initialize Algorithm
                // This is where it was crashing due to the 64GB request
                var argon2 = PasswordBasedKeyDerivationAlgorithm.Argon2id(argonParams);
                byte[] passwordBytes = Encoding.UTF8.GetBytes(password);
                byte[]? kekBytes = null;

                try
                {
                    // 3. Derive the 256-bit KEK used by the AES-GCM wrapper.
                    kekBytes = argon2.DeriveBytes(passwordBytes, salt, VaultAesGcm.KeySize);

                    // 4. Wrap DEK
                    byte[] wrappedDek = VaultAesGcm.Encrypt(
                        kekBytes,
                        nonce,
                        default,
                        rawDek);

                    _logger.LogInformation("Key wrapping completed successfully.");

                    return (wrappedDek, salt, DefaultPasses, DefaultMemorySizeKiB, nonce);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(passwordBytes);
                    if (kekBytes is not null) CryptographicOperations.ZeroMemory(kekBytes);
                }
            }
            catch (Exception ex)
            {
                _logger.LogCritical(ex, "Argon2 Critical Failure. Parameters: Passes={P}, Mem={M}KiB, Par={Par}",
                    DefaultPasses, DefaultMemorySizeKiB, DefaultParallelism);
                throw;
            }
        });
    }

    public async Task<byte[]?> UnwrapKeyAsync(byte[] wrappedDek, string password, byte[] salt, int iterations, int memorySize, byte[] nonce)
    {
        return await Task.Run(() =>
        {
            try
            {
                if (!IsValidStoredParameters(wrappedDek, salt, iterations, memorySize, nonce))
                {
                    _logger.LogError(
                        "Stored key-wrap parameters rejected. Iterations={Iterations}, MemorySize={MemorySize}KiB, SaltLength={SaltLength}, NonceLength={NonceLength}, WrappedLength={WrappedLength}",
                        iterations,
                        memorySize,
                        salt?.Length ?? 0,
                        nonce?.Length ?? 0,
                        wrappedDek?.Length ?? 0);
                    return null;
                }

                var argonParams = new Argon2Parameters
                {
                    NumberOfPasses = iterations,
                    MemorySize = memorySize,
                    DegreeOfParallelism = DefaultParallelism
                };

                var argon2 = PasswordBasedKeyDerivationAlgorithm.Argon2id(argonParams);
                byte[] passwordBytes = Encoding.UTF8.GetBytes(password);
                byte[]? kekBytes = null;

                try
                {
                    kekBytes = argon2.DeriveBytes(passwordBytes, salt, VaultAesGcm.KeySize);
                    return VaultAesGcm.Decrypt(
                        kekBytes,
                        nonce,
                        default,
                        wrappedDek);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(passwordBytes);
                    if (kekBytes is not null) CryptographicOperations.ZeroMemory(kekBytes);
                }
            }
            catch (CryptographicException)
            {
                _logger.LogWarning("Unwrap failed: Authentication tag mismatch (Invalid Password).");
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected failure during UnwrapKeyAsync.");
                return null;
            }
        });
    }

    public Task<PasswordKeyWrapperV2> WrapKeyV2Async(
        byte[] rawDek,
        string password,
        CancellationToken cancellationToken = default)
    {
        if (rawDek is null || rawDek.Length != DekSize)
        {
            throw new ArgumentException($"DEK must be exactly {DekSize} bytes.", nameof(rawDek));
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            throw new ArgumentException("Password cannot be empty.", nameof(password));
        }

        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var salt = RandomNumberGenerator.GetBytes(SaltSize);
            var nonce = RandomNumberGenerator.GetBytes(NonceSize);
            var argonParams = new Argon2Parameters
            {
                NumberOfPasses = DefaultPasses,
                MemorySize = DefaultMemorySizeKiB,
                DegreeOfParallelism = DefaultParallelism
            };
            var argon2 = PasswordBasedKeyDerivationAlgorithm.Argon2id(argonParams);
            var passwordBytes = Encoding.UTF8.GetBytes(password);
            byte[]? kekBytes = null;

            try
            {
                kekBytes = argon2.DeriveBytes(passwordBytes, salt, VaultAesGcm.KeySize);
                cancellationToken.ThrowIfCancellationRequested();
                var ciphertext = VaultAesGcm.Encrypt(
                    kekBytes,
                    nonce,
                    V2AssociatedData,
                    rawDek);

                return new PasswordKeyWrapperV2
                {
                    Kdf = new Argon2idParametersV2
                    {
                        Salt = salt,
                        Passes = DefaultPasses,
                        MemoryKiB = DefaultMemorySizeKiB,
                        Parallelism = DefaultParallelism
                    },
                    WrappedKey = new AesGcmWrappedKeyV2
                    {
                        Nonce = nonce,
                        Ciphertext = ciphertext
                    }
                };
            }
            finally
            {
                CryptographicOperations.ZeroMemory(passwordBytes);
                if (kekBytes is not null) CryptographicOperations.ZeroMemory(kekBytes);
            }
        }, cancellationToken);
    }

    public Task<byte[]?> UnwrapKeyV2Async(
        PasswordKeyWrapperV2 wrapper,
        string password,
        CancellationToken cancellationToken = default)
    {
        if (!IsValidV2Wrapper(wrapper) || string.IsNullOrWhiteSpace(password))
        {
            return Task.FromResult<byte[]?>(null);
        }

        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var argonParams = new Argon2Parameters
            {
                NumberOfPasses = wrapper.Kdf.Passes,
                MemorySize = wrapper.Kdf.MemoryKiB,
                DegreeOfParallelism = wrapper.Kdf.Parallelism
            };
            var passwordBytes = Encoding.UTF8.GetBytes(password);
            byte[]? kekBytes = null;

            try
            {
                var argon2 = PasswordBasedKeyDerivationAlgorithm.Argon2id(argonParams);
                kekBytes = argon2.DeriveBytes(
                    passwordBytes,
                    wrapper.Kdf.Salt,
                    VaultAesGcm.KeySize);
                cancellationToken.ThrowIfCancellationRequested();
                return VaultAesGcm.Decrypt(
                    kekBytes,
                    wrapper.WrappedKey.Nonce,
                    V2AssociatedData,
                    wrapper.WrappedKey.Ciphertext);
            }
            catch (CryptographicException)
            {
                _logger.LogWarning("V2 password-wrapper authentication failed.");
                return null;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Unexpected failure while opening the v2 password wrapper.");
                return null;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(passwordBytes);
                if (kekBytes is not null) CryptographicOperations.ZeroMemory(kekBytes);
            }
        }, cancellationToken);
    }

    private static void ValidateInputs(byte[] rawDek, string password)
    {
        if (rawDek == null || rawDek.Length == 0)
            throw new ArgumentException("DEK cannot be empty.", nameof(rawDek));
        if (string.IsNullOrWhiteSpace(password))
            throw new ArgumentException("Password cannot be empty.", nameof(password));
    }

    private static bool IsValidStoredParameters(byte[] wrappedDek, byte[] salt, int iterations, int memorySize, byte[] nonce)
    {
        if (wrappedDek == null || salt == null || nonce == null)
        {
            return false;
        }

        if (iterations < MinPasses || iterations > MaxPasses)
        {
            return false;
        }

        if (memorySize < MinMemorySizeKiB || memorySize > MaxMemorySizeKiB)
        {
            return false;
        }

        if (salt.Length != SaltSize || nonce.Length != NonceSize)
        {
            return false;
        }

        return wrappedDek.Length >= VaultAesGcm.TagSize;
    }

    private static bool IsValidV2Wrapper(PasswordKeyWrapperV2? wrapper)
    {
        if (wrapper?.Kdf is null || wrapper.WrappedKey is null)
        {
            return false;
        }

        var kdf = wrapper.Kdf;
        var wrappedKey = wrapper.WrappedKey;
        return string.Equals(kdf.Algorithm, Argon2idParametersV2.AlgorithmIdentifier, StringComparison.Ordinal)
            && kdf.Version == Argon2idParametersV2.CurrentAlgorithmVersion
            && kdf.Salt is { Length: SaltSize }
            && kdf.Passes is >= V2MinPasses and <= MaxPasses
            && kdf.MemoryKiB is >= V2MinMemorySizeKiB and <= MaxMemorySizeKiB
            && kdf.Parallelism is >= MinParallelism and <= MaxParallelism
            && string.Equals(wrappedKey.Algorithm, AesGcmWrappedKeyV2.AlgorithmIdentifier, StringComparison.Ordinal)
            && wrappedKey.Nonce is { Length: NonceSize }
            && wrappedKey.Ciphertext is { Length: DekSize + AesGcmTagSize };
    }
}
