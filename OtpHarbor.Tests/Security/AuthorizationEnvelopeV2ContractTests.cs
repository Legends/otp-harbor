using System.Text.Json;
using TOTP.Core.Security.Models;

namespace TOTP.Tests.Security;

public sealed class AuthorizationEnvelopeV2ContractTests
{
    [Fact]
    public void Serialize_UsesStablePortableWireContract()
    {
        var envelope = CreateEnvelope();

        var json = JsonSerializer.SerializeToUtf8Bytes(envelope);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.Equal(["format", "version", "passwordWrapper"], PropertyNames(root));
        Assert.Equal("totp-authorization-envelope", root.GetProperty("format").GetString());
        Assert.Equal(2, root.GetProperty("version").GetInt32());

        var wrapper = root.GetProperty("passwordWrapper");
        Assert.Equal(["kdf", "wrappedKey"], PropertyNames(wrapper));
        var kdf = wrapper.GetProperty("kdf");
        Assert.Equal(
            ["algorithm", "version", "salt", "passes", "memoryKiB", "parallelism"],
            PropertyNames(kdf));
        Assert.Equal("argon2id", kdf.GetProperty("algorithm").GetString());
        Assert.Equal(19, kdf.GetProperty("version").GetInt32());
        Assert.Equal("AQIDBAUGBwgJCgsMDQ4PEA==", kdf.GetProperty("salt").GetString());
        Assert.Equal(3, kdf.GetProperty("passes").GetInt32());
        Assert.Equal(65_536, kdf.GetProperty("memoryKiB").GetInt32());
        Assert.Equal(1, kdf.GetProperty("parallelism").GetInt32());

        var wrappedKey = wrapper.GetProperty("wrappedKey");
        Assert.Equal(["algorithm", "nonce", "ciphertext"], PropertyNames(wrappedKey));
        Assert.Equal("aes-256-gcm", wrappedKey.GetProperty("algorithm").GetString());
        Assert.Equal("ISIjJCUmJygpKiss", wrappedKey.GetProperty("nonce").GetString());
        Assert.Equal("AQIDBA==", wrappedKey.GetProperty("ciphertext").GetString());
        Assert.Equal(
            "totp-manager/authorization-envelope/v2/password-wrapper",
            AesGcmWrappedKeyV2.AssociatedDataContext);
    }

