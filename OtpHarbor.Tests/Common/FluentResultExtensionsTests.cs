using FluentResults;
using TOTP.Core.Common;

namespace TOTP.Tests.Common;

public sealed class FluentResultExtensionsTests
{
    [Fact]
    public void GetErrorCode_WhenSuccess_ReturnsUnknown()
    {
        var result = Result.Ok();

        Assert.Equal(AppErrorCode.Unknown, result.GetErrorCode());
    }

    [Fact]
    public void GetErrorCode_PrefersAppErrorInstance()
    {
        var result = Result.Fail(new AppError(AppErrorCode.AccountsUpdateFailed, "err"));

        Assert.Equal(AppErrorCode.AccountsUpdateFailed, result.GetErrorCode());
    }

    [Fact]
    public void GetErrorCode_UsesMetadataFallback()
    {
        var error = new Error("x");
        error.Metadata[AppError.ErrorCodeMetadataKey] = AppErrorCode.ImportInvalidPayload;
        var result = Result.Fail(error);

        Assert.Equal(AppErrorCode.ImportInvalidPayload, result.GetErrorCode());
    }
}
