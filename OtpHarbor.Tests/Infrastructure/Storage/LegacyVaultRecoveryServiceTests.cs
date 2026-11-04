using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NSec.Cryptography;
using TOTP.Core.Models;
using TOTP.Core.Security;
using TOTP.Core.Security.Interfaces;
using TOTP.Core.Security.Models;
using TOTP.Core.Services.Interfaces;
using TOTP.Infrastructure.Security;
using TOTP.Infrastructure.Storage;
using TOTP.Tests.Common;

namespace TOTP.Tests.Infrastructure.Storage;

public sealed class LegacyVaultRecoveryServiceTests
{
    [Fact]
    public async Task ReadOlderAccountsAsync_WithCorrectPassword_RecoversAccountsWithoutChangingEitherVault()
    {
        using var fixture = await Fixture.CreateAsync();
        var currentBefore = File.ReadAllBytes(fixture.CurrentVaultPath);
        var olderBefore = File.ReadAllBytes(fixture.OlderVaultPath);

        var result = await fixture.Service.ReadOlderAccountsAsync(
            Fixture.Password, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(fixture.AccountId, Assert.Single(result.Value).ID);
        Assert.Equal(currentBefore, File.ReadAllBytes(fixture.CurrentVaultPath));
        Assert.Equal(olderBefore, File.ReadAllBytes(fixture.OlderVaultPath));
    }

    [Fact]
    public async Task ReadOlderAccountsAsync_WithWrongPassword_FailsWithoutChangingEitherVault()
    {
        using var fixture = await Fixture.CreateAsync();
        var currentBefore = File.ReadAllBytes(fixture.CurrentVaultPath);
        var olderBefore = File.ReadAllBytes(fixture.OlderVaultPath);

        var result = await fixture.Service.ReadOlderAccountsAsync(
            "wrong password", TestContext.Current.CancellationToken);

        Assert.True(result.IsFailed);
        Assert.Equal(currentBefore, File.ReadAllBytes(fixture.CurrentVaultPath));
        Assert.Equal(olderBefore, File.ReadAllBytes(fixture.OlderVaultPath));
    }

    [Fact]
    public async Task ReadOlderAccountsAsync_WithTamperedVaultOrMissingEnvelope_FailsClosed()
    {
        using var fixture = await Fixture.CreateAsync();
        var tampered = File.ReadAllBytes(fixture.OlderVaultPath);
        tampered[^1] ^= 0x40;
        File.WriteAllBytes(fixture.OlderVaultPath, tampered);

        Assert.True((await fixture.Service.ReadOlderAccountsAsync(
            Fixture.Password, TestContext.Current.CancellationToken)).IsFailed);

        File.Delete(fixture.OlderEnvelopePath);
        Assert.True((await fixture.Service.ReadOlderAccountsAsync(
            Fixture.Password, TestContext.Current.CancellationToken)).IsFailed);
        Assert.True(fixture.Service.HasConflict());
    }

    [Fact]
    public async Task ReadOlderAccountsAsync_WithoutCurrentVault_DoesNotOfferRecovery()
    {
        using var fixture = await Fixture.CreateAsync();
        File.Delete(fixture.CurrentVaultPath);

        Assert.False(fixture.Service.HasConflict());
        Assert.True((await fixture.Service.ReadOlderAccountsAsync(
            Fixture.Password, TestContext.Current.CancellationToken)).IsFailed);
        Assert.True(File.Exists(fixture.OlderVaultPath));
    }

    [Fact]
    public async Task ReadOlderAccountsAsync_WithUnsupportedOlderHeader_FailsWithoutRewritingFile()
    {
        using var fixture = await Fixture.CreateAsync();
        var unsupported = File.ReadAllBytes(fixture.OlderVaultPath);
        "TVL2"u8.CopyTo(unsupported);
        File.WriteAllBytes(fixture.OlderVaultPath, unsupported);

        var result = await fixture.Service.ReadOlderAccountsAsync(
            Fixture.Password, TestContext.Current.CancellationToken);

        Assert.True(result.IsFailed);
        Assert.Equal(unsupported, File.ReadAllBytes(fixture.OlderVaultPath));
    }

    [Fact]
    public async Task ReadOlderAccountsAsync_WithSymlinkedOlderVault_FailsClosed()
    {
        using var fixture = await Fixture.CreateAsync();
        var target = fixture.OlderVaultPath + ".original";
        File.Move(fixture.OlderVaultPath, target);
        File.CreateSymbolicLink(fixture.OlderVaultPath, target);

        var result = await fixture.Service.ReadOlderAccountsAsync(
            Fixture.Password, TestContext.Current.CancellationToken);

        Assert.True(result.IsFailed);
        Assert.True(File.Exists(target));
    }

    private sealed class Fixture : IDisposable
    {
        public const string Password = "synthetic-v2-master-password";
        private readonly string _root;

        private Fixture(string root, Guid accountId)
        {
            _root = root;
            AccountId = accountId;
            CurrentVaultPath = Path.Combine(root, "OTP-Harbor", "master.vault");
            OlderVaultPath = Path.Combine(root, "TOTP-Manager", "master.totp");
            OlderEnvelopePath = Path.Combine(root, "TOTP-Manager", "authorization-envelope.bin");
            Service = new LegacyVaultRecoveryService(
                CurrentVaultPath,
                OlderVaultPath,
                OlderEnvelopePath,
                NoOpPlatformFileSecurity.Instance,
                new MasterPasswordService(NullLogger<MasterPasswordService>.Instance),
                new VaultService(Mock.Of<ISecurityContext>()));
        }

        public Guid AccountId { get; }
        public string CurrentVaultPath { get; }
        public string OlderVaultPath { get; }
        public string OlderEnvelopePath { get; }
        public ILegacyVaultRecoveryService Service { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), $"otp-harbor-older-vault-{Guid.NewGuid():N}");
            var accountId = Guid.NewGuid();
            var fixture = new Fixture(root, accountId);
            Directory.CreateDirectory(Path.GetDirectoryName(fixture.CurrentVaultPath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(fixture.OlderVaultPath)!);
            var keyBytes = RandomNumberGenerator.GetBytes(32);
            var nonce = RandomNumberGenerator.GetBytes(AeadAlgorithm.Aes256Gcm.NonceSize);
            var plaintext = JsonSerializer.SerializeToUtf8Bytes(new[]
            {
                new
                {
                    id = accountId,
                    issuer = "Synthetic",
                    secret = "JBSWY3DPEHPK3PXP",
                    account_name = "test@example.invalid",
                    period = 30
                }
            });
            try
            {
                byte[] ciphertext;
                using (var key = Key.Import(AeadAlgorithm.Aes256Gcm, keyBytes, KeyBlobFormat.RawSymmetricKey))
                    ciphertext = AeadAlgorithm.Aes256Gcm.Encrypt(key, nonce, ReadOnlySpan<byte>.Empty, plaintext);
                var olderVault = "TVLT"u8.ToArray().Concat(nonce).Concat(ciphertext).ToArray();
                await File.WriteAllBytesAsync(fixture.OlderVaultPath, olderVault);
                var passwordService = new MasterPasswordService(NullLogger<MasterPasswordService>.Instance);
                var wrapper = await passwordService.WrapKeyV2Async(keyBytes, Password);
                var envelope = AuthorizationEnvelopeV2Codec.Serialize(new AuthorizationEnvelopeV2
                {
                    PasswordWrapper = wrapper
                });
                Assert.True(envelope.IsSuccess);
                await File.WriteAllBytesAsync(fixture.OlderEnvelopePath, envelope.Value);
                await File.WriteAllBytesAsync(fixture.CurrentVaultPath, "TVL2-current-encrypted-vault"u8.ToArray());
                CryptographicOperations.ZeroMemory(ciphertext);
                CryptographicOperations.ZeroMemory(olderVault);
                return fixture;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(keyBytes);
                CryptographicOperations.ZeroMemory(nonce);
                CryptographicOperations.ZeroMemory(plaintext);
            }
        }

        public void Dispose()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
    }
}
