using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using NSec.Cryptography;
using TOTP.Core.Security.Models;
using TOTP.Infrastructure.Security;

namespace TOTP.Tests.Security;

public sealed class MasterPasswordServiceTests
{
    private readonly MasterPasswordService _sut = new(NullLogger<MasterPasswordService>.Instance);

    [Fact]
    public async Task WrapKeyAsync_ThenUnwrapKeyAsync_WithValidParameters_RoundTripsDek()
    {
        var dek = new byte[32];
        Random.Shared.NextBytes(dek);

        var wrapped = await _sut.WrapKeyAsync(dek, "correct-password");
        var unwrapped = await _sut.UnwrapKeyAsync(
            wrapped.WrappedDek,
            "correct-password",
            wrapped.Salt,
            wrapped.Iterations,
            wrapped.MemorySize,
            wrapped.Nonce);

        Assert.NotNull(unwrapped);
        Assert.Equal(dek, unwrapped);
    }

    [Fact]
    public async Task UnwrapKeyAsync_WhenStoredMemorySizeTooHigh_ReturnsNull()
    {
        var dek = new byte[32];
        Random.Shared.NextBytes(dek);

        var wrapped = await _sut.WrapKeyAsync(dek, "correct-password");
        var unwrapped = await _sut.UnwrapKeyAsync(
            wrapped.WrappedDek,
            "correct-password",
            wrapped.Salt,
            wrapped.Iterations,
            300 * 1024,
            wrapped.Nonce);

        Assert.Null(unwrapped);
    }

    [Fact]
    public async Task UnwrapKeyAsync_WhenStoredIterationsTooHigh_ReturnsNull()
    {
        var dek = new byte[32];
        Random.Shared.NextBytes(dek);

        var wrapped = await _sut.WrapKeyAsync(dek, "correct-password");
        var unwrapped = await _sut.UnwrapKeyAsync(
            wrapped.WrappedDek,
            "correct-password",
            wrapped.Salt,
            99,
            wrapped.MemorySize,
            wrapped.Nonce);

        Assert.Null(unwrapped);
    }

    [Fact]
    public async Task WrapKeyV2Async_ThenUnwrapKeyV2Async_PreservesParametersAndRoundTripsDek()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var dek = RandomNumberGenerator.GetBytes(32);
        byte[]? unwrapped = null;

