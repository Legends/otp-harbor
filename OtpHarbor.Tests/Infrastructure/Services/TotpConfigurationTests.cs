using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentResults;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OtpNet;
using TOTP.Core.Models;
using TOTP.Core.Services.Interfaces;
using TOTP.Infrastructure.Parser;
using TOTP.Infrastructure.Security;
using TOTP.Infrastructure.Services;

namespace TOTP.Tests.Infrastructure.Services;

public sealed class TotpConfigurationTests
{
    private const string Secret = "JBSWY3DPEHPK3PXP"; // Synthetic fixture.
    public static TheoryData<string, int> Configurations => new()
    {
        { "SHA1", 6 }, { "SHA1", 8 }, { "SHA256", 6 },
        { "SHA256", 8 }, { "SHA512", 6 }, { "SHA512", 8 }
    };

    // RFC 6238 Appendix B, with the algorithm-specific ASCII secrets from Appendix A.
    public static IEnumerable<object[]> RfcVectors()
    {
        (long Time, string Sha1, string Sha256, string Sha512)[] vectors =
        [
            (59, "94287082", "46119246", "90693936"),
            (1111111109, "07081804", "68084774", "25091201"),
            (1111111111, "14050471", "67062674", "99943326"),
            (1234567890, "89005924", "91819424", "93441116"),
            (2000000000, "69279037", "90698825", "38618901"),
            (20000000000, "65353130", "77737706", "47863826")
        ];
        foreach (var vector in vectors)
        foreach (var (algorithm, length, expected) in new[]
        {
            ("SHA1", 20, vector.Sha1), ("SHA256", 32, vector.Sha256), ("SHA512", 64, vector.Sha512)
        })
        foreach (var digits in new[] { 6, 8 })
            yield return [vector.Time, algorithm, length, digits, expected[^digits..]];
    }

