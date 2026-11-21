using Moq;
using TOTP.Avalonia.Desktop.Localization;
using TOTP.Avalonia.Desktop.Platform;
using TOTP.Avalonia.Desktop.Presentation;
using TOTP.Avalonia.Desktop.Presentation.Dialogs;
using TOTP.Core.Enums;
using TOTP.Core.Security.Interfaces;
using TOTP.Core.Security.Models;

namespace TOTP.Tests.Avalonia.Presentation;

public sealed partial class AuthorizationSettingsViewModelTests
{
    [Fact]
    public async Task PasswordEditor_OpenCancelAndLockClearAllInputsAndCommandState()
    {
        var state = ConfiguredState(PreferredUnlockMethod.Password);
        using var sut = CreateSut(Authorization(state).Object, Mock.Of<IAvaloniaDialogService>());
        Assert.False(sut.IsPasswordEditorOpen);
        Assert.False(sut.ChangePasswordCommand.CanExecute(null));
        await sut.BeginPasswordChangeAsync();
        Assert.True(sut.IsPasswordEditorOpen);
        Assert.True(sut.ChangePasswordCommand.CanExecute(null));
        Assert.False(sut.BeginPasswordChangeCommand.CanExecute(null));
        FillPasswordEditor(sut);
        await sut.CancelPasswordChangeAsync();
        AssertPasswordEditorCleared(sut);
        Assert.False(sut.IsPasswordEditorOpen);
        await sut.BeginPasswordChangeAsync();
        FillPasswordEditor(sut);
        state.Lock();
        AssertPasswordEditorCleared(sut);
        Assert.False(sut.IsPasswordEditorOpen);
        Assert.False(sut.BeginPasswordChangeCommand.CanExecute(null));
        await sut.BeginPasswordChangeAsync();
        Assert.False(sut.IsPasswordEditorOpen);
    }

