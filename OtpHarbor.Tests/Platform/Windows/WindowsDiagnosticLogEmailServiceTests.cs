using Moq;
using TOTP.Core.Services.Models;
using TOTP.Platform.Windows;
using TOTP.Tests.Common;

namespace TOTP.Tests.Platform.Windows;

public sealed class WindowsDiagnosticLogEmailServiceTests
{
    [Fact]
    public async Task OpenDraftAsync_CreatesPrivateSnapshotAndAttachesItToSupportDraft()
    {
        var root = Path.Combine(Path.GetTempPath(), $"otp-harbor-email-log-{Guid.NewGuid():N}");
        try
        {
            var paths = new WindowsApplicationPaths(root, root);
            Directory.CreateDirectory(paths.LogDirectory);
            var logPath = Path.Combine(paths.LogDirectory, "app20261105.log");
            await File.WriteAllTextAsync(logPath, "redacted diagnostic entry",
                TestContext.Current.CancellationToken);
            DiagnosticLogEmailRequest? composedRequest = null;
            string? snapshotContents = null;
            var composer = new Mock<IWindowsEmailComposer>();
            composer.Setup(value => value.OpenDraftWithAttachmentAsync(
                    It.IsAny<DiagnosticLogEmailRequest>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .Callback<DiagnosticLogEmailRequest, string, CancellationToken>(
                    (request, snapshotPath, _) =>
                    {
                        composedRequest = request;
                        snapshotContents = File.ReadAllText(snapshotPath);
                    })
                .Returns(Task.CompletedTask);
            var sut = new WindowsDiagnosticLogEmailService(
                paths, NoOpPlatformFileSecurity.Instance, composer.Object);
            var request = new DiagnosticLogEmailRequest(
                logPath,
                "otp-harbor-diagnostic.log",
                DiagnosticSupportContact.EmailAddress,
                "subject",
                "body",
                "chooser");

            var result = await sut.OpenDraftAsync(request, TestContext.Current.CancellationToken);

            Assert.True(result.IsSuccess);
            Assert.Equal(DiagnosticLogEmailOutcome.AttachmentIncluded, result.Value);
            Assert.Equal(DiagnosticSupportContact.EmailAddress, composedRequest?.Recipient);
            Assert.Equal(request.AttachmentFileName, composedRequest?.AttachmentFileName);
            Assert.Equal("redacted diagnostic entry", snapshotContents);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task OpenDraftAsync_RejectsFilesOutsideTheApplicationLogDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), $"otp-harbor-email-boundary-{Guid.NewGuid():N}");
        try
        {
            var paths = new WindowsApplicationPaths(root, root);
            Directory.CreateDirectory(paths.ApplicationDataDirectory);
            var outsidePath = Path.Combine(root, "not-a-log.log");
            await File.WriteAllTextAsync(outsidePath, "synthetic",
                TestContext.Current.CancellationToken);
            var composer = new Mock<IWindowsEmailComposer>();
            var sut = new WindowsDiagnosticLogEmailService(
                paths, NoOpPlatformFileSecurity.Instance, composer.Object);

            var result = await sut.OpenDraftAsync(new DiagnosticLogEmailRequest(
                outsidePath,
                "otp-harbor-diagnostic.log",
                DiagnosticSupportContact.EmailAddress,
                "subject",
                "body",
                "chooser"), TestContext.Current.CancellationToken);

            Assert.True(result.IsFailed);
            composer.Verify(value => value.OpenDraftWithAttachmentAsync(
                It.IsAny<DiagnosticLogEmailRequest>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task OpenDraftAsync_WhenAttachmentComposerIsUnavailable_OpensManualDraft()
    {
        var root = Path.Combine(Path.GetTempPath(), $"otp-harbor-email-fallback-{Guid.NewGuid():N}");
        try
        {
            var paths = new WindowsApplicationPaths(root, root);
            Directory.CreateDirectory(paths.LogDirectory);
            var logPath = Path.Combine(paths.LogDirectory, "app.log");
            await File.WriteAllTextAsync(logPath, "redacted",
                TestContext.Current.CancellationToken);
            var composer = new Mock<IWindowsEmailComposer>();
            composer.Setup(value => value.OpenDraftWithAttachmentAsync(
                    It.IsAny<DiagnosticLogEmailRequest>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("synthetic unavailable"));
            composer.Setup(value => value.OpenDraftWithoutAttachmentAsync(
                    It.IsAny<DiagnosticLogEmailRequest>(),
                    It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            var sut = new WindowsDiagnosticLogEmailService(
                paths, NoOpPlatformFileSecurity.Instance, composer.Object);

            var result = await sut.OpenDraftAsync(new DiagnosticLogEmailRequest(
                logPath,
                "otp-harbor-diagnostic.log",
                DiagnosticSupportContact.EmailAddress,
                "subject",
                "body",
                "chooser"), TestContext.Current.CancellationToken);

            Assert.True(result.IsSuccess);
            Assert.Equal(DiagnosticLogEmailOutcome.ManualAttachmentRequired, result.Value);
            composer.Verify(value => value.OpenDraftWithoutAttachmentAsync(
                It.Is<DiagnosticLogEmailRequest>(request =>
                    request.Recipient == DiagnosticSupportContact.EmailAddress),
                It.IsAny<CancellationToken>()), Times.Once);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
