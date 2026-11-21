using Moq;
using TOTP.Avalonia.Desktop.Platform;
using TOTP.Avalonia.Desktop.Presentation;
using TOTP.Avalonia.Desktop.Startup;
using TOTP.Core.Enums;
using TOTP.Core.Security;
using TOTP.Core.Security.Interfaces;
using TOTP.Infrastructure.Security;

namespace TOTP.Tests.Avalonia.Presentation;

public sealed partial class MainWindowViewModelTests
{
    [Fact]
    public async Task LeavingSecurityTab_DiscardsPasswordEditorAndAllSensitiveFields()
    {
        var state = new AuthorizationState();
        state.SetConfiguration(true, PreferredUnlockMethod.Password);
        state.Unlock();
        var authorization = new Mock<IAuthorizationService>();
        authorization.SetupGet(value => value.State).Returns(state);
        using var editor = new AuthorizationSettingsViewModel(authorization.Object,
            Mock.Of<IAvaloniaDialogService>(), CreateLocalization(), new PasswordValidationService());
        using var sut = CreateSut(Mock.Of<IAvaloniaStartupCoordinator>(), authorization.Object, authorizationSettings: editor);
        sut.SelectedSettingsTabIndex = 2;
        await editor.BeginPasswordChangeAsync();
        editor.CurrentPassword = "current-password";
        editor.NewPassword = "new-password";
        editor.ConfirmPassword = "new-password";
        sut.SelectedSettingsTabIndex = 0;
        Assert.False(editor.IsPasswordEditorOpen);
        Assert.Empty(editor.CurrentPassword);
        Assert.Empty(editor.NewPassword);
        Assert.Empty(editor.ConfirmPassword);
        sut.SelectedSettingsTabIndex = 2;
        Assert.False(editor.IsPasswordEditorOpen);
        authorization.Verify(value => value.ChangePasswordAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }
}