    [Fact]
    public void Serialize_DoesNotEmitLegacyOrComputedAuthorizationFields()
    {
        var json = JsonSerializer.Serialize(CreateEnvelope());

        Assert.DoesNotContain("Gate", json, StringComparison.Ordinal);
        Assert.DoesNotContain("Hello", json, StringComparison.Ordinal);
        Assert.DoesNotContain("IsConfigured", json, StringComparison.Ordinal);
        Assert.DoesNotContain("PasswordWrappedDek", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Deserialize_RoundTripsAllPasswordRecoveryParameters()
    {
        var original = CreateEnvelope();
        var json = JsonSerializer.SerializeToUtf8Bytes(original);

        var roundTripped = JsonSerializer.Deserialize<AuthorizationEnvelopeV2>(json);

        Assert.NotNull(roundTripped);
        Assert.Equal(AuthorizationEnvelopeV2.FormatIdentifier, roundTripped.Format);
        Assert.Equal(AuthorizationEnvelopeV2.CurrentVersion, roundTripped.Version);
        Assert.Equal(Argon2idParametersV2.AlgorithmIdentifier, roundTripped.PasswordWrapper.Kdf.Algorithm);
        Assert.Equal(Argon2idParametersV2.CurrentAlgorithmVersion, roundTripped.PasswordWrapper.Kdf.Version);
        Assert.Equal(original.PasswordWrapper.Kdf.Salt, roundTripped.PasswordWrapper.Kdf.Salt);
        Assert.Equal(original.PasswordWrapper.Kdf.Passes, roundTripped.PasswordWrapper.Kdf.Passes);
        Assert.Equal(original.PasswordWrapper.Kdf.MemoryKiB, roundTripped.PasswordWrapper.Kdf.MemoryKiB);
        Assert.Equal(original.PasswordWrapper.Kdf.Parallelism, roundTripped.PasswordWrapper.Kdf.Parallelism);
        Assert.Equal(original.PasswordWrapper.WrappedKey.Nonce, roundTripped.PasswordWrapper.WrappedKey.Nonce);
        Assert.Equal(original.PasswordWrapper.WrappedKey.Ciphertext, roundTripped.PasswordWrapper.WrappedKey.Ciphertext);
    }

    [Fact]
    public void Deserialize_WhenCriticalHeaderIsMissing_RejectsPayload()
    {
        const string json = """
            {
              "version": 2,
              "passwordWrapper": {
                "kdf": {
                  "algorithm": "argon2id",
                  "version": 19,
                  "salt": "AQIDBAUGBwgJCgsMDQ4PEA==",
                  "passes": 3,
                  "memoryKiB": 65536,
                  "parallelism": 1
                },
                "wrappedKey": {
                  "algorithm": "aes-256-gcm",
                  "nonce": "ISIjJCUmJygpKiss",
                  "ciphertext": "AQIDBA=="
                }
              }
            }
            """;

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<AuthorizationEnvelopeV2>(json));
    }

    [Fact]
    public void Serialize_WithWindowsQuickUnlock_UsesTypedProviderMetadata()
    {
        var envelope = CreateEnvelope() with
        {
            QuickUnlockWrapper = CreateWindowsQuickUnlockWrapper()
        };

        var json = JsonSerializer.SerializeToUtf8Bytes(envelope);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.Equal(
            ["format", "version", "passwordWrapper", "quickUnlockWrapper"],
            PropertyNames(root));
        var quickUnlock = root.GetProperty("quickUnlockWrapper");
        Assert.Equal(
            ["provider", "providerVersion", "authenticationPolicy", "keyReference", "wrappedKey"],
            PropertyNames(quickUnlock));
        Assert.Equal("windows-hello-tpm", quickUnlock.GetProperty("provider").GetString());
        Assert.Equal(1, quickUnlock.GetProperty("providerVersion").GetInt32());
        Assert.Equal(
            "user-verification-required",
            quickUnlock.GetProperty("authenticationPolicy").GetString());
        Assert.Equal("TOTP_TPM_SYNTHETIC_FIXTURE", quickUnlock.GetProperty("keyReference").GetString());

        var wrappedKey = quickUnlock.GetProperty("wrappedKey");
        Assert.Equal(["algorithm", "ciphertext"], PropertyNames(wrappedKey));
        Assert.Equal("rsa-oaep-sha256", wrappedKey.GetProperty("algorithm").GetString());
        Assert.Equal(256, wrappedKey.GetProperty("ciphertext").GetBytesFromBase64().Length);
        Assert.False(wrappedKey.TryGetProperty("nonce", out _));
    }

    [Fact]
    public void Deserialize_WithQuickUnlock_RoundTripsOpaqueMetadata()
    {
        var original = CreateEnvelope() with
        {
            QuickUnlockWrapper = CreateWindowsQuickUnlockWrapper()
        };

        var roundTripped = JsonSerializer.Deserialize<AuthorizationEnvelopeV2>(
            JsonSerializer.SerializeToUtf8Bytes(original));

        var quickUnlock = Assert.IsType<PlatformQuickUnlockWrapperV2>(roundTripped?.QuickUnlockWrapper);
        Assert.Equal(original.QuickUnlockWrapper.Provider, quickUnlock.Provider);
        Assert.Equal(original.QuickUnlockWrapper.ProviderVersion, quickUnlock.ProviderVersion);
        Assert.Equal(original.QuickUnlockWrapper.AuthenticationPolicy, quickUnlock.AuthenticationPolicy);
        Assert.Equal(original.QuickUnlockWrapper.KeyReference, quickUnlock.KeyReference);
        Assert.Equal(original.QuickUnlockWrapper.WrappedKey.Algorithm, quickUnlock.WrappedKey.Algorithm);
        Assert.Equal(original.QuickUnlockWrapper.WrappedKey.Ciphertext, quickUnlock.WrappedKey.Ciphertext);
        Assert.True(PlatformQuickUnlockContract.IsSupported(quickUnlock));
    }

    [Fact]
    public void Deserialize_WhenQuickUnlockProviderIsMissing_RejectsPayload()
    {
        var json = JsonSerializer.Serialize(CreateEnvelope() with
        {
            QuickUnlockWrapper = CreateWindowsQuickUnlockWrapper()
        });
        json = json.Replace(
            $"\"provider\":\"{PlatformQuickUnlockContract.WindowsHelloTpmProvider}\",",
            string.Empty,
            StringComparison.Ordinal);

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<AuthorizationEnvelopeV2>(json));
    }

    [Fact]
    public void IsSupported_WhenProviderPolicyOrCiphertextIsChanged_FailsClosed()
    {
        var valid = CreateWindowsQuickUnlockWrapper();

        Assert.False(PlatformQuickUnlockContract.IsSupported(valid with { Provider = "unknown-provider" }));
        Assert.False(PlatformQuickUnlockContract.IsSupported(valid with { AuthenticationPolicy = "silent" }));
        Assert.False(PlatformQuickUnlockContract.IsSupported(valid with
        {
            WrappedKey = valid.WrappedKey with { Nonce = new byte[12] }
        }));
        Assert.False(PlatformQuickUnlockContract.IsSupported(valid with
        {
            WrappedKey = valid.WrappedKey with { Ciphertext = new byte[255] }
        }));
    }

    private static string[] PropertyNames(JsonElement element) =>
        element.EnumerateObject().Select(property => property.Name).ToArray();

    private static PlatformQuickUnlockWrapperV2 CreateWindowsQuickUnlockWrapper() => new()
    {
        Provider = PlatformQuickUnlockContract.WindowsHelloTpmProvider,
        ProviderVersion = PlatformQuickUnlockContract.WindowsHelloTpmProviderVersion,
        AuthenticationPolicy = PlatformQuickUnlockContract.UserVerificationRequired,
        KeyReference = "TOTP_TPM_SYNTHETIC_FIXTURE",
        WrappedKey = new PlatformWrappedKeyV2
        {
            Algorithm = PlatformQuickUnlockContract.RsaOaepSha256Algorithm,
            Ciphertext = new byte[256]
        }
    };

    private static AuthorizationEnvelopeV2 CreateEnvelope() => new()
    {
        PasswordWrapper = new PasswordKeyWrapperV2
        {
            Kdf = new Argon2idParametersV2
            {
                Salt = Convert.FromBase64String("AQIDBAUGBwgJCgsMDQ4PEA=="),
                Passes = 3,
                MemoryKiB = 65_536,
                Parallelism = 1
            },
            WrappedKey = new AesGcmWrappedKeyV2
            {
                Nonce = Convert.FromBase64String("ISIjJCUmJygpKiss"),
                Ciphertext = [1, 2, 3, 4]
            }
        }
    };
}
