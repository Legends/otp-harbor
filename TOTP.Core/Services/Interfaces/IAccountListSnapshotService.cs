using FluentResults;
using TOTP.Core.Models;

namespace TOTP.Core.Services.Interfaces;

public interface IAccountListSnapshotService
{
    Task<Result<AccountListSnapshot>> LoadAsync();
}
