using FluentResults;
using System.Linq;

namespace TOTP.Core.Common;

public static class FluentResultExtensions
{
    public static AppErrorCode GetErrorCode(this IResultBase result)
    {
        if (result.IsSuccess)
        {
            return AppErrorCode.Unknown;
        }

        var appError = result.Errors.OfType<AppError>().FirstOrDefault();
        if (appError != null)
        {
            return appError.Code;
        }

        var metadataCode = result.Errors
            .Select(error => error.Metadata.TryGetValue(AppError.ErrorCodeMetadataKey, out var code) ? code : null)
            .OfType<AppErrorCode>()
            .FirstOrDefault();

        return metadataCode == default ? AppErrorCode.Unknown : metadataCode;
    }
}
