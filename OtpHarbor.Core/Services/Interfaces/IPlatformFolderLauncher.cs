using FluentResults;

namespace TOTP.Core.Services.Interfaces;

public interface IPlatformFolderLauncher
{
    Task<Result> OpenFolderAsync(string path, CancellationToken cancellationToken = default);
}
