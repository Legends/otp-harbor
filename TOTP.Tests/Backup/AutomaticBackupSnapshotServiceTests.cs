using System.Security.Cryptography;
using FluentResults;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TOTP.Core.Backup;
using TOTP.Core.Models;
using TOTP.Core.Security.Interfaces;
using TOTP.Core.Security.Models;
using TOTP.Infrastructure.Security;
using TOTP.Infrastructure.Services;
using TOTP.Tests.Common;
using TOTP.Tests.Security;

namespace TOTP.Tests.Backup;

public sealed class AutomaticBackupSnapshotServiceTests
{
    [Fact]
    public async Task CreateThenRead_V2Container_PreservesLinkedMultiGroupProfile()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var directory = CreateTemporaryDirectory();
        var key = RandomNumberGenerator.GetBytes(32);
        try
        {
            using var security = new SecurityContext();
            security.SetDek(key);
            var vaultService = new VaultService(security);
            var firstGroup = new AccountGroup(Guid.NewGuid(), "test1", "#4C956C");
            var secondGroup = new AccountGroup(Guid.NewGuid(), "test2", "#4F6BED");
            var account = new Account(
                Guid.NewGuid(),
                "Issuer",
                "JBSWY3DPEHPK3PXP",
                groups: [firstGroup, secondGroup]);
            var vaultPath = Path.Combine(directory, "vault.dat");
            await File.WriteAllBytesAsync(
                vaultPath,
                vaultService.EncryptVault([account]),
                cancellationToken);
            var store = new Mock<IAuthorizationEnvelopeStore>();
            store.Setup(value => value.LoadAsync(cancellationToken))
                .ReturnsAsync(Result.Ok<AuthorizationEnvelopeV2?>(CreateDeviceEnvelope()));
            var password = new Mock<IMasterPasswordService>();
            password.Setup(value => value.UnwrapKeyV2Async(
                    It.IsAny<PasswordKeyWrapperV2>(),
                    "correct password",
                    cancellationToken))
                .ReturnsAsync(() => key.ToArray());
            var sut = CreateService(vaultPath, store.Object, password.Object, vaultService);

            using var snapshot = (await sut.CreateAsync(cancellationToken)).Value;
            var package = AutomaticBackupPackageV2Codec.Deserialize(snapshot.Payload);
            await using var backupStream = new MemoryStream(snapshot.Payload.ToArray(), writable: false);
            var restored = await sut.ReadAccountsAsync(
                backupStream,
                "correct password",
                cancellationToken);

            Assert.True(package.IsSuccess);
            Assert.NotEqual(Guid.Empty, package.Value.BackupId);
            Assert.NotEmpty(package.Value.EncryptedAccounts);
            Assert.NotEmpty(package.Value.EncryptedProfile);
            Assert.Null(package.Value.AuthorizationEnvelope.QuickUnlockWrapper);
            Assert.True(restored.IsSuccess);
            var restoredAccount = Assert.Single(restored.Value);
            Assert.Equal(
                new[] { firstGroup.Id, secondGroup.Id },
                restoredAccount.Groups.Select(group => group.Id));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task CreateAsync_RemovesDeviceQuickUnlockWrapperFromPortableSnapshot()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var directory = CreateTemporaryDirectory();
        try
        {
            var vaultPath = Path.Combine(directory, "vault.dat");
            var vault = new byte[] { 1, 3, 5, 7 };
            await File.WriteAllBytesAsync(vaultPath, vault, cancellationToken);
            var store = new Mock<IAuthorizationEnvelopeStore>();
            store.Setup(value => value.LoadAsync(cancellationToken))
                .ReturnsAsync(Result.Ok<AuthorizationEnvelopeV2?>(CreateDeviceEnvelope()));
            var sut = CreateService(vaultPath, store.Object, new Mock<IMasterPasswordService>().Object, new CapturingVaultReader([]));

            using var snapshot = (await sut.CreateAsync(cancellationToken)).Value;
            var decoded = AutomaticBackupPackageV1Codec.Deserialize(snapshot.Payload);

            Assert.True(decoded.IsSuccess);
            Assert.Equal(vault, decoded.Value.EncryptedVault);
            Assert.Null(decoded.Value.AuthorizationEnvelope.QuickUnlockWrapper);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ReadAccountsAsync_CorrectMasterPassword_UsesIndependentBackupKey()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var package = CreatePortablePackage();
        var encoded = AutomaticBackupPackageV1Codec.Serialize(package).Value;
        var expectedAccounts = new[]
        {
            new Account(Guid.NewGuid(), "Issuer", "JBSWY3DPEHPK3PXP")
        };
        var password = new Mock<IMasterPasswordService>();
        password.Setup(value => value.UnwrapKeyV2Async(
                It.IsAny<PasswordKeyWrapperV2>(),
                "correct password",
                cancellationToken))
            .ReturnsAsync([7, 8, 9]);
        var reader = new CapturingVaultReader(expectedAccounts);
        var temporaryVaultPath = Path.GetTempFileName();
        var sut = CreateService(temporaryVaultPath, Mock.Of<IAuthorizationEnvelopeStore>(), password.Object, reader);
        try
        {
            await using var stream = new MemoryStream(encoded, writable: false);

            var result = await sut.ReadAccountsAsync(stream, "correct password", cancellationToken);

            Assert.True(result.IsSuccess);
            Assert.Equal(expectedAccounts, result.Value);
            Assert.Equal(package.EncryptedVault, reader.EncryptedVault);
            Assert.Equal(new byte[] { 7, 8, 9 }, reader.VaultKey);
        }
        finally
        {
            File.Delete(temporaryVaultPath);
        }
    }

    [Fact]
    public async Task ReadAccountsAsync_WrongMasterPassword_DoesNotAttemptVaultDecryption()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var encoded = AutomaticBackupPackageV1Codec.Serialize(CreatePortablePackage()).Value;
        var password = new Mock<IMasterPasswordService>();
        password.Setup(value => value.UnwrapKeyV2Async(
                It.IsAny<PasswordKeyWrapperV2>(),
                It.IsAny<string>(),
                cancellationToken))
            .ReturnsAsync((byte[]?)null);
        var reader = new CapturingVaultReader([]);
        var temporaryVaultPath = Path.GetTempFileName();
        var sut = CreateService(temporaryVaultPath, Mock.Of<IAuthorizationEnvelopeStore>(), password.Object, reader);
        try
        {
            await using var stream = new MemoryStream(encoded, writable: false);

            var result = await sut.ReadAccountsAsync(stream, "wrong password", cancellationToken);

            Assert.True(result.IsFailed);
            Assert.Equal(AutomaticBackupErrorCode.InvalidPassword, ErrorCode(result.Errors));
            Assert.False(reader.WasCalled);
        }
        finally
        {
            File.Delete(temporaryVaultPath);
        }
    }

