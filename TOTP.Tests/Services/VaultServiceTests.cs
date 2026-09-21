using Moq;
using System.Linq;
using System.Security.Cryptography;
using TOTP.Core.Models;
using TOTP.Core.Security.Interfaces;
using TOTP.Core.Security.Models;
using TOTP.Infrastructure.Security;

namespace TOTP.Tests.Services;

public sealed class VaultServiceTests
{
    [Fact]
    public void EncryptVault_WhenSecurityContextLocked_ThrowsInvalidOperationException()
    {
        var security = new Mock<ISecurityContext>();
        security.SetupGet(s => s.IsUnlocked).Returns(false);
        var sut = new VaultService(security.Object);

        Assert.Throws<InvalidOperationException>(() => { sut.EncryptVault([]); });
    }

    [Fact]
    public void DecryptVault_WhenSecurityContextLocked_ThrowsInvalidOperationException()
    {
        var security = new Mock<ISecurityContext>();
        security.SetupGet(s => s.IsUnlocked).Returns(false);
        var sut = new VaultService(security.Object);

        Assert.Throws<InvalidOperationException>(() => { sut.DecryptVault([1, 2, 3]); });
    }

    [Fact]
    public void EncryptThenDecryptVault_WithSameDek_ReturnsOriginalEntries()
    {
        var dek = RandomNumberGenerator.GetBytes(32);
        var security = new Mock<ISecurityContext>();
        security.SetupGet(s => s.IsUnlocked).Returns(true);
        security.Setup(s => s.GetDekCopy()).Returns(() => (byte[])dek.Clone());
        var sut = new VaultService(security.Object);
        var group = new AccountGroup(Guid.NewGuid(), "Work", "#4F6BED");
        List<Account> input =
        [
            new(Guid.NewGuid(), "GitHub", "AAAA", "john", 60, group),
            new(Guid.NewGuid(), "Google", "BBBB")
        ];

        var blob = sut.EncryptVault(input);
        var output = sut.DecryptVault(blob);

        Assert.Equal(2, output.Count);
        Assert.Equal(input[0].ID, output[0].ID);
        Assert.Equal(input[0].Issuer, output[0].Issuer);
        Assert.Equal(input[0].Secret, output[0].Secret);
        Assert.Equal(input[0].AccountName, output[0].AccountName);
        Assert.Equal(60, output[0].PeriodSeconds);
        Assert.Equal(group, output[0].Group);
    }

    [Fact]
    public void DecryptVault_WhenBlobTooSmall_ThrowsCryptographicException()
    {
        var dek = RandomNumberGenerator.GetBytes(32);
        var security = new Mock<ISecurityContext>();
        security.SetupGet(s => s.IsUnlocked).Returns(true);
        security.Setup(s => s.GetDekCopy()).Returns(() => (byte[])dek.Clone());
        var sut = new VaultService(security.Object);

        Assert.Throws<CryptographicException>(() => { sut.DecryptVault([1, 2, 3]); });
    }

    [Fact]
    public void DecryptVault_WhenHeaderInvalid_ThrowsCryptographicException()
    {
        var dek = RandomNumberGenerator.GetBytes(32);
        var security = new Mock<ISecurityContext>();
        security.SetupGet(s => s.IsUnlocked).Returns(true);
        security.Setup(s => s.GetDekCopy()).Returns(() => (byte[])dek.Clone());
        var sut = new VaultService(security.Object);
        var wrongHeader = "XXXX"u8.ToArray();
        var nonce = RandomNumberGenerator.GetBytes(12);
        var blob = wrongHeader.Concat(nonce).Concat(new byte[] { 1, 2, 3 }).ToArray();

        Assert.Throws<CryptographicException>(() => { sut.DecryptVault(blob); });
    }

    [Fact]
    public void DecryptVault_WhenCiphertextTampered_ThrowsCryptographicException()
    {
        var dek = RandomNumberGenerator.GetBytes(32);
        var security = new Mock<ISecurityContext>();
        security.SetupGet(s => s.IsUnlocked).Returns(true);
        security.Setup(s => s.GetDekCopy()).Returns(() => (byte[])dek.Clone());
        var sut = new VaultService(security.Object);
        var blob = sut.EncryptVault([new Account(Guid.NewGuid(), "GitHub", "SECRET")]);

        blob[^1] ^= 0xFF;

        Assert.Throws<CryptographicException>(() => { sut.DecryptVault(blob); });
    }

    [Fact]
    public void Verify_WithMatchingCandidateKey_AuthenticatesVaultWithoutUnlockingContext()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        using var security = new SecurityContext();
        security.SetDek(key);
        var sut = new VaultService(security);
        var blob = sut.EncryptVault([new Account(Guid.NewGuid(), "Synthetic", "TESTSECRET")]);
        var keySnapshot = (byte[])key.Clone();
        var blobSnapshot = (byte[])blob.Clone();
        security.Lock();

        var result = sut.Verify(blob, key);

        Assert.Equal(VaultKeyVerificationStatus.Verified, result);
        Assert.False(security.IsUnlocked);
        Assert.Equal(keySnapshot, key);
        Assert.Equal(blobSnapshot, blob);
    }

    [Fact]
    public void Verify_WithWrongCandidateKey_ReturnsAuthenticationFailed()
    {
        var encryptionKey = RandomNumberGenerator.GetBytes(32);
        using var security = new SecurityContext();
        security.SetDek(encryptionKey);
        var sut = new VaultService(security);
        var blob = sut.EncryptVault([]);
        security.Lock();

        var result = sut.Verify(blob, RandomNumberGenerator.GetBytes(32));

        Assert.Equal(VaultKeyVerificationStatus.AuthenticationFailed, result);
        Assert.False(security.IsUnlocked);
    }

    [Fact]
    public void Verify_WithTamperedCiphertext_ReturnsAuthenticationFailed()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        using var security = new SecurityContext();
        security.SetDek(key);
        var sut = new VaultService(security);
        var blob = sut.EncryptVault([]);
        security.Lock();
        blob[^1] ^= 0xFF;

        var result = sut.Verify(blob, key);

        Assert.Equal(VaultKeyVerificationStatus.AuthenticationFailed, result);
        Assert.False(security.IsUnlocked);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(31)]
    [InlineData(33)]
    public void Verify_WithInvalidCandidateKeyLength_FailsClosed(int keyLength)
    {
        var sut = new VaultService(Mock.Of<ISecurityContext>());

        var result = sut.Verify(new byte[64], new byte[keyLength]);

        Assert.Equal(VaultKeyVerificationStatus.InvalidCandidateKey, result);
    }

    [Fact]
    public void Verify_WithInvalidVaultFraming_ReturnsInvalidVaultFormat()
    {
        var sut = new VaultService(Mock.Of<ISecurityContext>());

        var result = sut.Verify(new byte[64], new byte[32]);

        Assert.Equal(VaultKeyVerificationStatus.InvalidVaultFormat, result);
    }
}
