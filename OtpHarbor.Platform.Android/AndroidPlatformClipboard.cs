using System.Security.Cryptography;
using System.Text;
using Android.Content;
using Android.OS;
using FluentResults;
using Microsoft.Extensions.Logging;
using TOTP.Core.Common;
using TOTP.Core.Services.Interfaces;
using TOTP.Core.Services.Models;
using Result = FluentResults.Result;

namespace TOTP.Platform.Android;

public sealed class AndroidPlatformClipboard : IAsyncPlatformClipboard, IDisposable
{
    private const string SensitiveClipboardExtra = "android.content.extra.IS_SENSITIVE";

    private readonly ClipboardManager _clipboard;
    private readonly ILogger<AndroidPlatformClipboard> _logger;
    private readonly object _sync = new();
    private ulong _nextToken;
    private ClipboardWriteReceipt? _receipt;
    private byte[]? _ownedTextHash;

    public AndroidPlatformClipboard(
        Context context,
        ILogger<AndroidPlatformClipboard> logger)
    {
        ArgumentNullException.ThrowIfNull(context);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _clipboard = context.GetSystemService(Context.ClipboardService) as ClipboardManager
            ?? throw new InvalidOperationException("The Android clipboard service is unavailable.");
    }

    public ClipboardCapabilities Capabilities =>
        ClipboardCapabilities.WriteText | ClipboardCapabilities.ConditionalClear;

    public Task<Result<ClipboardWriteReceipt>> SetTextAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var clip = ClipData.NewPlainText("OTP Harbor", text)
                ?? throw new InvalidOperationException("The Android clipboard payload could not be created.");
            if (OperatingSystem.IsAndroidVersionAtLeast(33))
            {
                var extras = new PersistableBundle();
                extras.PutBoolean(SensitiveClipboardExtra, true);
                var description = clip.Description
                    ?? throw new InvalidOperationException("The Android clipboard description is unavailable.");
                description.Extras = extras;
            }

            _clipboard.PrimaryClip = clip;
            var hash = HashText(text);
            lock (_sync)
            {
                ClearOwnedHash();
                var receipt = new ClipboardWriteReceipt(++_nextToken);
                _receipt = receipt;
                _ownedTextHash = hash;
                return Task.FromResult(Result.Ok(receipt));
            }
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                "Android clipboard write failed with exception type {ExceptionType}.",
                exception.GetType().FullName);
            return Task.FromResult(Result.Fail<ClipboardWriteReceipt>(new AppError(
                AppErrorCode.ClipboardWriteFailed,
                "Clipboard text could not be written.")));
        }
    }

    public Task<Result<bool>> ClearIfUnchangedAsync(
        ClipboardWriteReceipt receipt,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        byte[]? expectedHash;
        lock (_sync)
        {
            if (_receipt != receipt || _ownedTextHash is null)
                return Task.FromResult(Result.Ok(false));
            expectedHash = (byte[])_ownedTextHash.Clone();
        }

        try
        {
            var currentText = _clipboard.PrimaryClip?.GetItemAt(0)?.CoerceToText(
                global::Android.App.Application.Context)?.ToString();
            if (currentText is null) return Task.FromResult(Result.Ok(false));

            var currentHash = HashText(currentText);
            try
            {
                if (!CryptographicOperations.FixedTimeEquals(expectedHash, currentHash))
                {
                    ReleaseReceipt(receipt);
                    return Task.FromResult(Result.Ok(false));
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(currentHash);
            }

            _clipboard.ClearPrimaryClip();
            ReleaseReceipt(receipt);
            return Task.FromResult(Result.Ok(true));
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                "Android clipboard clear failed with exception type {ExceptionType}.",
                exception.GetType().FullName);
            return Task.FromResult(Result.Fail<bool>(new AppError(
                AppErrorCode.ClipboardClearFailed,
                "Clipboard text could not be cleared.")));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(expectedHash);
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _receipt = null;
            ClearOwnedHash();
        }
    }

    private static byte[] HashText(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        try
        {
            return SHA256.HashData(bytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    private void ReleaseReceipt(ClipboardWriteReceipt receipt)
    {
        lock (_sync)
        {
            if (_receipt != receipt) return;
            _receipt = null;
            ClearOwnedHash();
        }
    }

    private void ClearOwnedHash()
    {
        if (_ownedTextHash is not null)
            CryptographicOperations.ZeroMemory(_ownedTextHash);
        _ownedTextHash = null;
    }
}
