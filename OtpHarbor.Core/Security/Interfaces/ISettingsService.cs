using FluentResults;

namespace TOTP.Core.Security.Interfaces;

public interface ISettingsService
{
    IAppSettings Current { get; }
    Task<Result<IAppSettings>> LoadAsync();
    Task<Result> SaveAsync();
    //Task InitializeAsync();
}
