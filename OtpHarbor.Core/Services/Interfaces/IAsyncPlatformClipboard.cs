using FluentResults;
using TOTP.Core.Services.Models;

namespace TOTP.Core.Services.Interfaces;

public interface IAsyncPlatformClipboard
{
    ClipboardCapabilities Capabilities { get; }
    Task<Result<ClipboardWriteReceipt>> SetTextAsync(
        string text,
        CancellationToken cancellationToken = default);
    Task<Result<bool>> ClearIfUnchangedAsync(
        ClipboardWriteReceipt receipt,
        CancellationToken cancellationToken = default);
}
