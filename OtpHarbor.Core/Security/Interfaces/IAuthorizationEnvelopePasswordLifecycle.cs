using FluentResults;
using TOTP.Core.Security.Models;

namespace TOTP.Core.Security.Interfaces;

public interface IAuthorizationEnvelopePasswordLifecycle : IDisposable
{
    Task<Result<SensitiveBuffer>> ConfigureAsync(
        string recoveryPassword,
        CancellationToken cancellationToken = default);

    Task<Result<SensitiveBuffer>> ChangePasswordAsync(
        string currentRecoveryPassword,
        string newRecoveryPassword,
        CancellationToken cancellationToken = default);
}
