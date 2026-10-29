using System.Security.Cryptography;
using System.Text;
using FluentResults;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TOTP.Core.Services.Interfaces;
using TOTP.Core.Services.Models;

namespace TOTP.Infrastructure.Services;

public sealed class PortableUpdateService(
    IConfiguration configuration,
    IApplicationVersionProvider applicationVersion,
    HttpClient httpClient,
    ISignedAppcastVerifier appcastVerifier,
    ISignedPayloadVerifier payloadVerifier,
    IPlatformApplicationPaths applicationPaths,
    IPlatformFileSecurity fileSecurity,
    ILogger<PortableUpdateService> logger) : IPortableUpdateService
{
    private const int MaximumAppcastBytes = 256 * 1024;
    private const int MaximumSignatureBytes = 1024;
    private const long MaximumPackageBytes = 128L * 1024 * 1024;

    public async Task<Result<PortableUpdateCheckResult>> CheckAsync(
        CancellationToken cancellationToken = default)
    {
        if (!configuration.GetValue("AutoUpdate:Enabled", false))
            return Result.Ok(new PortableUpdateCheckResult(PortableUpdateCheckStatus.Disabled));

        var distributionMode = configuration["AutoUpdate:DistributionMode"] ?? "direct";
        if (distributionMode is "package-manager" or "store")
            return Result.Ok(new PortableUpdateCheckResult(PortableUpdateCheckStatus.Disabled));
        if (!string.Equals(distributionMode, "direct", StringComparison.Ordinal))
            return FailCheck(
                PortableUpdateErrorCode.ConfigurationInvalid,
                "The update distribution policy is invalid.");

        var appcastText = configuration["AutoUpdate:AppcastUrl"];
        var publicKey = configuration["AutoUpdate:PublicKey"];
        var channel = configuration["AutoUpdate:Channel"] ?? "stable";
        if (!TryHttpsUri(appcastText, out var appcastUri)
            || string.IsNullOrWhiteSpace(publicKey)
            || channel is not ("stable" or "rc"))
            return FailCheck(
                PortableUpdateErrorCode.ConfigurationInvalid,
                "The signed update feed is not configured safely.");

        byte[]? appcastBytes = null;
        byte[]? signatureBytes = null;
        try
        {
            appcastBytes = await FetchBoundedAsync(appcastUri, MaximumAppcastBytes, cancellationToken);
            signatureBytes = await FetchBoundedAsync(
                new Uri(appcastUri.AbsoluteUri + ".signature"),
                MaximumSignatureBytes,
                cancellationToken);
            var signature = Encoding.UTF8.GetString(signatureBytes).Trim();
            var verified = appcastVerifier.Verify(new SignedAppcastCheckRequest(
                appcastBytes,
                signature,
                publicKey,
                applicationVersion.CurrentVersion,
                CurrentOperatingSystem(),
                System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture
                    .ToString().ToLowerInvariant(),
                RequireExplicitTarget: true,
                Channel: channel));
            if (verified.Status is SignedAppcastCheckStatus.InvalidFormat
                or SignedAppcastCheckStatus.InvalidSignature)
            {
                return FailCheck(
                    PortableUpdateErrorCode.FeedVerificationFailed,
                    "The signed update feed was rejected.");
            }

            if (verified.Status == SignedAppcastCheckStatus.NoApplicableUpdate)
                return Result.Ok(new PortableUpdateCheckResult(PortableUpdateCheckStatus.NoUpdate));

            if (verified.Version is null
                || verified.ArtifactUri is null
                || !TryHttpsUri(verified.ArtifactUri.AbsoluteUri, out _)
                || string.IsNullOrWhiteSpace(verified.ArtifactSignature)
                || !HasValidSignatureShape(verified.ArtifactSignature))
            {
                return FailCheck(
                    PortableUpdateErrorCode.OfferIncomplete,
                    "The signed update offer is incomplete.");
            }

            return Result.Ok(new PortableUpdateCheckResult(
                PortableUpdateCheckStatus.UpdateAvailable,
                new PortableUpdateOffer(
                    verified.Version,
                    verified.ArtifactUri,
                    verified.ArtifactSignature,
                    verified.ReleaseNotes ?? string.Empty)));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogFailure("check", exception);
            return FailCheck(
                PortableUpdateErrorCode.FeedUnavailable,
                "The update feed could not be checked safely.",
                exception);
        }
        finally
        {
            Clear(appcastBytes);
            Clear(signatureBytes);
        }
    }

    private static Result<PortableUpdateCheckResult> FailCheck(
        PortableUpdateErrorCode code,
        string message,
        Exception? exception = null) =>
        Result.Fail<PortableUpdateCheckResult>(
            new PortableUpdateError(code, message, exception));

    public async Task<Result<PortableUpdatePackage>> DownloadAsync(
        PortableUpdateOffer offer,
        IProgress<PortableUpdateDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(offer);
        var publicKey = configuration["AutoUpdate:PublicKey"];
        if (!TryHttpsUri(offer.ArtifactUri.AbsoluteUri, out _)
            || string.IsNullOrWhiteSpace(publicKey)
            || !HasValidSignatureShape(offer.ArtifactSignature))
        {
            return Result.Fail("The update package metadata is invalid.");
        }

        string? partialPath = null;
        byte[]? packageBytes = null;
        try
        {
            using var response = await httpClient.GetAsync(
                offer.ArtifactUri,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is > MaximumPackageBytes)
                return Result.Fail("The update package exceeds the safety limit.");

            var updateDirectory = Path.Combine(applicationPaths.ApplicationDataDirectory, "Updates");
            Directory.CreateDirectory(updateDirectory);
            fileSecurity.RestrictDirectoryToCurrentUser(updateDirectory);
            var packageExtension = SafePackageExtension(offer.ArtifactUri);
            var packageStem = $"update-{Guid.NewGuid():N}";
            partialPath = Path.Combine(updateDirectory, $"{packageStem}{packageExtension}.part");
            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var destination = new FileStream(
                             partialPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             81920,
                             useAsync: true))
            {
                fileSecurity.RestrictFileToCurrentUser(partialPath);
                var buffer = new byte[81920];
                long total = 0;
                try
                {
                    while (true)
                    {
                        var read = await source.ReadAsync(buffer, cancellationToken);
                        if (read == 0) break;
                        total += read;
                        if (total > MaximumPackageBytes)
                            return Result.Fail("The update package exceeds the safety limit.");
                        await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                        progress?.Report(new PortableUpdateDownloadProgress(
                            total,
                            response.Content.Headers.ContentLength));
                    }

                    await destination.FlushAsync(cancellationToken);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(buffer);
                }
            }

            packageBytes = await File.ReadAllBytesAsync(partialPath, cancellationToken);
            if (!payloadVerifier.Verify(packageBytes, offer.ArtifactSignature, publicKey))
                return Result.Fail("The downloaded update signature was rejected.");

            var readyPath = Path.Combine(updateDirectory, $"{packageStem}.ready{packageExtension}");
            File.Move(partialPath, readyPath, overwrite: false);
            partialPath = readyPath;
            fileSecurity.RestrictFileToCurrentUser(readyPath);
            partialPath = null;
            return Result.Ok(new PortableUpdatePackage(
                offer.Version,
                readyPath,
                offer.ArtifactSignature,
                publicKey));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogFailure("download", exception);
            return Result.Fail("The update package could not be downloaded safely.");
        }
        finally
        {
            Clear(packageBytes);
            if (partialPath is not null) TryDelete(partialPath);
        }
    }

    private async Task<byte[]> FetchBoundedAsync(
        Uri uri,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(
            uri,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > 0
            && response.Content.Headers.ContentLength > maximumBytes)
            throw new InvalidDataException("Remote content exceeds the configured bound.");

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var destination = new MemoryStream();
        var buffer = new byte[81920];
        try
        {
            var total = 0;
            while (true)
            {
                var read = await source.ReadAsync(buffer, cancellationToken);
                if (read == 0) break;
                total += read;
                if (total > maximumBytes)
                    throw new InvalidDataException("Remote content exceeds the configured bound.");
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }
            return destination.ToArray();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(buffer);
        }
    }

    private static bool TryHttpsUri(string? value, out Uri uri)
    {
        var valid = Uri.TryCreate(value, UriKind.Absolute, out var candidate)
                    && candidate.Scheme == Uri.UriSchemeHttps;
        uri = candidate ?? new Uri("https://invalid.invalid");
        return valid;
    }

    private static bool HasValidSignatureShape(string signature)
    {
        try
        {
            return Convert.FromBase64String(signature.Trim()).Length == 64;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string SafePackageExtension(Uri artifactUri)
    {
        var extension = Path.GetExtension(artifactUri.AbsolutePath);
        return extension.Length is > 1 and <= 10
               && extension.Skip(1).All(char.IsLetterOrDigit)
            ? extension.ToLowerInvariant()
            : ".package";
    }

    private static string CurrentOperatingSystem() =>
        OperatingSystem.IsWindows() ? "windows" :
        OperatingSystem.IsMacOS() ? "macos" :
        OperatingSystem.IsLinux() ? "linux" : "unknown";

    private void LogFailure(string stage, Exception exception) => logger.LogError(
        "Portable update {Stage} failed with exception type {ExceptionType}.",
        stage,
        exception.GetType().FullName);

    private static void Clear(byte[]? buffer)
    {
        if (buffer is not null) CryptographicOperations.ZeroMemory(buffer);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // Best-effort cleanup of an unverified partial package.
        }
    }
}
