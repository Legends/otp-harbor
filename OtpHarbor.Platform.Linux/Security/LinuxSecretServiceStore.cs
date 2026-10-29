using System.Buffers;
using System.Buffers.Text;
using System.Security.Cryptography;
using FluentResults;
using Microsoft.Extensions.Logging;
using TOTP.Core.Security.Interfaces;
using TOTP.Core.Security.Models;

namespace TOTP.Platform.Linux.Security;

public sealed class LinuxSecretServiceStore(
    ILinuxSecretServiceRuntime runtime,
    ILogger<LinuxSecretServiceStore> logger) : IPlatformSecretStore
{
    private const string ApplicationAttribute = "io.github.legends.totpmanager";
    private const int MaximumReferenceLength = 256;
    private const int MaximumSecretLength = 4096;
    private const int MaximumEncodedOutputLength = 8192;

    public string ProviderId => "linux-secret-service";

    public Task<PlatformSecretStoreAvailability> GetAvailabilityAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!runtime.IsPlatformSupported)
            return Task.FromResult(PlatformSecretStoreAvailability.NotSupported);
        if (string.IsNullOrWhiteSpace(runtime.SecretToolPath))
            return Task.FromResult(PlatformSecretStoreAvailability.NotSupported);
        return Task.FromResult(runtime.HasSessionBus
            ? PlatformSecretStoreAvailability.Available
            : PlatformSecretStoreAvailability.NotConfigured);
    }

    public async Task<Result> StoreAsync(
        string secretReference,
        ReadOnlyMemory<byte> secret,
        CancellationToken cancellationToken = default)
    {
        if (!IsValidReference(secretReference))
            return Fail(PlatformSecretStoreErrorCode.InvalidReference, "The Secret Service reference is invalid.");
        if (secret.IsEmpty || secret.Length > MaximumSecretLength)
            return Fail(PlatformSecretStoreErrorCode.InvalidSecret, "The Secret Service secret is invalid.");
        if (!IsRuntimeAvailable()) return Unavailable();

        byte[]? encoded = null;
        byte[]? output = null;
        try
        {
            encoded = EncodeForStandardInput(secret.Span);
            var result = await runtime.RunAsync(
                ["store", "--label=OTP Harbor device secret", "application", ApplicationAttribute, "reference", secretReference],
                encoded,
                MaximumEncodedOutputLength,
                cancellationToken);
            output = result.StandardOutput;
            return result.ExitCode == 0
                ? Result.Ok()
                : Fail(PlatformSecretStoreErrorCode.StoreFailed, "The Secret Service secret could not be stored.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogFailure("store", exception);
            return Fail(PlatformSecretStoreErrorCode.StoreFailed, "The Secret Service secret could not be stored.");
        }
        finally
        {
            Clear(encoded);
            Clear(output);
        }
    }

    public async Task<Result<SensitiveBuffer?>> RetrieveAsync(
        string secretReference,
        CancellationToken cancellationToken = default)
    {
        if (!IsValidReference(secretReference))
            return Fail<SensitiveBuffer?>(
                PlatformSecretStoreErrorCode.InvalidReference,
                "The Secret Service reference is invalid.");
        if (!IsRuntimeAvailable()) return Unavailable<SensitiveBuffer?>();

        byte[]? output = null;
        byte[]? decoded = null;
        try
        {
            var result = await runtime.RunAsync(
                ["lookup", "application", ApplicationAttribute, "reference", secretReference],
                ReadOnlyMemory<byte>.Empty,
                MaximumEncodedOutputLength,
                cancellationToken);
            output = result.StandardOutput;
            if (result.ExitCode == 1 && TrimTrailingWhitespace(output).IsEmpty)
                return Result.Ok<SensitiveBuffer?>(null);
            if (result.ExitCode != 0)
                return Fail<SensitiveBuffer?>(
                    PlatformSecretStoreErrorCode.RetrieveFailed,
                    "The Secret Service secret could not be retrieved.");

            var encoded = TrimTrailingWhitespace(output);
            if (encoded.IsEmpty)
                return Fail<SensitiveBuffer?>(
                    PlatformSecretStoreErrorCode.InvalidSecret,
                    "The Secret Service returned invalid secret material.");
            decoded = ArrayPool<byte>.Shared.Rent(MaximumSecretLength);
            var status = Base64.DecodeFromUtf8(encoded, decoded, out var consumed, out var written);
            if (status != OperationStatus.Done || consumed != encoded.Length || written is <= 0 or > MaximumSecretLength)
                return Fail<SensitiveBuffer?>(
                    PlatformSecretStoreErrorCode.InvalidSecret,
                    "The Secret Service returned invalid secret material.");

            return Result.Ok<SensitiveBuffer?>(SensitiveBuffer.CopyFrom(decoded.AsSpan(0, written)));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogFailure("retrieve", exception);
            return Fail<SensitiveBuffer?>(
                PlatformSecretStoreErrorCode.RetrieveFailed,
                "The Secret Service secret could not be retrieved.");
        }
        finally
        {
            Clear(output);
            if (decoded is not null)
            {
                CryptographicOperations.ZeroMemory(decoded);
                ArrayPool<byte>.Shared.Return(decoded);
            }
        }
    }

    public async Task<Result> DeleteAsync(
        string secretReference,
        CancellationToken cancellationToken = default)
    {
        if (!IsValidReference(secretReference))
            return Fail(PlatformSecretStoreErrorCode.InvalidReference, "The Secret Service reference is invalid.");
        if (!IsRuntimeAvailable()) return Unavailable();

        byte[]? output = null;
        try
        {
            var result = await runtime.RunAsync(
                ["clear", "application", ApplicationAttribute, "reference", secretReference],
                ReadOnlyMemory<byte>.Empty,
                MaximumEncodedOutputLength,
                cancellationToken);
            output = result.StandardOutput;
            return result.ExitCode is 0 or 1
                ? Result.Ok()
                : Fail(PlatformSecretStoreErrorCode.DeleteFailed, "The Secret Service secret could not be removed.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogFailure("delete", exception);
            return Fail(PlatformSecretStoreErrorCode.DeleteFailed, "The Secret Service secret could not be removed.");
        }
        finally
        {
            Clear(output);
        }
    }

    private bool IsRuntimeAvailable() =>
        runtime.IsPlatformSupported
        && runtime.HasSessionBus
        && !string.IsNullOrWhiteSpace(runtime.SecretToolPath);

    private static byte[] EncodeForStandardInput(ReadOnlySpan<byte> secret)
    {
        var length = Base64.GetMaxEncodedToUtf8Length(secret.Length);
        var encoded = new byte[length + 1];
        var status = Base64.EncodeToUtf8(secret, encoded, out var consumed, out var written);
        if (status != OperationStatus.Done || consumed != secret.Length || written + 1 != encoded.Length)
            throw new InvalidOperationException("Secret encoding failed.");
        encoded[written] = (byte)'\n';
        return encoded;
    }

    private static ReadOnlySpan<byte> TrimTrailingWhitespace(byte[] value)
    {
        var length = value.Length;
        while (length > 0 && value[length - 1] is (byte)'\r' or (byte)'\n' or (byte)' ' or (byte)'\t')
            length--;
        return value.AsSpan(0, length);
    }

    private static bool IsValidReference(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length <= MaximumReferenceLength
        && !value.Any(char.IsControl);

    private static void Clear(byte[]? value)
    {
        if (value is not null) CryptographicOperations.ZeroMemory(value);
    }

    private void LogFailure(string operation, Exception exception) =>
        logger.LogWarning(
            "Linux Secret Service operation failed safely. operation={Operation} failure_type={FailureType}",
            operation,
            exception.GetType().Name);

    private static Result Unavailable() =>
        Fail(PlatformSecretStoreErrorCode.Unavailable, "Secret Service is unavailable.");

    private static Result<T> Unavailable<T>() =>
        Fail<T>(PlatformSecretStoreErrorCode.Unavailable, "Secret Service is unavailable.");

    private static Result Fail(PlatformSecretStoreErrorCode code, string message) =>
        Result.Fail(new PlatformSecretStoreError(code, message));

    private static Result<T> Fail<T>(PlatformSecretStoreErrorCode code, string message) =>
        Result.Fail<T>(new PlatformSecretStoreError(code, message));
}
