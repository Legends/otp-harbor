using FluentResults;
using TOTP.Core.Security.Models;

namespace TOTP.Core.Security.Interfaces;

public interface IAuthorizationEnvelopeStore : IDisposable
{
    Task<Result<AuthorizationEnvelopeV2?>> LoadAsync(CancellationToken cancellationToken = default);
    Task<Result> SaveAsync(AuthorizationEnvelopeV2 envelope, CancellationToken cancellationToken = default);
}