    private static AutomaticBackupSnapshotService CreateService(
        string vaultPath,
        IAuthorizationEnvelopeStore store,
        IMasterPasswordService password,
        IVaultSnapshotReader reader) => new(
        vaultPath,
        store,
        password,
        reader,
        NoOpPlatformFileSecurity.Instance,
        TimeProvider.System,
        NullLogger<AutomaticBackupSnapshotService>.Instance);

    private static AutomaticBackupPackageV1 CreatePortablePackage()
    {
        var vault = new byte[] { 2, 4, 6, 8 };
        return new AutomaticBackupPackageV1
        {
            CreatedUtc = DateTimeOffset.UtcNow,
            VaultSha256 = Convert.ToHexString(SHA256.HashData(vault)),
            EncryptedVault = vault,
            AuthorizationEnvelope = AuthorizationEnvelopeV2CodecTests.CreateEnvelope()
        };
    }

    private static AuthorizationEnvelopeV2 CreateDeviceEnvelope() =>
        AuthorizationEnvelopeV2CodecTests.CreateEnvelope() with
        {
            QuickUnlockWrapper = new PlatformQuickUnlockWrapperV2
            {
                Provider = "test-device",
                ProviderVersion = 1,
                AuthenticationPolicy = "user-verification-required",
                KeyReference = "device-only-reference",
                WrappedKey = new PlatformWrappedKeyV2
                {
                    Algorithm = "test",
                    Ciphertext = new byte[32]
                }
            }
        };

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"otp-harbor-auto-backup-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static AutomaticBackupErrorCode ErrorCode(IEnumerable<FluentResults.IError> errors) =>
        Assert.IsType<AutomaticBackupError>(Assert.Single(errors)).Code;

    private sealed class CapturingVaultReader(IReadOnlyList<Account> accounts) : IVaultSnapshotReader
    {
        public byte[]? EncryptedVault { get; private set; }
        public byte[]? VaultKey { get; private set; }
        public bool WasCalled { get; private set; }

        public Result<IReadOnlyList<Account>> ReadAccounts(
            ReadOnlySpan<byte> encryptedVault,
            ReadOnlySpan<byte> vaultKey)
        {
            WasCalled = true;
            EncryptedVault = encryptedVault.ToArray();
            VaultKey = vaultKey.ToArray();
            return Result.Ok(accounts);
        }
    }
}
