using Microsoft.Extensions.Logging;
using Moq;
using TOTP.Core.Security.Interfaces;
using TOTP.Core.Security.Models;
using TOTP.Platform.Windows.Security;
using Windows.Security.Credentials.UI;

namespace TOTP.Tests.Platform.Windows.Security;

public sealed class HelloGateTests
{
    [Theory]
    [InlineData(UserConsentVerificationResult.Verified, AuthorizationResult.Success)]
    [InlineData(UserConsentVerificationResult.Canceled, AuthorizationResult.Cancelled)]
    [InlineData(UserConsentVerificationResult.DeviceBusy, AuthorizationResult.NotAvailable)]
    public async Task RequestVerificationAsync_RestoresPromptOwnerForEveryResult(
        UserConsentVerificationResult verification, AuthorizationResult expected)
    {
        var scope = new Mock<IDisposable>();
        var provider = new Mock<IHelloPromptWindowHandleProvider>();
        provider.Setup(p => p.BeginPrompt()).Returns(scope.Object);
        provider.SetupGet(p => p.VerificationMessage).Returns("Localized verification");
        var requester = new Mock<IHelloVerificationRequester>();
        requester.Setup(r => r.RequestAsync(nint.Zero, "Localized verification", It.IsAny<CancellationToken>()))
            .ReturnsAsync(verification);
        var sut = new HelloGate(Mock.Of<ILogger<HelloGate>>(), provider.Object, requester.Object);

        Assert.Equal(expected, await sut.RequestVerificationAsync(TestContext.Current.CancellationToken));

        scope.Verify(s => s.Dispose(), Times.Once);
    }

    [Fact]
    public async Task RequestVerificationAsync_WhenPromptThrows_RestoresOwner()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var scope = new Mock<IDisposable>();
        var provider = new Mock<IHelloPromptWindowHandleProvider>();
        provider.Setup(p => p.BeginPrompt()).Returns(scope.Object);
        var requester = new Mock<IHelloVerificationRequester>();
        requester.Setup(r => r.RequestAsync(It.IsAny<nint>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());
        var sut = new HelloGate(Mock.Of<ILogger<HelloGate>>(), provider.Object, requester.Object);

        await Assert.ThrowsAsync<OperationCanceledException>(() => sut.RequestVerificationAsync(cancellationToken));

        scope.Verify(s => s.Dispose(), Times.Once);
    }

    [Fact]
    public async Task RequestVerificationAsync_UsesActiveApplicationWindowHandle()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var expectedHandle = new nint(1234);
        var handleProvider = new Mock<IHelloPromptWindowHandleProvider>();
        handleProvider.Setup(p => p.GetActiveWindowHandle()).Returns(expectedHandle);
        handleProvider.SetupGet(p => p.VerificationMessage).Returns("Unlock OTP Harbor Vault");
        var requester = new Mock<IHelloVerificationRequester>();
        requester
            .Setup(r => r.RequestAsync(expectedHandle, "Unlock OTP Harbor Vault", cancellationToken))
            .ReturnsAsync(UserConsentVerificationResult.Verified);
        var sut = new HelloGate(
            Mock.Of<ILogger<HelloGate>>(),
            handleProvider.Object,
            requester.Object);

        var result = await sut.RequestVerificationAsync(cancellationToken);

        Assert.Equal(AuthorizationResult.Success, result);
        requester.Verify(
            r => r.RequestAsync(expectedHandle, "Unlock OTP Harbor Vault", cancellationToken),
            Times.Once);
    }

    [Fact]
    public async Task RequestVerificationAsync_WhenSystemPromptIsCancelled_ReturnsCancelled()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var requester = new Mock<IHelloVerificationRequester>();
        requester
            .Setup(r => r.RequestAsync(It.IsAny<nint>(), It.IsAny<string>(), cancellationToken))
            .ReturnsAsync(UserConsentVerificationResult.Canceled);
        var sut = new HelloGate(
            Mock.Of<ILogger<HelloGate>>(),
            Mock.Of<IHelloPromptWindowHandleProvider>(),
            requester.Object);

        var result = await sut.RequestVerificationAsync(cancellationToken);

        Assert.Equal(AuthorizationResult.Cancelled, result);
    }
}
