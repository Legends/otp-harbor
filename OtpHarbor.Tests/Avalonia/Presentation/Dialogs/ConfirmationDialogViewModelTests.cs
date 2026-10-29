using TOTP.Avalonia.Desktop.Presentation.Dialogs;
using TOTP.Core.Services.Interfaces;

namespace TOTP.Tests.Avalonia.Presentation.Dialogs;

public sealed class ConfirmationDialogViewModelTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Command_RequestsExactlyTheSelectedResult(bool confirm)
    {
        var sut = new ConfirmationDialogViewModel(new ConfirmationDialogRequest(
            "Confirm action",
            "This is a synthetic confirmation.",
            NotificationSeverity.Warning,
            "Continue",
            "Cancel"));
        bool? result = null;
        sut.CloseRequested += value => result = value;

        (confirm ? sut.ConfirmCommand : sut.CancelCommand).Execute(null);

        Assert.Equal(confirm, result);
    }

    [Fact]
    public void Constructor_RejectsMissingUserVisibleIntent()
    {
        var action = () => new ConfirmationDialogViewModel(new ConfirmationDialogRequest(
            "",
            "Message",
            NotificationSeverity.Information,
            "OK",
            "Cancel"));

        Assert.Throws<ArgumentException>(action);
    }

    [Fact]
    public void Constructor_WhenUsedAsRecoverableMessage_HidesCancelAction()
    {
        var sut = new ConfirmationDialogViewModel(new ConfirmationDialogRequest(
            "Operation failed safely",
            "Review the settings and try again.",
            NotificationSeverity.Error,
            "Close",
            "Close",
            ShowCancel: false));

        Assert.False(sut.ShowCancel);
    }

    [Fact]
    public void Constructor_WhenActionIsDestructive_ExposesDestructivePresentationState()
    {
        var sut = new ConfirmationDialogViewModel(new ConfirmationDialogRequest(
            "Delete account",
            "Example account will be permanently removed.",
            NotificationSeverity.Warning,
            "Delete",
            "Cancel",
            IsDestructive: true));

        Assert.True(sut.IsDestructive);
    }
}