    [Theory]
    [MemberData(nameof(RfcVectors))]
    public void Generate_MatchesOfficialVectors(long time, string algorithm, int length, int digits, string expected)
    {
        var bytes = Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat("1234567890", 7))[..length]);
        var secret = Base32Encoding.ToString(bytes);
        var clock = new FixedClock(time);
        var result = new OtpNetTotpGenerator(clock).Generate(secret, 30, algorithm, digits);
        Assert.Equal(expected, result.Code);
        Assert.Equal(digits, result.Code.Length);
        Assert.Equal(30 - time % 30, result.RemainingSeconds);
        var next = new OtpNetTotpGenerator(new FixedClock(time + result.RemainingSeconds))
            .Generate(secret, 30, algorithm, digits);
        Assert.Equal(next.Code, result.NextCode);
    }

    [Theory]
    [InlineData("SHA384", 6)]
    [InlineData("MD5", 8)]
    [InlineData("", 6)]
    [InlineData(null, 6)]
    [InlineData("SHA1", 0)]
    [InlineData("SHA256", 7)]
    [InlineData("SHA512", 9)]
    public void Generate_RejectsUnsupportedConfiguration(string? algorithm, int digits)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new OtpNetTotpGenerator().Generate(Secret, 30, algorithm!, digits));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new QrCodeService().BuildOtpAuthUri("Example", Secret, "alice", 30, algorithm!, digits));
    }

    [Theory]
    [MemberData(nameof(Configurations))]
    public async Task UriAndQrImport_PreserveConfiguration(string algorithm, int digits)
    {
        var uri = new QrCodeService().BuildOtpAuthUri("Example", Secret, "alice", 60, algorithm, digits);
        var parsed = OtpauthParser.Parse(uri);
        Assert.Equal(algorithm, parsed.Algorithm);
        Assert.Equal(digits, parsed.Digits);
        var manager = new Mock<IAccountManager>();
        manager.Setup(value => value.GetAllOtpEntriesSortedAsync()).ReturnsAsync(Result.Ok<IReadOnlyList<Account>>([]));
        Account? saved = null;
        manager.Setup(value => value.AddNewAsync(It.IsAny<Account>()))
            .Callback<Account>(account => saved = account).ReturnsAsync(Result.Ok());
        var outcome = await new QrAccountImportService(manager.Object).ImportAsync(uri,
            (_, _) => Task.FromResult(TOTP.Core.Services.Models.QrAccountConflictDecision.Cancel),
            TestContext.Current.CancellationToken);
        Assert.True(outcome.IsSuccess);
        AssertConfiguration(saved!, algorithm, digits);
        Assert.Equal(60, saved!.PeriodSeconds);
    }

    [Theory]
    [MemberData(nameof(Configurations))]
    public async Task AllNativeExports_AndEncryptedBackups_RoundTrip(string algorithm, int digits)
    {
        var source = new Account(Guid.NewGuid(), "Example", Secret, "alice", 60, algorithm: algorithm, digits: digits);
        var service = new ExportService(NullLogger<ExportService>.Instance);
        foreach (var format in new[] { ExportFileFormat.Json, ExportFileFormat.Csv, ExportFileFormat.Txt })
        {
            await using var stream = new MemoryStream();
            Assert.True((await service.ExportToStreamAsync([source], stream, format, TestContext.Current.CancellationToken)).IsSuccess);
            stream.Position = 0;
            var restored = await service.ImportFromStreamAsync(stream, "fixture." + format.ToString().ToLowerInvariant(), cancellationToken: TestContext.Current.CancellationToken);
            Assert.True(restored.IsSuccess);
            AssertConfiguration(Assert.Single(restored.Value), algorithm, digits);
        }
        await using var backup = new MemoryStream();
        Assert.True((await service.ExportToEncryptedStreamAsync([source], "synthetic test password", backup, ExportFileFormat.Json, TestContext.Current.CancellationToken)).IsSuccess);
        backup.Position = 0;
        var recovered = await service.ImportFromStreamAsync(backup, "fixture.otphbp", "synthetic test password", TestContext.Current.CancellationToken);
        Assert.True(recovered.IsSuccess);
        AssertConfiguration(Assert.Single(recovered.Value), algorithm, digits);
    }

    [Theory]
    [MemberData(nameof(Configurations))]
    public void VaultAndAccountCopies_PreserveConfigurationAndSecret(string algorithm, int digits)
    {
        var key = RandomNumberGenerator.GetBytes(32);
        using var security = new SecurityContext();
        try
        {
            security.SetDek(key);
            var service = new VaultService(security);
            var source = new Account(Guid.NewGuid(), "Example", Secret, algorithm: algorithm, digits: digits);
            var copy = source.WithFavorite(true).WithGroup(new AccountGroup(Guid.NewGuid(), "Work", "#4F6BED"));
            var encrypted = service.EncryptVault([copy]);
            Assert.DoesNotContain(Secret, Encoding.UTF8.GetString(encrypted));
            var restored = Assert.Single(service.DecryptVault(encrypted));
            AssertConfiguration(restored, algorithm, digits);
            Assert.True(restored.IsFavorite);
            Assert.NotNull(restored.Group);
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    [Fact]
    public void LegacyJsonAndUri_DefaultOnlyMissingMetadata()
    {
        var account = JsonSerializer.Deserialize<Account>("""
            {"issuer":"Example","secret":"JBSWY3DPEHPK3PXP"}
            """);
        AssertConfiguration(account!, "SHA1", 6);
        var parsed = OtpauthParser.Parse("otpauth://totp/Example?secret=" + Secret);
        Assert.Equal("SHA1", parsed.Algorithm);
        Assert.Equal(6, parsed.Digits);
    }

    [Fact]
    public void LegacyCurrentVault_WithoutConfigurationMetadata_RetainsDefaultsAndSecret()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        using var security = new SecurityContext();
        security.SetDek(key);
        var revision = Guid.NewGuid();
        var accounts = JsonSerializer.SerializeToUtf8Bytes(new
        {
            RevisionId = revision,
            Accounts = new[] { new { Id = Guid.NewGuid(), Issuer = "Example", Secret, AccountName = "alice", PeriodSeconds = 60, IsFavorite = false } }
        });
        var profile = JsonSerializer.SerializeToUtf8Bytes(new { Version = 1, RevisionId = revision, Groups = Array.Empty<object>() });
        try
        {
            var accountsNonce = RandomNumberGenerator.GetBytes(VaultAesGcm.NonceSize);
            var profileNonce = RandomNumberGenerator.GetBytes(VaultAesGcm.NonceSize);
            var encryptedAccounts = accountsNonce.Concat(VaultAesGcm.Encrypt(key, accountsNonce, "TVL2:accounts"u8, accounts)).ToArray();
            var encryptedProfile = profileNonce.Concat(VaultAesGcm.Encrypt(key, profileNonce, "TVL2:profile"u8, profile)).ToArray();
            var blob = VaultService.CombineEncryptedVault(encryptedAccounts, encryptedProfile);
            var service = new VaultService(security);
            var account = Assert.Single(service.DecryptVault(blob));
            AssertConfiguration(account, "SHA1", 6);
            Assert.Equal(60, account.PeriodSeconds);
            var rewritten = Assert.Single(service.DecryptVault(service.EncryptVault([account])));
            AssertConfiguration(rewritten, "SHA1", 6);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(accounts);
            CryptographicOperations.ZeroMemory(profile);
        }
    }

    [Theory]
    [InlineData("\"algorithm\":null,\"digits\":6")]
    [InlineData("\"algorithm\":\"\",\"digits\":6")]
    [InlineData("\"algorithm\":\"SHA384\",\"digits\":8")]
    [InlineData("\"algorithm\":\"SHA512\",\"digits\":7")]
    [InlineData("\"digits\":0")]
    public async Task JsonImport_RejectsExplicitUnsupportedMetadata(string metadata)
    {
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(
            "[{\"issuer\":\"Example\",\"secret\":\"" + Secret + "\"," + metadata + "}]"));
        var result = await new ExportService(NullLogger<ExportService>.Instance)
            .ImportFromStreamAsync(stream, "invalid.json", cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.IsFailed);
    }

    [Theory]
    [InlineData(ImportConflictStrategy.ReplaceExisting)]
    [InlineData(ImportConflictStrategy.KeepBoth)]
    public async Task AccountImport_ConfigurationOnlyConflictPreservesIncomingMetadata(ImportConflictStrategy strategy)
    {
        var existing = new Account(Guid.NewGuid(), "Example", Secret, "alice");
        var incoming = new Account(Guid.NewGuid(), "Example", Secret, "alice", algorithm: "SHA512", digits: 8);
        var manager = new Mock<IAccountManager>();
        manager.Setup(value => value.GetAllOtpEntriesSortedAsync()).ReturnsAsync(Result.Ok<IReadOnlyList<Account>>([existing]));
        manager.Setup(value => value.BackupOtpEntriesStorageFileAsync()).ReturnsAsync(Result.Ok());
        IReadOnlyCollection<Account>? committed = null;
        manager.Setup(value => value.CommitImportAsync(It.IsAny<IReadOnlyCollection<Account>>()))
            .Callback<IReadOnlyCollection<Account>>(accounts => committed = accounts).ReturnsAsync(Result.Ok());
        var result = await new AccountImportService(manager.Object).ImportAsync([incoming], strategy,
            (preview, _) =>
            {
                var conflict = Assert.Single(preview.ChangedConflicts);
                Assert.True(conflict.AlgorithmChanged);
                Assert.True(conflict.DigitsChanged);
                Assert.False(conflict.SecretChanged);
                return Task.FromResult(true);
            }, TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        Assert.NotNull(committed);
        AssertConfiguration(Assert.Single(committed, account => account.Algorithm == "SHA512"), "SHA512", 8);
    }

    [Fact]
    public async Task QrImport_ConfigurationOnlyConflictUpdatesWithoutChangingSeed()
    {
        var existing = new Account(Guid.NewGuid(), "Example", Secret, "alice");
        var manager = new Mock<IAccountManager>();
        manager.Setup(value => value.GetAllOtpEntriesSortedAsync()).ReturnsAsync(Result.Ok<IReadOnlyList<Account>>([existing]));
        manager.Setup(value => value.BackupOtpEntriesStorageFileAsync()).ReturnsAsync(Result.Ok());
        IReadOnlyCollection<Account>? committed = null;
        manager.Setup(value => value.CommitImportAsync(It.IsAny<IReadOnlyCollection<Account>>()))
            .Callback<IReadOnlyCollection<Account>>(accounts => committed = accounts).ReturnsAsync(Result.Ok());
        var uri = new QrCodeService().BuildOtpAuthUri("Example", Secret, "alice", algorithm: "SHA512", digits: 8);
        var result = await new QrAccountImportService(manager.Object).ImportAsync(uri,
            (conflict, _) =>
            {
                Assert.True(conflict.AlgorithmChanged);
                Assert.True(conflict.DigitsChanged);
                Assert.False(conflict.SecretChanged);
                return Task.FromResult(TOTP.Core.Services.Models.QrAccountConflictDecision.UpdateExisting);
            }, TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        AssertConfiguration(Assert.Single(committed!), "SHA512", 8);
        Assert.Equal(existing.ID, Assert.Single(committed!).ID);
    }

    [Theory]
    [MemberData(nameof(Configurations))]
    public async Task SharedAccountServices_ProduceIdenticalDisplayAndSnapshot(string algorithm, int digits)
    {
        var account = new Account(Guid.NewGuid(), "Example", Secret, algorithm: algorithm, digits: digits);
        var manager = new Mock<IAccountManager>();
        manager.Setup(value => value.GetAllOtpEntriesSortedAsync()).ReturnsAsync(Result.Ok<IReadOnlyList<Account>>([account]));
        var dal = new Mock<IAccountDAL>();
        dal.Setup(value => value.GetAllAsync()).ReturnsAsync(Result.Ok(new List<Account> { account }));
        var generator = new OtpNetTotpGenerator(new FixedClock(1111111109));
        var single = (await new AccountTotpService(manager.Object, generator).GenerateAsync(account.ID)).Value;
        var batch = (await new AccountTotpService(manager.Object, generator).GenerateManyAsync([account.ID])).Value;
        var snapshot = (await new AccountListSnapshotService(dal.Object, generator).LoadAsync()).Value;
        Assert.Equal(single, batch.Codes[account.ID]);
        Assert.Equal(single, Assert.Single(snapshot.Accounts).Totp);
        Assert.Equal(digits, single.Code.Length);
        var desktop = new TOTP.Avalonia.Desktop.Presentation.AccountListItemViewModel(account.ID, account.Issuer, "");
        desktop.UpdateCode(single.Code, single.RemainingSeconds, single.PeriodSeconds, single.NextCode);
        Assert.Equal(single.Code.Insert(digits / 2, " "), desktop.DisplayCode);
        Assert.Equal(single.NextCode.Insert(digits / 2, " "), desktop.DisplayNextCode);
        desktop.Tick();
        Assert.Equal(single.RemainingSeconds - 1, desktop.RemainingSeconds);
    }

    [Theory]
    [InlineData("algorithm=")]
    [InlineData("algorithm=MD5")]
    [InlineData("digits=7")]
    [InlineData("digits=")]
    [InlineData("digits=no")]
    public void Uri_RejectsExplicitInvalidMetadata(string parameter) =>
        Assert.Throws<ArgumentException>(() => OtpauthParser.Parse("otpauth://totp/Example?secret=" + Secret + "&" + parameter));

    private static void AssertConfiguration(Account account, string algorithm, int digits)
    {
        Assert.Equal(algorithm, account.Algorithm);
        Assert.Equal(digits, account.Digits);
        Assert.Equal(Secret, account.Secret);
    }

    public sealed class FixedClock(long seconds) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.FromUnixTimeSeconds(seconds);
    }
}