        try
        {
            var wrapper = await _sut.WrapKeyV2Async(dek, "correct-password", cancellationToken);

            Assert.Equal(Argon2idParametersV2.AlgorithmIdentifier, wrapper.Kdf.Algorithm);
            Assert.Equal(Argon2idParametersV2.CurrentAlgorithmVersion, wrapper.Kdf.Version);
            Assert.Equal(16, wrapper.Kdf.Salt.Length);
            Assert.Equal(3, wrapper.Kdf.Passes);
            Assert.Equal(65_536, wrapper.Kdf.MemoryKiB);
            Assert.Equal(1, wrapper.Kdf.Parallelism);
            Assert.Equal(AesGcmWrappedKeyV2.AlgorithmIdentifier, wrapper.WrappedKey.Algorithm);
            Assert.Equal(12, wrapper.WrappedKey.Nonce.Length);
            Assert.Equal(48, wrapper.WrappedKey.Ciphertext.Length);

            unwrapped = await _sut.UnwrapKeyV2Async(wrapper, "correct-password", cancellationToken);

            Assert.NotNull(unwrapped);
            Assert.Equal(dek, unwrapped);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dek);
            if (unwrapped is not null)
            {
                CryptographicOperations.ZeroMemory(unwrapped);
            }
        }
    }

    [Fact]
    public async Task UnwrapKeyV2Async_WhenWrappedByNsecAesGcm_RemainsCompatible()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var dek = RandomNumberGenerator.GetBytes(32);
        var salt = RandomNumberGenerator.GetBytes(16);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var passwordBytes = Encoding.UTF8.GetBytes("existing-vault-password");
        byte[]? ciphertext = null;
        byte[]? unwrapped = null;

        try
        {
            var parameters = new Argon2Parameters
            {
                NumberOfPasses = 3,
                MemorySize = 64 * 1024,
                DegreeOfParallelism = 1
            };
            var derivation = PasswordBasedKeyDerivationAlgorithm.Argon2id(parameters);
            var algorithm = AeadAlgorithm.Aes256Gcm;
            using var key = derivation.DeriveKey(passwordBytes, salt, algorithm);
            ciphertext = algorithm.Encrypt(
                key,
                nonce,
                Encoding.UTF8.GetBytes(AesGcmWrappedKeyV2.AssociatedDataContext),
                dek);
            var existingWrapper = new PasswordKeyWrapperV2
            {
                Kdf = new Argon2idParametersV2
                {
                    Salt = salt,
                    Passes = 3,
                    MemoryKiB = 64 * 1024,
                    Parallelism = 1
                },
                WrappedKey = new AesGcmWrappedKeyV2
                {
                    Nonce = nonce,
                    Ciphertext = ciphertext
                }
            };

            unwrapped = await _sut.UnwrapKeyV2Async(
                existingWrapper,
                "existing-vault-password",
                cancellationToken);

            Assert.Equal(dek, unwrapped);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dek);
            CryptographicOperations.ZeroMemory(passwordBytes);
            if (ciphertext is not null) CryptographicOperations.ZeroMemory(ciphertext);
            if (unwrapped is not null) CryptographicOperations.ZeroMemory(unwrapped);
        }
    }

    [Fact]
    public async Task UnwrapKeyV2Async_WhenPasswordIsWrong_ReturnsNull()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var dek = RandomNumberGenerator.GetBytes(32);
        try
        {
            var wrapper = await _sut.WrapKeyV2Async(dek, "correct-password", cancellationToken);

            var unwrapped = await _sut.UnwrapKeyV2Async(wrapper, "wrong-password", cancellationToken);

            Assert.Null(unwrapped);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dek);
        }
    }

    [Fact]
    public async Task UnwrapKeyV2Async_WhenCiphertextIsTampered_ReturnsNull()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var dek = RandomNumberGenerator.GetBytes(32);
        try
        {
            var wrapper = await _sut.WrapKeyV2Async(dek, "correct-password", cancellationToken);
            var tamperedCiphertext = wrapper.WrappedKey.Ciphertext.ToArray();
            tamperedCiphertext[0] ^= 0x80;
            var tampered = wrapper with
            {
                WrappedKey = wrapper.WrappedKey with { Ciphertext = tamperedCiphertext }
            };

            var unwrapped = await _sut.UnwrapKeyV2Async(tampered, "correct-password", cancellationToken);

            Assert.Null(unwrapped);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dek);
        }
    }

    [Fact]
    public async Task UnwrapKeyV2Async_WhenOpenedWithoutV2AssociatedData_ReturnsNull()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var dek = RandomNumberGenerator.GetBytes(32);
        try
        {
            var wrapper = await _sut.WrapKeyV2Async(dek, "correct-password", cancellationToken);

            var unwrapped = await _sut.UnwrapKeyAsync(
                wrapper.WrappedKey.Ciphertext,
                "correct-password",
                wrapper.Kdf.Salt,
                wrapper.Kdf.Passes,
                wrapper.Kdf.MemoryKiB,
                wrapper.WrappedKey.Nonce);

            Assert.Null(unwrapped);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dek);
        }
    }

    [Fact]
    public async Task UnwrapKeyV2Async_WhenPersistedParallelismIsChanged_ReturnsNull()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var dek = RandomNumberGenerator.GetBytes(32);
        try
        {
            var wrapper = await _sut.WrapKeyV2Async(dek, "correct-password", cancellationToken);
            var altered = wrapper with
            {
                Kdf = wrapper.Kdf with { Parallelism = 2 }
            };

            var unwrapped = await _sut.UnwrapKeyV2Async(altered, "correct-password", cancellationToken);

            Assert.Null(unwrapped);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dek);
        }
    }

    [Fact]
    public async Task UnwrapKeyV2Async_WhenStoredParametersAreUnsupported_ReturnsNullBeforeDerivation()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var wrapper = new PasswordKeyWrapperV2
        {
            Kdf = new Argon2idParametersV2
            {
                Salt = new byte[16],
                Passes = 3,
                MemoryKiB = 256 * 1024 + 1,
                Parallelism = 1
            },
            WrappedKey = new AesGcmWrappedKeyV2
            {
                Nonce = new byte[12],
                Ciphertext = new byte[48]
            }
        };

        var unwrapped = await _sut.UnwrapKeyV2Async(wrapper, "correct-password", cancellationToken);

        Assert.Null(unwrapped);
    }
}
