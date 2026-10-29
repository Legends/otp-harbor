using FluentResults;

namespace TOTP.Core.Security.Interfaces;

public interface IPlatformUnattendedUnlockEnrollment : IDisposable
{
    Task<Result> EnableAsync(
        string recoveryPassword,
        CancellationToken cancellationToken = default);

    Task<Result> DisableAsync(CancellationToken cancellationToken = default);
}
