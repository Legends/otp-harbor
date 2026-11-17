using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Configuration;
using Moq;
using OtpHarbor.Testing.StoreUpgrade;
using TOTP.Core.Models;
using TOTP.Core.Security;
using TOTP.Core.Security.Models;
using TOTP.Core.Services.Interfaces;
using TOTP.DAL.Services;
using TOTP.Infrastructure.Security;
using TOTP.Infrastructure.Storage;
using TOTP.Infrastructure.Services;
using TOTP.Tests.Common;

namespace TOTP.Tests.Infrastructure.Storage;

public sealed class StoreUpgradeTestKitVerifierTests
{
    [Fact]
    public async Task V2TxtFixture_UsesLegacyColumnsAndPreservesAllAccountFields()
    {
        var path = Path.Combine(Path.GetTempPath(), "otp-harbor-v2-txt-" + Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            SyntheticV2Vault.WriteImportFixture(path);
            Assert.Equal("issuer|account_name|secret|id|period_seconds", File.ReadLines(path).First());
            var result = await new ExportService(NullLogger<ExportService>.Instance).ImportFromFileAsync(path);
            Assert.True(result.IsSuccess);
            Assert.Equal(4, result.Value.Count);
            var expected = UpgradeVerifier.FixtureAccounts();
            for (var index = 0; index < expected.Count; index++)
            {
                Assert.Equal(expected[index].Issuer, result.Value[index].Issuer);
                Assert.Equal(expected[index].AccountName, result.Value[index].AccountName);
                Assert.Equal(expected[index].Secret, result.Value[index].Secret);
                Assert.Equal(expected[index].PeriodSeconds, result.Value[index].PeriodSeconds);
                Assert.Equal(Guid.Parse($"00000000-0000-4000-8000-{index + 1:000000000000}"), result.Value[index].ID);
            }
        }
        finally { File.Delete(path); }
    }
    [Fact]
    public async Task GeneratedV2Fixture_MigratesThroughProductionDalAndPreservesAllAccounts()
    {
        var root = Path.Combine(Path.GetTempPath(), "otp-harbor-generated-v2-" + Guid.NewGuid().ToString("N"));
        var baseline = Path.Combine(root, "baseline");
        var current = Path.Combine(root, "current");
        Directory.CreateDirectory(root);
        byte[]? key = null;
        try
        {
            await SyntheticV2Vault.CreateAsync(baseline);
            var legacy = Path.Combine(baseline, "master.totp");
            var oldEnvelope = Path.Combine(baseline, "authorization-envelope.bin");
            await UpgradeVerifier.VerifyV2Async(legacy, oldEnvelope, SyntheticV2Vault.Password);
            var original = File.ReadAllBytes(legacy);
            await Assert.ThrowsAsync<InvalidDataException>(() => SyntheticV2Vault.CreateAsync(baseline));
            Assert.Equal(original, File.ReadAllBytes(legacy));
            var decoded = AuthorizationEnvelopeV2Codec.Deserialize(File.ReadAllBytes(oldEnvelope));
            Assert.True(decoded.IsSuccess);
            key = await new MasterPasswordService(NullLogger<MasterPasswordService>.Instance)
                .UnwrapKeyV2Async(decoded.Value.PasswordWrapper, SyntheticV2Vault.Password, TestContext.Current.CancellationToken);
            Assert.NotNull(key);
            Directory.CreateDirectory(current);
            foreach (var file in Directory.GetFiles(baseline)) File.Copy(file, Path.Combine(current, Path.GetFileName(file)));
            var vault = Path.Combine(current, "master.vault");
            var envelope = Path.Combine(current, "authorization-envelope.bin");
            var paths = new Mock<IPlatformApplicationPaths>();
            paths.SetupGet(value => value.VaultFilePath).Returns(vault);
            paths.SetupGet(value => value.AuthorizationEnvelopeFilePath).Returns(envelope);
            VaultFileNameMigration.MigrateIfUsingDefaultPath(new ConfigurationBuilder().Build(), paths.Object, NoOpPlatformFileSecurity.Instance);
            using var security = new SecurityContext();
            security.SetDek(key);
            using var dal = new AccountDAL(NullLogger<AccountDAL>.Instance, new VaultService(security), vault, NoOpPlatformFileSecurity.Instance, envelope);
            Assert.True((await dal.GetAllAsync()).IsSuccess);
            await UpgradeVerifier.VerifyV3Async(legacy, oldEnvelope, vault, envelope, SyntheticV2Vault.Password);
            UpgradeVerifier.AssertSameFile(Path.Combine(baseline, "master.totp.bak1"), Path.Combine(current, "pre-upgrade-rollback", "master.totp.bak1"));
            Assert.True((await dal.GetAllAsync()).IsSuccess);
            await UpgradeVerifier.VerifyV3Async(legacy, oldEnvelope, vault, envelope, SyntheticV2Vault.Password);
        }
        finally
        {
            if (key is not null) CryptographicOperations.ZeroMemory(key);
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task VerifyV2_AuthenticatesReleasedShapeWithoutModifyingFiles()
    {
        using var fixture = await Fixture.CreateAsync();
        var before = fixture.HashFiles();
        await UpgradeVerifier.VerifyV2Async(fixture.OldVault, fixture.OldEnvelope, Fixture.Password);
        Assert.Equal(before, fixture.HashFiles());
    }

    [Fact]
    public async Task VerifyV3_AcceptsPreservedAccountsAndReadableRollbackWithoutModifyingFiles()
    {
        using var fixture = await Fixture.CreateAsync();
        var before = fixture.HashFiles();
        await fixture.VerifyAsync();
        await fixture.VerifyAsync();
        Assert.Equal(before, fixture.HashFiles());
    }

    [Fact]
    public async Task VerifyV3_AcceptsProductionFilenameAndAuthenticatedVaultMigration()
    {
        using var fixture = await Fixture.CreateAsync(migrated: false);
        await fixture.MigrateAsync();
        var before = fixture.HashFiles();
        await fixture.VerifyAsync();
        Assert.Equal(before, fixture.HashFiles());
    }

    [Theory]
    [InlineData("id")]
    [InlineData("issuer")]
    [InlineData("name")]
    [InlineData("secret")]
    [InlineData("period")]
    [InlineData("algorithm")]
    [InlineData("digits")]
    [InlineData("favorite")]
    [InlineData("group")]
    public async Task VerifyV3_RejectsAChangedAccountField(string field)
    {
        using var fixture = await Fixture.CreateAsync();
        var original = fixture.Accounts[0];
        fixture.Accounts[0] = new Account(
            field == "id" ? Guid.NewGuid() : original.ID,
            field == "issuer" ? "Changed" : original.Issuer,
            field == "secret" ? "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ" : original.Secret,
            field == "name" ? "changed@example.invalid" : original.AccountName,
            field == "period" ? 60 : original.PeriodSeconds,
            isFavorite: field == "favorite",
            groups: field == "group" ? [new AccountGroup(Guid.NewGuid(), "Changed", "#4F6BED")] : [],
            algorithm: field == "algorithm" ? "SHA512" : original.Algorithm,
            digits: field == "digits" ? 8 : original.Digits);
        fixture.WriteV3();
        var before = fixture.HashFiles();
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.VerifyAsync());
        Assert.Equal(before, fixture.HashFiles());
    }

    [Theory]
    [InlineData("password")]
    [InlineData("envelope")]
    [InlineData("rollback")]
    [InlineData("legacy-header")]
    [InlineData("corrupt-vault")]
    [InlineData("missing-account")]
    public async Task VerifyV3_FailsSafelyWithoutRepairingInputs(string issue)
    {
        using var fixture = await Fixture.CreateAsync();
        if (issue == "envelope") File.WriteAllBytes(fixture.Envelope, [1, 2, 3]);
        if (issue == "rollback") File.WriteAllBytes(fixture.RollbackVault, [1, 2, 3]);
        if (issue == "legacy-header") File.Copy(fixture.OldVault, fixture.Vault, overwrite: true);
        if (issue == "corrupt-vault")
        {
            var bytes = File.ReadAllBytes(fixture.Vault);
            bytes[^1] ^= 1;
            File.WriteAllBytes(fixture.Vault, bytes);
        }
        if (issue == "missing-account") { fixture.Accounts.RemoveAt(0); fixture.WriteV3(); }
        var before = fixture.HashFiles();
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.VerifyAsync(
            issue == "password" ? "wrong-test-password" : Fixture.Password));
        Assert.Equal(before, fixture.HashFiles());
    }

