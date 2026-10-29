using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using TOTP.Core.Common;

namespace TOTP.Tests.Common;

public sealed class ErrorMapperReflectionTests
{
    [Theory]
    [InlineData("TOTP.Infrastructure.Common.ExportServiceErrorMapper", "MapExportError", typeof(UnauthorizedAccessException), AppErrorCode.ExportFileAccessDenied)]
    [InlineData("TOTP.Infrastructure.Common.ExportServiceErrorMapper", "MapExportError", typeof(DirectoryNotFoundException), AppErrorCode.ExportFileWriteFailed)]
    [InlineData("TOTP.Infrastructure.Common.ExportServiceErrorMapper", "MapExportError", typeof(IOException), AppErrorCode.ExportFileWriteFailed)]
    [InlineData("TOTP.Infrastructure.Common.ExportServiceErrorMapper", "MapExportError", typeof(CryptographicException), AppErrorCode.ExportEncryptionFailed)]
    [InlineData("TOTP.Infrastructure.Common.ExportServiceErrorMapper", "MapExportError", typeof(Exception), AppErrorCode.ExportUnknownFailed)]
    [InlineData("TOTP.Infrastructure.Common.ExportServiceErrorMapper", "MapImportError", typeof(FileNotFoundException), AppErrorCode.ImportFileNotFound)]
    [InlineData("TOTP.Infrastructure.Common.ExportServiceErrorMapper", "MapImportError", typeof(DirectoryNotFoundException), AppErrorCode.ImportFileNotFound)]
    [InlineData("TOTP.Infrastructure.Common.ExportServiceErrorMapper", "MapImportError", typeof(UnauthorizedAccessException), AppErrorCode.ExportFileAccessDenied)]
    [InlineData("TOTP.Infrastructure.Common.ExportServiceErrorMapper", "MapImportError", typeof(CryptographicException), AppErrorCode.ImportWrongPasswordOrTampered)]
    [InlineData("TOTP.Infrastructure.Common.ExportServiceErrorMapper", "MapImportError", typeof(JsonException), AppErrorCode.ImportInvalidPayload)]
    [InlineData("TOTP.Infrastructure.Common.ExportServiceErrorMapper", "MapImportError", typeof(Exception), AppErrorCode.ImportUnknownFailed)]
    [InlineData("TOTP.DAL.Common.AccountDalErrorMapper", "MapReadError", typeof(UnauthorizedAccessException), AppErrorCode.OtpStorageAccessDenied)]
    [InlineData("TOTP.DAL.Common.AccountDalErrorMapper", "MapReadError", typeof(CryptographicException), AppErrorCode.OtpStorageDecryptFailed)]
    [InlineData("TOTP.DAL.Common.AccountDalErrorMapper", "MapReadError", typeof(IOException), AppErrorCode.OtpStorageReadFailed)]
    [InlineData("TOTP.DAL.Common.AccountDalErrorMapper", "MapReadError", typeof(Exception), AppErrorCode.OtpStorageReadFailed)]
    [InlineData("TOTP.DAL.Common.AccountDalErrorMapper", "MapExportError", typeof(UnauthorizedAccessException), AppErrorCode.ExportFileAccessDenied)]
    [InlineData("TOTP.DAL.Common.AccountDalErrorMapper", "MapExportError", typeof(DirectoryNotFoundException), AppErrorCode.ExportFileWriteFailed)]
    [InlineData("TOTP.DAL.Common.AccountDalErrorMapper", "MapExportError", typeof(CryptographicException), AppErrorCode.ExportEncryptionFailed)]
    [InlineData("TOTP.DAL.Common.AccountDalErrorMapper", "MapExportError", typeof(IOException), AppErrorCode.ExportFileWriteFailed)]
    [InlineData("TOTP.DAL.Common.AccountDalErrorMapper", "MapExportError", typeof(Exception), AppErrorCode.ExportUnknownFailed)]
    public void InternalErrorMappers_MapExpectedCodes(string typeName, string methodName, Type exceptionType, AppErrorCode expected)
    {
        var ex = (Exception)Activator.CreateInstance(exceptionType)!;
        var mapped = InvokeMapper(typeName, methodName, [ex]);

        Assert.Equal(expected, mapped.Code);
    }

    [Fact]
    public void OtpDal_MapWriteError_UsesOperationCodeForUnknownException()
    {
        var opCode = AppErrorCode.OtpDeleteFailed;
        var mapped = InvokeMapper(
            "TOTP.DAL.Common.AccountDalErrorMapper",
            "MapWriteError",
            [new Exception("x"), opCode, "operation failed"]);

        Assert.Equal(opCode, mapped.Code);
    }

    [Theory]
    [InlineData(typeof(UnauthorizedAccessException), AppErrorCode.OtpStorageAccessDenied)]
    [InlineData(typeof(CryptographicException), AppErrorCode.OtpStorageEncryptionFailed)]
    [InlineData(typeof(IOException), AppErrorCode.OtpStorageWriteFailed)]
    public void OtpDal_MapWriteError_MapsKnownExceptions(Type exceptionType, AppErrorCode expected)
    {
        var ex = (Exception)Activator.CreateInstance(exceptionType)!;
        var mapped = InvokeMapper(
            "TOTP.DAL.Common.AccountDalErrorMapper",
            "MapWriteError",
            [ex, AppErrorCode.OtpCreateFailed, "ignored"]);

        Assert.Equal(expected, mapped.Code);
    }

    private static AppError InvokeMapper(string typeName, string methodName, object[] args)
    {
        var type = AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetType(typeName, throwOnError: false))
            .FirstOrDefault(t => t is not null);

        if (type is null)
        {
            var baseDir = AppContext.BaseDirectory;
            foreach (var dll in Directory.EnumerateFiles(baseDir, "*.dll"))
            {
                try { _ = Assembly.LoadFrom(dll); } catch { }
            }

            type = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType(typeName, throwOnError: false))
                .FirstOrDefault(t => t is not null);
        }

        if (type is null)
        {
            throw new InvalidOperationException($"Type not found: {typeName}");
        }

        var method = type.GetMethod(methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException($"Method not found: {typeName}.{methodName}");

        return (AppError)(method.Invoke(null, args)
            ?? throw new InvalidOperationException("Mapper returned null."));
    }
}
