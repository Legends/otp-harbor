using Avalonia.Input;
using Avalonia.Input.Platform;
using Microsoft.Extensions.Logging;
using Moq;
using TOTP.Core.Services.Models;
using TOTP.Avalonia.Desktop.Platform;

namespace TOTP.Tests.Avalonia.Platform;

public sealed class AvaloniaPlatformClipboardTests
{
    [Theory]
    [InlineData(true, false, false, null, true)]
    [InlineData(false, true, false, null, true)]
    [InlineData(false, false, true, ":0", true)]
    [InlineData(false, false, true, "", false)]
    [InlineData(false, false, true, null, false)]
    public void OwnershipPolicy_UsesTheActualX11DisplayInsteadOfSessionType(
        bool isWindows,
        bool isMacOS,
        bool isLinux,
        string? x11Display,
        bool expected)
    {
        Assert.Equal(
            expected,
            AvaloniaClipboardOwnershipPolicy.IsSupported(
                isWindows,
                isMacOS,
                isLinux,
                x11Display));
    }

    [Fact]
    public async Task ClearIfUnchangedAsync_WhenOwnedTransferRemainsCurrent_ClearsClipboard()
    {
        IAsyncDataTransfer? written = null;
        var clipboard = new Mock<IClipboard>();
        clipboard.Setup(value => value.SetDataAsync(It.IsAny<IAsyncDataTransfer>()))
            .Callback<IAsyncDataTransfer>(value => written = value)
            .Returns(Task.CompletedTask);
        clipboard.Setup(value => value.TryGetInProcessDataAsync())
            .ReturnsAsync(() => written);
        clipboard.Setup(value => value.ClearAsync()).Returns(Task.CompletedTask);
        var sut = CreateSut(clipboard.Object, supportsOwnership: true);

        var write = await sut.SetTextAsync("123456", TestContext.Current.CancellationToken);
        var clear = await sut.ClearIfUnchangedAsync(
            write.Value,
            TestContext.Current.CancellationToken);

        Assert.True(clear.Value);
        clipboard.Verify(value => value.ClearAsync(), Times.Once);
    }

    [Fact]
    public async Task ClearIfUnchangedAsync_WhenAnotherTransferIsCurrent_DoesNotClear()
    {
        var clipboard = new Mock<IClipboard>();
        clipboard.Setup(value => value.SetDataAsync(It.IsAny<IAsyncDataTransfer>()))
            .Returns(Task.CompletedTask);
        clipboard.Setup(value => value.TryGetInProcessDataAsync())
            .ReturnsAsync(new DataTransfer());
        var sut = CreateSut(clipboard.Object, supportsOwnership: true);

        var write = await sut.SetTextAsync("123456", TestContext.Current.CancellationToken);
        var clear = await sut.ClearIfUnchangedAsync(
            write.Value,
            TestContext.Current.CancellationToken);

        Assert.False(clear.Value);
        clipboard.Verify(value => value.ClearAsync(), Times.Never);
    }

    [Fact]
    public async Task SetTextAsync_WhenOwnershipUnsupported_DoesNotClaimConditionalClear()
    {
        var clipboard = new Mock<IClipboard>();
        var sut = CreateSut(clipboard.Object, supportsOwnership: false);

        Assert.Equal(ClipboardCapabilities.WriteText, sut.Capabilities);

        var clear = await sut.ClearIfUnchangedAsync(
            new TOTP.Core.Services.Models.ClipboardWriteReceipt(1),
            TestContext.Current.CancellationToken);
        Assert.True(clear.IsFailed);
    }

    private static AvaloniaPlatformClipboard CreateSut(
        IClipboard clipboard,
        bool supportsOwnership)
    {
        var accessor = new AvaloniaClipboardAccessor();
        accessor.Set(clipboard);
        return new AvaloniaPlatformClipboard(
            accessor,
            supportsOwnership,
            Mock.Of<ILogger<AvaloniaPlatformClipboard>>());
    }
}
