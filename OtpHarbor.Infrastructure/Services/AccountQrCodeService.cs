using System.Security.Cryptography;
using FluentResults;
using TOTP.Core.Security.Models;
using TOTP.Core.Services.Interfaces;

namespace TOTP.Infrastructure.Services;

public sealed class AccountQrCodeService(
    IAccountManager accountManager,
    IQrCodeService qrCodeService) : IAccountQrCodeService
{
    public async Task<Result<SensitiveBuffer>> GenerateAsync(Guid accountId)
    {
        if (accountId == Guid.Empty)
            return Result.Fail<SensitiveBuffer>("An account must be selected.");

        var accounts = await accountManager.GetAllOtpEntriesSortedAsync();
        if (accounts.IsFailed) return Result.Fail<SensitiveBuffer>(accounts.Errors);

        var account = accounts.Value.FirstOrDefault(candidate => candidate.ID == accountId);
        if (account is null)
            return Result.Fail<SensitiveBuffer>("The selected account no longer exists.");

        byte[]? png = null;
        try
        {
            var uri = qrCodeService.BuildOtpAuthUri(
                account.Issuer,
                account.Secret,
                account.AccountName,
                account.PeriodSeconds);
            png = qrCodeService.GenerateQr(uri);
            return Result.Ok(SensitiveBuffer.CopyFrom(png));
        }
        catch (Exception)
        {
            return Result.Fail<SensitiveBuffer>("A QR code could not be generated.");
        }
        finally
        {
            if (png is not null) CryptographicOperations.ZeroMemory(png);
        }
    }
}
