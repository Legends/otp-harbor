using FluentResults;
using TOTP.Core.Models;

namespace TOTP.Core.Security.Interfaces;

public interface IAppPreferencesStore : IDisposable
{
    Task<Result<AppPreferencesV1?>> LoadAsync(CancellationToken cancellationToken = default);
    Task<Result> SaveAsync(AppPreferencesV1 preferences, CancellationToken cancellationToken = default);
}
