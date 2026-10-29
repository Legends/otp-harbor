using System.Text.Json;
using System.Security.Cryptography;
using FluentResults;
using TOTP.Core.Security.Models;

namespace TOTP.Core.Security;

public static class AuthorizationEnvelopeV2Codec
{
    public const int MaximumPayloadSize = 64 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new() { MaxDepth = 16, WriteIndented = true };

    public static Result<byte[]> Serialize(AuthorizationEnvelopeV2 envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        var validation = Validate(envelope, requireSupportedQuickUnlock: true);
        if (validation.IsFailed) return Result.Fail<byte[]>(validation.Errors);

        var payload = JsonSerializer.SerializeToUtf8Bytes(envelope, JsonOptions);
        if (payload.Length <= MaximumPayloadSize) return Result.Ok(payload);

        CryptographicOperations.ZeroMemory(payload);
        return Fail<byte[]>(AuthorizationEnvelopeErrorCode.TooLarge, "Authorization envelope exceeds the size limit.");
    }

    public static Result<AuthorizationEnvelopeV2> Deserialize(ReadOnlyMemory<byte> payload)
    {
        if (payload.IsEmpty)
            return Fail<AuthorizationEnvelopeV2>(AuthorizationEnvelopeErrorCode.Empty, "Authorization envelope is empty.");
        if (payload.Length > MaximumPayloadSize)
            return Fail<AuthorizationEnvelopeV2>(AuthorizationEnvelopeErrorCode.TooLarge, "Authorization envelope exceeds the size limit.");

        try
        {
            using var document = JsonDocument.Parse(payload, new JsonDocumentOptions { MaxDepth = 16 });
            if (HasDuplicateProperty(document.RootElement))
                return Fail<AuthorizationEnvelopeV2>(AuthorizationEnvelopeErrorCode.Malformed, "Authorization envelope contains duplicate properties.");

            var envelope = JsonSerializer.Deserialize<AuthorizationEnvelopeV2>(payload.Span, JsonOptions);
            if (envelope is null)
                return Fail<AuthorizationEnvelopeV2>(AuthorizationEnvelopeErrorCode.Malformed, "Authorization envelope is null.");

            var validation = Validate(envelope, requireSupportedQuickUnlock: false);
            return validation.IsSuccess
                ? Result.Ok(envelope)
                : Result.Fail<AuthorizationEnvelopeV2>(validation.Errors);
        }
        catch (JsonException)
        {
            return Fail<AuthorizationEnvelopeV2>(AuthorizationEnvelopeErrorCode.Malformed, "Authorization envelope JSON is invalid.");
        }
    }

    private static Result Validate(AuthorizationEnvelopeV2 envelope, bool requireSupportedQuickUnlock)
    {
        if (!string.Equals(envelope.Format, AuthorizationEnvelopeV2.FormatIdentifier, StringComparison.Ordinal))
            return Fail(AuthorizationEnvelopeErrorCode.UnsupportedFormat, "Authorization envelope format is unsupported.");
        if (envelope.Version != AuthorizationEnvelopeV2.CurrentVersion)
            return Fail(AuthorizationEnvelopeErrorCode.UnsupportedVersion, "Authorization envelope version is unsupported.");

        var wrapper = envelope.PasswordWrapper;
        if (wrapper?.Kdf is null || wrapper.WrappedKey is null)
            return Fail(AuthorizationEnvelopeErrorCode.InvalidPasswordWrapper, "Password wrapper is missing.");

        var kdf = wrapper.Kdf;
        var wrappedKey = wrapper.WrappedKey;
        var validPasswordWrapper =
            string.Equals(kdf.Algorithm, Argon2idParametersV2.AlgorithmIdentifier, StringComparison.Ordinal)
            && kdf.Version == Argon2idParametersV2.CurrentAlgorithmVersion
            && kdf.Salt is { Length: 16 }
            && kdf.Passes is >= 3 and <= 10
            && kdf.MemoryKiB is >= 65_536 and <= 262_144
            && kdf.Parallelism == 1
            && string.Equals(wrappedKey.Algorithm, AesGcmWrappedKeyV2.AlgorithmIdentifier, StringComparison.Ordinal)
            && wrappedKey.Nonce is { Length: 12 }
            && wrappedKey.Ciphertext is { Length: 48 };
        if (!validPasswordWrapper)
            return Fail(AuthorizationEnvelopeErrorCode.InvalidPasswordWrapper, "Password wrapper is invalid.");

        if (requireSupportedQuickUnlock
            && envelope.QuickUnlockWrapper is not null
            && !PlatformQuickUnlockContract.IsSupported(envelope.QuickUnlockWrapper))
        {
            return Fail(AuthorizationEnvelopeErrorCode.InvalidQuickUnlockWrapper, "Quick-unlock wrapper is unsupported.");
        }

        return Result.Ok();
    }

    private static bool HasDuplicateProperty(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name) || HasDuplicateProperty(property.Value)) return true;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            return element.EnumerateArray().Any(HasDuplicateProperty);
        }

        return false;
    }

    private static Result Fail(AuthorizationEnvelopeErrorCode code, string message) =>
        Result.Fail(new AuthorizationEnvelopeError(code, message));

    private static Result<T> Fail<T>(AuthorizationEnvelopeErrorCode code, string message) =>
        Result.Fail<T>(new AuthorizationEnvelopeError(code, message));
}
