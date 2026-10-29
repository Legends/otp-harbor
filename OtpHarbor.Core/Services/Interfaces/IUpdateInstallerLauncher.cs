using FluentResults;
using TOTP.Core.Services.Models;

namespace TOTP.Core.Services.Interfaces;

public interface IUpdateInstallerLauncher
{
    bool IsSupported { get; }
    Task<Result> LaunchAsync(
        PortableUpdatePackage package,
        CancellationToken cancellationToken = default);
}
