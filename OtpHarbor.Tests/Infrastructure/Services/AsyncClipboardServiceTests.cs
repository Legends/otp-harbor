using FluentResults;
using Microsoft.Extensions.Logging;
using Moq;
using TOTP.Core.Services.Interfaces;
using TOTP.Core.Services.Models;
using TOTP.Infrastructure.Services;

namespace TOTP.Tests.Infrastructure.Services;

public sealed class AsyncClipboardServiceTests
{
    [Fact]
    public async Task CopyAsync_WithoutConditionalClear_WritesWithoutSchedulingClear()
    {
        var platform = new Mock<IAsyncPlatformClipboard>();
        platform.SetupGet(value => value.Capabilities).Returns(ClipboardCapabilities.WriteText);
        platform.Setup(value => value.SetTextAsync("123456", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok(new ClipboardWriteReceipt(42)));
        using var sut = new AsyncClipboardService(
            platform.Object,
            Mock.Of<ILogger<AsyncClipboardService>>());

        var result = await sut.CopyAsync(
            "123456",
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        platform.Verify(value => value.SetTextAsync(
            "123456",
            It.IsAny<CancellationToken>()), Times.Once);
        platform.Verify(value => value.ClearIfUnchangedAsync(
            It.IsAny<ClipboardWriteReceipt>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CopyAndScheduleClearAsync_ClearsOnlyWithReturnedReceipt()
    {
        var receipt = new ClipboardWriteReceipt(42);
        var cleared = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var platform = new Mock<IAsyncPlatformClipboard>();
        platform.SetupGet(value => value.Capabilities).Returns(
            ClipboardCapabilities.WriteText | ClipboardCapabilities.ConditionalClear);
        platform.Setup(value => value.SetTextAsync("123456", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok(receipt));
        platform.Setup(value => value.ClearIfUnchangedAsync(receipt, It.IsAny<CancellationToken>()))
            .Callback(() => cleared.SetResult())
            .ReturnsAsync(Result.Ok(true));
        using var sut = new AsyncClipboardService(
            platform.Object,
            Mock.Of<ILogger<AsyncClipboardService>>());

        var result = await sut.CopyAndScheduleClearAsync(
            "123456",
            TimeSpan.FromMilliseconds(10),
            TestContext.Current.CancellationToken);
        await cleared.Task.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        platform.Verify(value => value.ClearIfUnchangedAsync(
            receipt,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CopyAndScheduleClearAsync_WithoutConditionalClear_FailsBeforeWrite()
    {
        var platform = new Mock<IAsyncPlatformClipboard>();
        platform.SetupGet(value => value.Capabilities).Returns(ClipboardCapabilities.WriteText);
        using var sut = new AsyncClipboardService(
            platform.Object,
            Mock.Of<ILogger<AsyncClipboardService>>());

        var result = await sut.CopyAndScheduleClearAsync(
            "123456",
            TimeSpan.FromSeconds(30),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsFailed);
        platform.Verify(value => value.SetTextAsync(
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }
}
