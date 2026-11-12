using TOTP.Avalonia.Shared.Import;
using Moq;
using TOTP.Core.Services.Interfaces;
using TOTP.Core.Services.Models;
using TOTP.Avalonia.Desktop.Platform;

namespace TOTP.Tests.TestUtilities;

internal static class ImportReviewTestActions
{
    public static void BridgeFileSummaryDialog(IAvaloniaDialogService service)
    {
        if (service is not IMocked<IAvaloniaDialogService> mocked) return;
        mocked.Mock.Setup(value => value.ConfirmImportFileAsync(It.IsAny<AuthenticatorImportViewModel>(),
                It.IsAny<AccountImportReadSummary>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((AuthenticatorImportViewModel model, AccountImportReadSummary summary, string name, CancellationToken token) =>
                model.WaitForFileReviewAsync(summary, name, token));
    }
    public static void BridgeDetectedRead(IExportService service)
    {
        if (service is not IMocked<IExportService> mocked) return;
        mocked.Mock.Setup(value => value.ImportFromStreamWithFormatSelectionAsync(It.IsAny<Stream>(), It.IsAny<string>(),
                It.IsAny<Func<IReadOnlyList<AccountImportFormat>, CancellationToken, Task<AccountImportFormat?>>>(), It.IsAny<CancellationToken>()))
            .Returns((Stream stream, string name, Func<IReadOnlyList<AccountImportFormat>, CancellationToken, Task<AccountImportFormat?>> _, CancellationToken token) =>
                service.ImportFromStreamAsync(stream, name, cancellationToken: token));
    }
    public static void ReviewFirstSelectedFile(AuthenticatorImportViewModel model)
    {
        var reviews = 0;
        model.PropertyChanged += (_, _) =>
        {
            if (!model.HasSelectedFile) return;
            if (reviews++ == 0) model.ReviewFileCommand.Execute(null);
            else model.ClearFileCommand.Execute(null);
        };
    }
}
