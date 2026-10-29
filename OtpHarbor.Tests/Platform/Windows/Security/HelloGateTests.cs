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
    public async Task RequestVerificationAsync_MapsEveryResult(
        UserConsentVerificationResult verification, AuthorizationResult expected)
    {
        var provider = new Mock<IHelloPromptWindowHandleProvider>();
        provider.SetupGet(p => p.VerificationMessage).Returns("Localized verification");
        var requester = new Mock<IHelloVerificationRequester>();
        requester.Setup(r => r.RequestAsync(nint.Zero, "Localized verification", It.IsAny<CancellationToken>()))
            .ReturnsAsync(verification);
        var sut = new HelloGate(Mock.Of<ILogger<HelloGate>>(), provider.Object, requester.Object);

        Assert.Equal(expected, await sut.RequestVerificationAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public void BeginPromptSession_DelegatesPromptLifecycleToOwnerProvider()
    {
        var scope = new Mock<IDisposable>();
        var provider = new Mock<IHelloPromptWindowHandleProvider>();
        provider.Setup(p => p.BeginPrompt()).Returns(scope.Object);
        var sut = new HelloGate(
            Mock.Of<ILogger<HelloGate>>(),
            provider.Object,
            Mock.Of<IHelloVerificationRequester>());

        using (sut.BeginPromptSession())
        {
            provider.Verify(p => p.BeginPrompt(), Times.Once);
            scope.Verify(s => s.Dispose(), Times.Never);
        }

        scope.Verify(s => s.Dispose(), Times.Once);
    }

    [Fact]
    public async Task RequestVerificationAsync_WhenRequesterThrows_PropagatesException()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var provider = new Mock<IHelloPromptWindowHandleProvider>();
        var requester = new Mock<IHelloVerificationRequester>();
        requester.Setup(r => r.RequestAsync(It.IsAny<nint>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());
        var sut = new HelloGate(Mock.Of<ILogger<HelloGate>>(), provider.Object, requester.Object);

        await Assert.ThrowsAsync<OperationCanceledException>(() => sut.RequestVerificationAsync(cancellationToken));
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