    [Fact]
    public async Task VerifyV2_RejectsNonFixtureAccountsBeforeApprovingCapture()
    {
        using var fixture = await Fixture.CreateAsync(includeAdditionalAccount: true);
        await Assert.ThrowsAsync<InvalidDataException>(() => UpgradeVerifier.VerifyV2Async(
            fixture.OldVault, fixture.OldEnvelope, Fixture.Password));
    }

    [Fact]
    public async Task VerifyV2_RejectsOversizedInput()
    {
        using var fixture = await Fixture.CreateAsync();
        using (var file = File.OpenWrite(fixture.OldVault)) file.SetLength(16 * 1024 * 1024 + 1);
        await Assert.ThrowsAsync<InvalidDataException>(() => UpgradeVerifier.VerifyV2Async(
            fixture.OldVault, fixture.OldEnvelope, Fixture.Password));
    }

    private sealed class Fixture : IDisposable
    {
        public const string Password = "synthetic-store-upgrade-test-password";
        private readonly string _root = Path.Combine(Path.GetTempPath(), "otp-harbor-upgrade-verifier-" + Guid.NewGuid().ToString("N"));
        private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);
        public List<Account> Accounts { get; } = UpgradeVerifier.FixtureAccounts().Select(account =>
            new Account(Guid.NewGuid(), account.Issuer, account.Secret, account.AccountName, account.PeriodSeconds)).ToList();
        public string OldVault => Path.Combine(_root, "baseline", "master.totp");
        public string OldEnvelope => Path.Combine(_root, "baseline", "authorization-envelope.bin");
        public string Vault => Path.Combine(_root, "current", "master.vault");
        public string Envelope => Path.Combine(_root, "current", "authorization-envelope.bin");
        public string RollbackVault => Path.Combine(_root, "current", "pre-upgrade-rollback", "master.totp");

