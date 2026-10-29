using FluentResults;
using TOTP.Core.Security.Models;

namespace TOTP.Core.Services.Interfaces;

public interface IAccountQrCodeService
{
    Task<Result<SensitiveBuffer>> GenerateAsync(Guid accountId);
}
