using TOTP.Avalonia.Desktop.Presentation;
using TOTP.Core.Services.Interfaces;

namespace TOTP.Tests.Avalonia.Presentation;

public sealed class NotificationStateTests
{
    [Fact]
    public void ShowPersistent_ExposesStandardTextSeverityAndVisibility()
    {
        using var sut = new NotificationState();

        sut.ShowPersistent("Synthetic warning", NotificationSeverity.Warning);

        Assert.Equal("Synthetic warning", sut.Text);
        Assert.Equal(NotificationSeverity.Warning, sut.Severity);
        Assert.True(sut.HasMessage);
    }

    [Fact]
    public async Task ShowTransient_ClearsAfterConfiguredDuration()
    {
        using var sut = new NotificationState(TimeSpan.FromMilliseconds(20));
        sut.ShowTransient("Saved", NotificationSeverity.Success);

        await Task.Delay(100, TestContext.Current.CancellationToken);

        Assert.Empty(sut.Text);
        Assert.False(sut.HasMessage);
    }

    [Fact]
    public async Task NewPersistentMessage_IsNotClearedByPreviousTransientLifetime()
    {
        using var sut = new NotificationState(TimeSpan.FromMilliseconds(20));
        sut.ShowTransient("Saved", NotificationSeverity.Success);
        sut.ShowPersistent("Failed", NotificationSeverity.Error);

        await Task.Delay(100, TestContext.Current.CancellationToken);

        Assert.Equal("Failed", sut.Text);
        Assert.Equal(NotificationSeverity.Error, sut.Severity);
    }

    [Theory]
    [InlineData(NotificationSeverity.Information)]
    [InlineData(NotificationSeverity.Success)]
    [InlineData(NotificationSeverity.Warning)]
    public async Task ShowForSeverity_NonErrorNoticeClearsAfterConfiguredDuration(
        NotificationSeverity severity)
    {
        using var sut = new NotificationState(TimeSpan.FromMilliseconds(20));

        sut.ShowForSeverity("Transient notice", severity);
        await Task.Delay(100, TestContext.Current.CancellationToken);

        Assert.Empty(sut.Text);
        Assert.False(sut.HasMessage);
    }

    [Fact]
    public async Task ShowForSeverity_ErrorClearsAfterConfiguredDuration()
    {
        using var sut = new NotificationState(TimeSpan.FromMilliseconds(20));

        sut.ShowForSeverity("Recoverable error", NotificationSeverity.Error);
        await Task.Delay(100, TestContext.Current.CancellationToken);

        Assert.Empty(sut.Text);
        Assert.False(sut.HasMessage);
    }

    [Fact]
    public void Shown_IsRaisedForRepeatedMessagesSoWindowOverlaysCanRestartTheirLifetime()
    {
        using var sut = new NotificationState();
        var shown = new List<NotificationShownEventArgs>();
        sut.Shown += (_, args) => shown.Add(args);

        sut.ShowPersistent("Retry failed", NotificationSeverity.Error);
        sut.ShowPersistent("Retry failed", NotificationSeverity.Error);

        Assert.Collection(
            shown,
            first => Assert.Equal(
                new NotificationShownEventArgs("Retry failed", NotificationSeverity.Error),
                first),
            second => Assert.Equal(
                new NotificationShownEventArgs("Retry failed", NotificationSeverity.Error),
                second));
    }
}
