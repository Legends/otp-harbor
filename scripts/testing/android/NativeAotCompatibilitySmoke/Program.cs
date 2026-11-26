using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using TOTP.Core.Backup;
using TOTP.Core.Models;
using TOTP.Core.Security;
using TOTP.Core.Security.Models;
using TOTP.Infrastructure.Security;
using TOTP.Infrastructure.Services;

Require(!JsonSerializer.IsReflectionEnabledByDefault, "JSON reflection must be disabled for this smoke check.");

var preferences = AppPreferencesV1Codec.Deserialize("{\"format\":\"totp-preferences\",\"version\":1}"u8.ToArray());
Require(preferences.IsSuccess && preferences.Value == new AppPreferencesV1(), "Legacy preference defaults changed.");
Require(preferences.Value.AllowScreenshots, "An omitted screenshot preference must allow capture by default.");
var blockedCapture = AppPreferencesV1Codec.Deserialize("{\"format\":\"totp-preferences\",\"version\":1,\"allowScreenshots\":false}"u8.ToArray());
Require(blockedCapture.IsSuccess && !blockedCapture.Value.AllowScreenshots, "An explicit capture restriction must be preserved.");
Require(AppPreferencesV1Codec.Deserialize(AppPreferencesV1Codec.Serialize(preferences.Value).Value).IsSuccess,
    "Preferences did not round-trip without reflection.");
Require(AppPreferencesV1Codec.Deserialize("{\"format\":\"totp-preferences\",\"format\":\"other\",\"version\":1}"u8.ToArray()).IsFailed,
    "Duplicate preferences were accepted.");

var envelope = new AuthorizationEnvelopeV2
{
    PasswordWrapper = new PasswordKeyWrapperV2
    {
        Kdf = new Argon2idParametersV2
        {
            Salt = new byte[16], Passes = 3, MemoryKiB = 65_536, Parallelism = 1
        },
        WrappedKey = new AesGcmWrappedKeyV2 { Nonce = new byte[12], Ciphertext = new byte[48] }
    }
};
var encodedEnvelope = AuthorizationEnvelopeV2Codec.Serialize(envelope);
Require(encodedEnvelope.IsSuccess && AuthorizationEnvelopeV2Codec.Deserialize(encodedEnvelope.Value).IsSuccess,
    "Authorization envelope did not round-trip without reflection.");
Require(AuthorizationEnvelopeV2Codec.Serialize(envelope with
{
    PasswordWrapper = envelope.PasswordWrapper with { Kdf = envelope.PasswordWrapper.Kdf with { MemoryKiB = 8 } }
}).IsFailed, "Weak KDF parameters were accepted.");

var configuration = new AutomaticBackupConfigurationV1
{
    DestinationKind = "synthetic", DestinationLocation = "synthetic-location", DestinationDisplayName = "Synthetic"
};
Require(AutomaticBackupConfigurationV1Codec.Deserialize(AutomaticBackupConfigurationV1Codec.Serialize(configuration).Value).IsSuccess,
    "Backup configuration did not round-trip without reflection.");

var key = RandomNumberGenerator.GetBytes(32);
using var security = new SecurityContext();
try
{
    security.SetDek(key);
    var group = new AccountGroup(Guid.NewGuid(), "Synthetic group", "#123456");
    var accounts = Enumerable.Range(0, 40).Select(index => new Account(
        Guid.NewGuid(), $"Synthetic {index:D2}", "JBSWY3DPEHPK3PXP", $"user{index}@example.invalid",
        groups: [group], isFavorite: index % 2 == 0, algorithm: "SHA256", digits: 8)).ToList();
    var vault = new VaultService(security);
    var encrypted = vault.EncryptVault(accounts);
    var restored = vault.DecryptVault(encrypted);
    Require(restored.Count == 40 && restored.All(account => account.Groups.Count == 1 && account.Digits == 8
        && account.Algorithm == "SHA256"), "Current vault accounts/profile did not round-trip without reflection.");

    var legacy = "[{\"id\":\"10000000-0000-0000-0000-000000000001\",\"issuer\":\"Synthetic legacy\",\"secret\":\"JBSWY3DPEHPK3PXP\"}]"u8.ToArray();
    var nonce = RandomNumberGenerator.GetBytes(12);
    var legacyVault = new byte[4 + nonce.Length + legacy.Length + 16];
    "TVLT"u8.CopyTo(legacyVault);
    nonce.CopyTo(legacyVault, 4);
    try
    {
        using var aes = new AesGcm(key, 16);
        aes.Encrypt(nonce, legacy, legacyVault.AsSpan(16, legacy.Length), legacyVault.AsSpan(16 + legacy.Length, 16));
        var legacyAccounts = vault.DecryptVault(legacyVault);
        Require(legacyAccounts.Count == 1 && legacyAccounts[0].PeriodSeconds == 30
            && legacyAccounts[0].Digits == 6 && legacyAccounts[0].Algorithm == "SHA1",
            "Legacy vault defaults changed.");
    }
    finally { CryptographicOperations.ZeroMemory(legacy); }

    var backup = new AutomaticBackupPackageV1
    {
        CreatedUtc = DateTimeOffset.UnixEpoch, EncryptedVault = encrypted,
        VaultSha256 = Convert.ToHexString(SHA256.HashData(encrypted)), AuthorizationEnvelope = envelope
    };
    Require(AutomaticBackupPackageV1Codec.Deserialize(AutomaticBackupPackageV1Codec.Serialize(backup).Value).IsSuccess,
        "Legacy backup did not round-trip without reflection.");
    var currentBackup = new AutomaticBackupPackageV2
    {
        BackupId = Guid.NewGuid(), CreatedUtc = DateTimeOffset.UnixEpoch,
        EncryptedAccounts = encrypted, EncryptedProfile = [1, 2, 3], AuthorizationEnvelope = envelope
    };
    Require(AutomaticBackupPackageV2Codec.Deserialize(AutomaticBackupPackageV2Codec.Serialize(currentBackup).Value).IsSuccess,
        "Current backup did not round-trip without reflection.");

    var importer = new ExportService(NullLogger<ExportService>.Instance);
    using var import = new MemoryStream(Encoding.UTF8.GetBytes(
        "[{\"id\":\"10000000-0000-0000-0000-000000000001\",\"issuer\":\"Synthetic import\",\"secret\":\"JBSWY3DPEHPK3PXP\"}]"));
    var imported = await importer.ImportFromStreamAsync(import, "synthetic.json");
    Require(imported.IsSuccess && imported.Value.Count == 1, "Native JSON import failed without reflection.");
}
finally { CryptographicOperations.ZeroMemory(key); }

Console.WriteLine("Reflection-disabled JSON checks passed: preferences/defaults, authorization/KDF rejection, 40-account current vault/profile, legacy vault/defaults, backup configuration, v1/v2 backups and native import.");

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
