using Microsoft.Extensions.Logging.Abstractions;
using System.Security.Cryptography;
using System.Text.Json;
using TOTP.Infrastructure.Security;
using TOTP.Tests.Common;

namespace TOTP.Tests.Integration;

[Collection(NonParallelCollectionDefinition.NonParallel)]
public sealed class LegacySettingsFixtureTests
{
    private const string SyntheticPassword = "Synthetic-Only-Password!42";
    private static readonly byte[] ExpectedDek = Convert.FromBase64String(
        "oaKjpKWmp6ipqqusra6vsLGys7S1tre4ubq7vL2+v8A=");

    [Fact]
    public void Manifest_CoversEveryFixtureExactlyOnce()
    {
        var manifest = LoadManifest();
        var fixtureFiles = Directory.GetFiles(FixtureDirectory, "*.json")
            .Select(Path.GetFileName)
            .Where(file => !string.Equals(file, "manifest.json", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(6, manifest.Count);
        Assert.Equal(manifest.Count, manifest.Select(entry => entry.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(
            fixtureFiles,
            manifest.Select(entry => entry.File).Order(StringComparer.Ordinal).ToArray());

        foreach (var entry in manifest)
        {
            Assert.False(string.IsNullOrWhiteSpace(entry.SourceCommit));
            Assert.False(string.IsNullOrWhiteSpace(entry.HistoricalReaderOutcome));
            using var document = JsonDocument.Parse(File.ReadAllBytes(FixturePath(entry)));
            Assert.Equal(JsonValueKind.Object, document.RootElement.ValueKind);
        }
    }

    [Fact]
    public void Pbkdf2Fixtures_ContainValidSyntheticHistoricalPasswordHashes()
    {
        foreach (var entry in LoadManifest().Where(entry => entry.AuthorizationKind == "Pbkdf2PasswordHash"))
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(FixturePath(entry)));
            var authorization = entry.RootKind == "AuthorizationProfile"
                ? document.RootElement
                : document.RootElement.GetProperty("Authorization");
            var salt = authorization.GetProperty("PasswordSalt").GetBytesFromBase64();
            var expectedHash = authorization.GetProperty("PasswordHash").GetBytesFromBase64();

            var actualHash = Rfc2898DeriveBytes.Pbkdf2(
                SyntheticPassword,
                salt,
                200_000,
                HashAlgorithmName.SHA256,
                expectedHash.Length);

            Assert.True(CryptographicOperations.FixedTimeEquals(expectedHash, actualHash), entry.Id);
            CryptographicOperations.ZeroMemory(actualHash);
        }
    }

    [Fact]
    public async Task EnvelopeFixtures_UnwrapToSyntheticDekWithCurrentPasswordService()
    {
        var passwordService = new MasterPasswordService(NullLogger<MasterPasswordService>.Instance);

        foreach (var entry in LoadManifest().Where(entry => entry.AuthorizationKind.StartsWith("Argon2id", StringComparison.Ordinal)))
        {
            var settings = JsonSerializer.Deserialize<LegacyAppSettings>(await File.ReadAllBytesAsync(
                FixturePath(entry),
                TestContext.Current.CancellationToken));
            Assert.NotNull(settings);
            var authorization = settings.Authorization;

            var dek = await passwordService.UnwrapKeyAsync(
                authorization.PasswordWrappedDek!,
                SyntheticPassword,
                authorization.PasswordSalt!,
                authorization.ArgonIterations,
                authorization.ArgonMemorySize,
                authorization.DekNonce!);

            Assert.NotNull(dek);
            try
            {
                Assert.Equal(ExpectedDek, dek);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(dek);
            }
        }
    }

    private static string FixtureDirectory =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "LegacySettings");

    private static string FixturePath(LegacyFixtureManifestEntry entry) =>
        Path.Combine(FixtureDirectory, entry.File);

    private static IReadOnlyList<LegacyFixtureManifestEntry> LoadManifest() =>
        JsonSerializer.Deserialize<List<LegacyFixtureManifestEntry>>(
            File.ReadAllBytes(Path.Combine(FixtureDirectory, "manifest.json")),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
        ?? throw new InvalidOperationException("Legacy settings fixture manifest is empty.");

    private sealed record LegacyFixtureManifestEntry(
        string Id,
        string File,
        string SourceCommit,
        string RootKind,
        string AuthorizationKind,
        string HistoricalReaderOutcome);

    private sealed record LegacyAppSettings
    {
        public required LegacyAuthorizationData Authorization { get; init; }
    }

    private sealed record LegacyAuthorizationData
    {
        public byte[]? PasswordSalt { get; init; }
        public int ArgonIterations { get; init; }
        public int ArgonMemorySize { get; init; }
        public byte[]? PasswordWrappedDek { get; init; }
        public byte[]? DekNonce { get; init; }
    }
}
