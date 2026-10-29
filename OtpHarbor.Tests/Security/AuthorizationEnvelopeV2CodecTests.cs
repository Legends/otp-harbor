using System.Text;
using System.Text.Json;
using TOTP.Core.Security;
using TOTP.Core.Security.Models;

namespace TOTP.Tests.Security;

public sealed class AuthorizationEnvelopeV2CodecTests
{
    [Fact]
    public void SerializeThenDeserialize_ValidEnvelope_RoundTripsPasswordWrapper()
    {
        var envelope = CreateEnvelope();

        var encoded = AuthorizationEnvelopeV2Codec.Serialize(envelope);
        var decoded = AuthorizationEnvelopeV2Codec.Deserialize(encoded.Value);

        Assert.True(encoded.IsSuccess);
        Assert.True(decoded.IsSuccess);
        Assert.Equal(envelope.PasswordWrapper.Kdf.Salt, decoded.Value.PasswordWrapper.Kdf.Salt);
        Assert.Equal(envelope.PasswordWrapper.Kdf.Passes, decoded.Value.PasswordWrapper.Kdf.Passes);
        Assert.Equal(envelope.PasswordWrapper.Kdf.MemoryKiB, decoded.Value.PasswordWrapper.Kdf.MemoryKiB);
        Assert.Equal(envelope.PasswordWrapper.Kdf.Parallelism, decoded.Value.PasswordWrapper.Kdf.Parallelism);
        Assert.Equal(envelope.PasswordWrapper.WrappedKey.Nonce, decoded.Value.PasswordWrapper.WrappedKey.Nonce);
        Assert.Equal(envelope.PasswordWrapper.WrappedKey.Ciphertext, decoded.Value.PasswordWrapper.WrappedKey.Ciphertext);
    }

    [Theory]
    [InlineData("{not-json")]
    [InlineData("{\"format\":\"totp-authorization-envelope\",\"format\":\"other\"}")]
    [InlineData("{\"unexpected\":true}")]
    public void Deserialize_MalformedAmbiguousOrUnknownJson_FailsClosed(string json)
    {
        var result = AuthorizationEnvelopeV2Codec.Deserialize(Encoding.UTF8.GetBytes(json));

        Assert.False(result.IsSuccess);
        Assert.Equal(AuthorizationEnvelopeErrorCode.Malformed, ErrorCode(result.Errors));
    }

    [Fact]
    public void Deserialize_UnsupportedVersion_ReturnsTypedFailure()
    {
        var encoded = AuthorizationEnvelopeV2Codec.Serialize(CreateEnvelope()).Value;
        var json = Encoding.UTF8.GetString(encoded).Replace("\"version\": 2", "\"version\": 99", StringComparison.Ordinal);

        var result = AuthorizationEnvelopeV2Codec.Deserialize(Encoding.UTF8.GetBytes(json));

        Assert.False(result.IsSuccess);
        Assert.Equal(AuthorizationEnvelopeErrorCode.UnsupportedVersion, ErrorCode(result.Errors));
    }

    [Fact]
    public void Serialize_WeakKdfParameters_ReturnsTypedFailure()
    {
        var envelope = CreateEnvelope() with
        {
            PasswordWrapper = CreateEnvelope().PasswordWrapper with
            {
                Kdf = CreateEnvelope().PasswordWrapper.Kdf with { MemoryKiB = 8 }
            }
        };

        var result = AuthorizationEnvelopeV2Codec.Serialize(envelope);

        Assert.False(result.IsSuccess);
        Assert.Equal(AuthorizationEnvelopeErrorCode.InvalidPasswordWrapper, ErrorCode(result.Errors));
    }

    [Fact]
    public void UnknownQuickUnlockProvider_IsReadableForPasswordRecoveryButNotWritable()
    {
        var envelope = CreateEnvelope() with
        {
            QuickUnlockWrapper = new PlatformQuickUnlockWrapperV2
            {
                Provider = "future-provider",
                ProviderVersion = 1,
                AuthenticationPolicy = PlatformQuickUnlockContract.UserVerificationRequired,
                KeyReference = "future-reference",
                WrappedKey = new PlatformWrappedKeyV2
                {
                    Algorithm = "future-algorithm",
                    Ciphertext = new byte[32]
                }
            }
        };
        var payload = JsonSerializer.SerializeToUtf8Bytes(envelope);

        var decoded = AuthorizationEnvelopeV2Codec.Deserialize(payload);
        var encoded = AuthorizationEnvelopeV2Codec.Serialize(envelope);

        Assert.True(decoded.IsSuccess);
        Assert.Equal("future-provider", decoded.Value.QuickUnlockWrapper?.Provider);
        Assert.False(encoded.IsSuccess);
        Assert.Equal(AuthorizationEnvelopeErrorCode.InvalidQuickUnlockWrapper, ErrorCode(encoded.Errors));
    }

    internal static AuthorizationEnvelopeV2 CreateEnvelope() => new()
    {
        PasswordWrapper = new PasswordKeyWrapperV2
        {
            Kdf = new Argon2idParametersV2
            {
                Salt = Enumerable.Range(1, 16).Select(value => (byte)value).ToArray(),
                Passes = 3,
                MemoryKiB = 65_536,
                Parallelism = 1
            },
            WrappedKey = new AesGcmWrappedKeyV2
            {
                Nonce = Enumerable.Range(17, 12).Select(value => (byte)value).ToArray(),
                Ciphertext = new byte[48]
            }
        }
    };

    internal static AuthorizationEnvelopeErrorCode ErrorCode(IEnumerable<FluentResults.IError> errors) =>
        Assert.IsType<AuthorizationEnvelopeError>(Assert.Single(errors)).Code;
}
