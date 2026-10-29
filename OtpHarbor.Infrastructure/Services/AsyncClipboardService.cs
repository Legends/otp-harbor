using FluentResults;
using Microsoft.Extensions.Logging;
using TOTP.Core.Common;
using TOTP.Core.Services.Interfaces;
using TOTP.Core.Services.Models;

namespace TOTP.Infrastructure.Services;

public sealed class AsyncClipboardService(
    IAsyncPlatformClipboard platformClipboard,
    ILogger<AsyncClipboardService> logger) : IAsyncClipboardService, IDisposable
{
    private static readonly ClipboardCapabilities RequiredCapabilities =
        ClipboardCapabilities.WriteText | ClipboardCapabilities.ConditionalClear;

    private readonly object _sync = new();
    private CancellationTokenSource? _scheduledClear;

    public ClipboardCapabilities Capabilities => platformClipboard.Capabilities;

    public async Task<Result> CopyAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(text)) return Result.Ok();
        if (!Capabilities.HasFlag(ClipboardCapabilities.WriteText))
        {
            return Result.Fail(new AppError(
                AppErrorCode.ClipboardUnavailable,
                "The platform clipboard does not support text copying."));
        }

        var write = await platformClipboard.SetTextAsync(text, cancellationToken);
        if (write.IsFailed) return Result.Fail(write.Errors);

        CancelScheduledClear();
        logger.LogInformation("Sensitive clipboard data copied without an automatic clear.");
        return Result.Ok();
    }

    public async Task<Result> CopyAndScheduleClearAsync(
        string text,
        TimeSpan duration,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(text)) return Result.Ok();
        if (duration <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(duration));
        if ((Capabilities & RequiredCapabilities) != RequiredCapabilities)
        {
            return Result.Fail(new AppError(
                AppErrorCode.ClipboardUnavailable,
                "The platform clipboard does not support safe conditional clearing."));
        }

        var write = await platformClipboard.SetTextAsync(text, cancellationToken);
        if (write.IsFailed) return Result.Fail(write.Errors);

        CancellationTokenSource schedule;
        lock (_sync)
        {
            _scheduledClear?.Cancel();
            _scheduledClear?.Dispose();
            _scheduledClear = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            schedule = _scheduledClear;
        }

        _ = ClearAfterAsync(write.Value, duration, schedule);
        logger.LogInformation(
            "Sensitive clipboard data copied with an automatic clear scheduled after {DurationSeconds} seconds.",
            duration.TotalSeconds);
        return Result.Ok();
    }

    private async Task ClearAfterAsync(
        ClipboardWriteReceipt receipt,
        TimeSpan duration,
        CancellationTokenSource schedule)
    {
        try
        {
            await Task.Delay(duration, schedule.Token);
            var cleared = await platformClipboard.ClearIfUnchangedAsync(receipt, schedule.Token);
            if (cleared.IsFailed)
            {
                logger.LogWarning("Scheduled asynchronous clipboard clear failed.");
            }
            else
            {
                logger.LogInformation(cleared.Value
                    ? "Clipboard cleared automatically."
                    : "Clipboard changed before timeout; automatic clear was skipped.");
            }
        }
        catch (OperationCanceledException) when (schedule.IsCancellationRequested)
        {
        }
        finally
        {
            lock (_sync)
            {
                if (ReferenceEquals(_scheduledClear, schedule))
                {
                    _scheduledClear.Dispose();
                    _scheduledClear = null;
                }
            }
        }
    }

    public void Dispose()
    {
        CancelScheduledClear();
    }

    private void CancelScheduledClear()
    {
        lock (_sync)
        {
            _scheduledClear?.Cancel();
            _scheduledClear?.Dispose();
            _scheduledClear = null;
        }
    }
}