    [Theory]
    [InlineData("", "new-password", "new-password", AvaloniaStringKeys.PasswordRequired)]
    [InlineData("current-password", "", "new-password", AvaloniaStringKeys.PasswordRequired)]
    [InlineData("current-password", "short", "short", AvaloniaStringKeys.PasswordMinimumLength)]
    [InlineData("current-password", "new-password", "different", AvaloniaStringKeys.PasswordMismatch)]
    public async Task PasswordEditor_ValidationRejectsBeforeServiceAndKeepsClearEditor(
        string current, string next, string confirm, string message)
    {
        var auth = Authorization(ConfiguredState(PreferredUnlockMethod.Password));
        using var sut = CreateSut(auth.Object, Mock.Of<IAvaloniaDialogService>());
        await sut.BeginPasswordChangeAsync();
        sut.CurrentPassword = current;
        sut.NewPassword = next;
        sut.ConfirmPassword = confirm;
        await sut.ChangePasswordAsync();
        auth.Verify(value => value.ChangePasswordAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        AssertPasswordEditorCleared(sut);
        Assert.True(sut.IsPasswordEditorOpen);
        Assert.Equal(message, sut.Message);
    }

    [Theory]
    [InlineData(AuthorizationResult.Success, false, AvaloniaStringKeys.PasswordChanged)]
    [InlineData(AuthorizationResult.InvalidCredentials, true, AvaloniaStringKeys.PasswordVerificationFailed)]
    [InlineData(AuthorizationResult.Failed, true, AvaloniaStringKeys.PasswordChangeFailed)]
    public async Task PasswordEditor_SaveUsesInlineCredentialsAndReportsResult(
        AuthorizationResult result, bool remainsOpen, string message)
    {
        var auth = Authorization(ConfiguredState(PreferredUnlockMethod.PlatformQuickUnlock));
        var dialogs = new Mock<IAvaloniaDialogService>();
        using var sut = CreateSut(auth.Object, dialogs.Object);
        await sut.BeginPasswordChangeAsync();
        var completion = new TaskCompletionSource<AuthorizationResult>();
        auth.Setup(value => value.ChangePasswordAsync("current-password", "new-password")).Returns(completion.Task);
        FillPasswordEditor(sut);
        var saving = sut.ChangePasswordAsync();
        AssertPasswordEditorCleared(sut);
        Assert.True(sut.IsBusy);
        Assert.False(sut.ChangePasswordCommand.CanExecute(null));
        Assert.False(sut.CancelPasswordChangeCommand.CanExecute(null));
        await sut.ChangePasswordAsync();
        completion.SetResult(result);
        await saving;
        auth.Verify(value => value.ChangePasswordAsync("current-password", "new-password"), Times.Once);
        dialogs.Verify(value => value.PromptForPasswordAsync(It.IsAny<PasswordDialogRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal(remainsOpen, sut.IsPasswordEditorOpen);
        Assert.Equal(message, sut.Message);
        Assert.False(sut.IsBusy);
        AssertPasswordEditorCleared(sut);
    }

    [Fact]
    public async Task PasswordEditor_ExceptionKeepsClearEditorAndCanRetry()
    {
        var auth = Authorization(ConfiguredState(PreferredUnlockMethod.Password));
        auth.Setup(value => value.ChangePasswordAsync(It.IsAny<string>(), It.IsAny<string>())).ThrowsAsync(new IOException("synthetic failure"));
        using var sut = CreateSut(auth.Object, Mock.Of<IAvaloniaDialogService>());
        await sut.BeginPasswordChangeAsync();
        FillPasswordEditor(sut);
        await sut.ChangePasswordAsync();
        AssertPasswordEditorCleared(sut);
        Assert.True(sut.IsPasswordEditorOpen);
        Assert.True(sut.ChangePasswordCommand.CanExecute(null));
        Assert.Equal(AvaloniaStringKeys.PasswordChangeFailed, sut.Message);
    }

    [Fact]
    public async Task PasswordEditor_LockBeforeRotationStartsDoesNotCallService()
    {
        var state = ConfiguredState(PreferredUnlockMethod.Password);
        var auth = Authorization(state);
        using var sut = CreateSut(auth.Object, Mock.Of<IAvaloniaDialogService>());
        await sut.BeginPasswordChangeAsync();
        FillPasswordEditor(sut);
        sut.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(sut.IsBusy) && sut.IsBusy) state.Lock();
        };
        await sut.ChangePasswordAsync();
        auth.Verify(value => value.ChangePasswordAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        AssertPasswordEditorCleared(sut);
        Assert.False(sut.IsPasswordEditorOpen);
        Assert.False(state.IsUnlocked);
        Assert.False(sut.Notification.HasMessage);
    }

    [Fact]
    public async Task PasswordEditor_LockDuringRotationKeepsSessionLockedAndSuppressesSuccess()
    {
        var state = ConfiguredState(PreferredUnlockMethod.Password);
        var auth = Authorization(state);
        var completion = new TaskCompletionSource<AuthorizationResult>();
        auth.Setup(value => value.ChangePasswordAsync(It.IsAny<string>(), It.IsAny<string>())).Returns(completion.Task);
        auth.Setup(value => value.Lock()).Callback(state.Lock);
        using var sut = CreateSut(auth.Object, Mock.Of<IAvaloniaDialogService>());
        await sut.BeginPasswordChangeAsync();
        FillPasswordEditor(sut);
        var saving = sut.ChangePasswordAsync();
        state.Lock();
        state.Unlock(); // The existing rotation service reactivates its refreshed context on completion.
        completion.SetResult(AuthorizationResult.Success);
        await saving;
        auth.Verify(value => value.Lock(), Times.Once);
        Assert.False(state.IsUnlocked);
        Assert.False(sut.IsPasswordEditorOpen);
        AssertPasswordEditorCleared(sut);
        Assert.False(sut.Notification.HasMessage);
    }

    [Fact]
    public async Task PasswordEditor_DisposeClearsSecretsAndReleasesStateSubscription()
    {
        var state = ConfiguredState(PreferredUnlockMethod.Password);
        var sut = CreateSut(Authorization(state).Object, Mock.Of<IAvaloniaDialogService>());
        await sut.BeginPasswordChangeAsync();
        FillPasswordEditor(sut);
        sut.Dispose();
        AssertPasswordEditorCleared(sut);
        Assert.False(sut.IsPasswordEditorOpen);
        var notifications = 0;
        sut.PropertyChanged += (_, _) => notifications++;
        state.Lock();
        Assert.Equal(0, notifications);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("de")]
    [InlineData("fr")]
    [InlineData("es")]
    public async Task PasswordEditor_LengthGuidanceUsesActiveLocaleAndActualPolicy(string language)
    {
        var localization = new AvaloniaLocalizationService(new global::Avalonia.Controls.ResourceDictionary(), new AvaloniaStringCatalog());
        var validation = new Mock<IPasswordValidationService>();
        validation.SetupGet(value => value.MinimumLength).Returns(8);
        using var sut = new AuthorizationSettingsViewModel(Authorization(ConfiguredState(PreferredUnlockMethod.Password)).Object,
            Mock.Of<IAvaloniaDialogService>(), localization, validation.Object);
        await sut.BeginPasswordChangeAsync();
        sut.NewPassword = "sample";
        Assert.Equal(6, sut.PasswordLengthProgress);
        localization.ApplyCulture(language);
        Assert.Equal(string.Format(localization.GetString(AvaloniaStringKeys.PasswordMinimumLength), 8), sut.PasswordMinimumLengthHelp);
        sut.NewPassword = "longer-than-the-minimum";
        Assert.Equal(8, sut.PasswordLengthProgress);
        sut.ClearSensitiveInputs();
        Assert.Equal(0, sut.PasswordLengthProgress);
    }

    private static void FillPasswordEditor(AuthorizationSettingsViewModel sut)
    {
        sut.CurrentPassword = "current-password";
        sut.NewPassword = "new-password";
        sut.ConfirmPassword = "new-password";
    }

    private static void AssertPasswordEditorCleared(AuthorizationSettingsViewModel sut)
    {
        Assert.Empty(sut.CurrentPassword);
        Assert.Empty(sut.NewPassword);
        Assert.Empty(sut.ConfirmPassword);
    }
}
