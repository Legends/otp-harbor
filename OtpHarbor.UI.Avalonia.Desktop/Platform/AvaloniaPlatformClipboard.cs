using Avalonia.Input;
using FluentResults;
using Microsoft.Extensions.Logging;
using TOTP.Core.Common;
using TOTP.Core.Services.Interfaces;
using TOTP.Core.Services.Models;

namespace TOTP.Avalonia.Desktop.Platform;

public sealed class AvaloniaPlatformClipboard(
    AvaloniaClipboardAccessor accessor,
    bool supportsOwnership,
    ILogger<AvaloniaPlatformClipboard> logger) : IAsyncPlatformClipboard, IDisposable
{
    private readonly object _sync = new();
    private ulong _nextToken;
    private ClipboardWriteReceipt? _receipt;
    private IAsyncDataTransfer? _ownedTransfer;

    public ClipboardCapabilities Capabilities => supportsOwnership
        ? ClipboardCapabilities.WriteText | ClipboardCapabilities.ConditionalClear
        : ClipboardCapabilities.WriteText;

    public async Task<Result<ClipboardWriteReceipt>> SetTextAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var clipboard = accessor.Current;
        if (clipboard is null)
            return Result.Fail<ClipboardWriteReceipt>(Unavailable());

        try
        {
            var transfer = new DataTransfer();
            transfer.Add(DataTransferItem.CreateText(text));
            await clipboard.SetDataAsync(transfer);
            cancellationToken.ThrowIfCancellationRequested();

            lock (_sync)
            {
                var receipt = new ClipboardWriteReceipt(++_nextToken);
                _receipt = receipt;
                _ownedTransfer = transfer;
                return Result.Ok(receipt);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                "Avalonia clipboard write failed with exception type {ExceptionType}.",
                exception.GetType().FullName);
            return Result.Fail<ClipboardWriteReceipt>(new AppError(
                AppErrorCode.ClipboardWriteFailed,
                "Clipboard text could not be written."));
        }
    }

    public async Task<Result<bool>> ClearIfUnchangedAsync(
        ClipboardWriteReceipt receipt,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!supportsOwnership) return Result.Fail<bool>(Unavailable());

        var clipboard = accessor.Current;
        IAsyncDataTransfer? expected;
        lock (_sync)
        {
            if (_receipt != receipt) return Result.Ok(false);
            expected = _ownedTransfer;
        }

        if (clipboard is null || expected is null)
            return Result.Fail<bool>(Unavailable());

        try
        {
            var current = await clipboard.TryGetInProcessDataAsync();
            if (!ReferenceEquals(current, expected))
            {
                ReleaseReceipt(receipt);
                return Result.Ok(false);
            }

            await clipboard.ClearAsync();
            cancellationToken.ThrowIfCancellationRequested();
            ReleaseReceipt(receipt);

            return Result.Ok(true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                "Avalonia clipboard clear failed with exception type {ExceptionType}.",
                exception.GetType().FullName);
            return Result.Fail<bool>(new AppError(
                AppErrorCode.ClipboardClearFailed,
                "Clipboard text could not be cleared."));
        }
    }

    private static AppError Unavailable() => new(
        AppErrorCode.ClipboardUnavailable,
        "Safe conditional clipboard access is unavailable.");

    private void ReleaseReceipt(ClipboardWriteReceipt receipt)
    {
        lock (_sync)
        {
            if (_receipt != receipt) return;
            _receipt = null;
            _ownedTransfer = null;
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _receipt = null;
            _ownedTransfer = null;
        }
    }
}