        public static async Task<Fixture> CreateAsync(bool includeAdditionalAccount = false, bool migrated = true)
        {
            var fixture = new Fixture();
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(fixture.OldVault)!);
                Directory.CreateDirectory(Path.GetDirectoryName(fixture.Vault)!);
                if (includeAdditionalAccount)
                    fixture.Accounts.Add(new Account(Guid.NewGuid(), "Unrelated synthetic account", "JBSWY3DPEHPK3PXP"));
                // Exact released v2.0.1 JSON shape: no algorithm/digits/group fields.
                var plain = JsonSerializer.SerializeToUtf8Bytes(fixture.Accounts.Select(account => new
                {
                    id = account.ID, issuer = account.Issuer, secret = account.Secret,
                    account_name = account.AccountName, period = account.PeriodSeconds
                }));
                try
                {
                    var nonce = RandomNumberGenerator.GetBytes(12);
                    var ciphertext = new byte[plain.Length];
                    var tag = new byte[16];
                    using var aes = new AesGcm(fixture._key, 16);
                    aes.Encrypt(nonce, plain, ciphertext, tag);
                    File.WriteAllBytes(fixture.OldVault, "TVLT"u8.ToArray().Concat(nonce).Concat(ciphertext).Concat(tag).ToArray());
                }
                finally { CryptographicOperations.ZeroMemory(plain); }
                var passwords = new MasterPasswordService(NullLogger<MasterPasswordService>.Instance);
                var wrapper = await passwords.WrapKeyV2Async(fixture._key, Password, TestContext.Current.CancellationToken);
                var envelope = AuthorizationEnvelopeV2Codec.Serialize(new AuthorizationEnvelopeV2 { PasswordWrapper = wrapper });
                Assert.True(envelope.IsSuccess);
                File.WriteAllBytes(fixture.OldEnvelope, envelope.Value);
                File.Copy(fixture.OldEnvelope, fixture.Envelope);
                if (migrated)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(fixture.RollbackVault)!);
                    File.Copy(fixture.OldVault, fixture.RollbackVault);
                    File.Copy(fixture.OldEnvelope, Path.Combine(Path.GetDirectoryName(fixture.RollbackVault)!, "authorization-envelope.bin"));
                    fixture.WriteV3();
                }
                else
                {
                    File.Copy(fixture.OldVault, Path.Combine(Path.GetDirectoryName(fixture.Vault)!, "master.totp"));
                }
                return fixture;
            }
            catch { fixture.Dispose(); throw; }
        }

        public void WriteV3()
        {
            using var security = new SecurityContext();
            security.SetDek(_key);
            File.WriteAllBytes(Vault, new VaultService(security).EncryptVault(Accounts));
        }

        public async Task MigrateAsync()
        {
            var paths = new Mock<IPlatformApplicationPaths>();
            paths.SetupGet(value => value.VaultFilePath).Returns(Vault);
            paths.SetupGet(value => value.AuthorizationEnvelopeFilePath).Returns(Envelope);
            VaultFileNameMigration.MigrateIfUsingDefaultPath(new ConfigurationBuilder().Build(),
                paths.Object, NoOpPlatformFileSecurity.Instance);
            using var security = new SecurityContext();
            security.SetDek(_key);
            using var dal = new AccountDAL(NullLogger<AccountDAL>.Instance, new VaultService(security),
                Vault, NoOpPlatformFileSecurity.Instance, Envelope);
            var result = await dal.GetAllAsync();
            Assert.True(result.IsSuccess);
            Assert.Equal(4, result.Value.Count);
        }

        public Task VerifyAsync(string password = Password) =>
            UpgradeVerifier.VerifyV3Async(OldVault, OldEnvelope, Vault, Envelope, password);

        public string[] HashFiles() => Directory.GetFiles(_root, "*", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => Path.GetRelativePath(_root, path) + ":" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))))
            .ToArray();

        public void Dispose()
        {
            CryptographicOperations.ZeroMemory(_key);
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
    }
}
