using System.Security.Cryptography;
using Windows.Security.Credentials.UI;
using Microsoft.Extensions.Logging;
using TOTP.Core.Security.Interfaces;
using TOTP.Core.Security.Models;

namespace TOTP.Platform.Windows.Security;

public sealed class HelloGate : IHelloGate
{
    private const int RsaKeySizeBits = 2048;

    private readonly ILogger<HelloGate> _logger;
    private readonly IHelloPromptWindowHandleProvider _windowHandleProvider;
    private readonly IHelloVerificationRequester _verificationRequester;

    public HelloGate(
        ILogger<HelloGate> logger,
        IHelloPromptWindowHandleProvider windowHandleProvider,
        IHelloVerificationRequester verificationRequester)
    {
        _logger = logger;
        _windowHandleProvider = windowHandleProvider;
        _verificationRequester = verificationRequester;
    }

    public async Task<PlatformQuickUnlockAvailability> GetAvailabilityAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var availability = await UserConsentVerifier.CheckAvailabilityAsync().AsTask(ct);
        return availability switch
        {
            UserConsentVerifierAvailability.Available => PlatformQuickUnlockAvailability.Available,
            UserConsentVerifierAvailability.DeviceNotPresent => PlatformQuickUnlockAvailability.NotSupported,
            UserConsentVerifierAvailability.NotConfiguredForUser => PlatformQuickUnlockAvailability.NotConfigured,
            UserConsentVerifierAvailability.DisabledByPolicy => PlatformQuickUnlockAvailability.DisabledByPolicy,
            UserConsentVerifierAvailability.DeviceBusy => PlatformQuickUnlockAvailability.TemporarilyUnavailable,
            _ => PlatformQuickUnlockAvailability.Unknown
        };
    }

    public async Task<bool> IsAvailableAsync(CancellationToken ct = default) =>
        await GetAvailabilityAsync(ct) == PlatformQuickUnlockAvailability.Available;

    public async Task<AuthorizationResult> RequestVerificationAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var prompt = _windowHandleProvider.BeginPrompt();
        var message = _windowHandleProvider.VerificationMessage;
        var windowHandle = _windowHandleProvider.GetActiveWindowHandle();
        var result = await _verificationRequester.RequestAsync(windowHandle, message, ct);
        return result switch
        {
            UserConsentVerificationResult.Verified => AuthorizationResult.Success,
            UserConsentVerificationResult.Canceled => AuthorizationResult.Cancelled,
            UserConsentVerificationResult.RetriesExhausted => AuthorizationResult.TooManyAttempts,
            UserConsentVerificationResult.DisabledByPolicy => AuthorizationResult.DisabledByPolicy,
            UserConsentVerificationResult.DeviceBusy or
            UserConsentVerificationResult.DeviceNotPresent or
            UserConsentVerificationResult.NotConfiguredForUser => AuthorizationResult.NotAvailable,
            _ => AuthorizationResult.Failed
        };
    }

    public async Task<byte[]> ProtectKeyAsync(
        byte[] rawDek,
        string keyId,
        CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            CngKeyCreationParameters keyParams = new CngKeyCreationParameters
            {
                ExportPolicy = CngExportPolicies.None,
                Provider = CngProvider.MicrosoftPlatformCryptoProvider
            };
            keyParams.Parameters.Add(new CngProperty(
                "Length",
                BitConverter.GetBytes(RsaKeySizeBits),
                CngPropertyOptions.None));

            using var key = CngKey.Create(CngAlgorithm.Rsa, keyId, keyParams);

            using var rsa = new RSACng(key);
            return rsa.Encrypt(rawDek, RSAEncryptionPadding.OaepSHA256);
        }, ct);
    }

    public async Task<byte[]?> UnprotectKeyAsync(byte[] wrappedDek, string keyId, CancellationToken ct = default)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            return await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                // This can trigger the Windows Hello popup because the key is in the Platform Crypto Provider.
                using var key = CngKey.Open(keyId, CngProvider.MicrosoftPlatformCryptoProvider);
                using var rsa = new RSACng(key);

                return rsa.Decrypt(wrappedDek, RSAEncryptionPadding.OaepSHA256);
            }, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (CryptographicException ex)
        {
            _logger.LogWarning(ex, "TPM/Hello unwrap failed. User might have cancelled or hardware changed.");
            return null;
        }
    }

    public async Task RemoveKeyAsync(string keyId, CancellationToken ct = default)
    {
        await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            if (!CngKey.Exists(keyId, CngProvider.MicrosoftPlatformCryptoProvider)) return;
            using var key = CngKey.Open(keyId, CngProvider.MicrosoftPlatformCryptoProvider);
            key.Delete();
        }, ct);
    }
}
