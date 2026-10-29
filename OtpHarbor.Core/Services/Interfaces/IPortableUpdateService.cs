using FluentResults;
using TOTP.Core.Services.Models;

namespace TOTP.Core.Services.Interfaces;

public interface IPortableUpdateService
{
    Task<Result<PortableUpdateCheckResult>> CheckAsync(CancellationToken cancellationToken = default);

    Task<Result<PortableUpdatePackage>> DownloadAsync(
        PortableUpdateOffer offer,
        IProgress<PortableUpdateDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
