using FluentResults;
using TOTP.Core.Services.Interfaces;
using TOTP.Core.Services.Models;

namespace TOTP.Infrastructure.Services;

public sealed class UnavailableUpdateInstallerLauncher : IUpdateInstallerLauncher
{
    public bool IsSupported => false;

    public Task<Result> LaunchAsync(
        PortableUpdatePackage package,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Result.Fail(
            "Update installation is unavailable for this desktop package."));
    }
}
