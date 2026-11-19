using Moq;
using TOTP.Core.Security.Models;

namespace TOTP.Tests.Avalonia.Mobile;

public sealed partial class MobileShellViewModelTests
{
    [Fact]
    public async Task PasswordChangeEditor_InitiallyCollapsedOpensWithoutChangingAndCancelRestoresInitialState()
    {
        var context = await CreatePasswordChangeEditorContextAsync();
        using var sut = context.Sut;
        Assert.False(sut.IsMasterPasswordChangeExpanded);
        Assert.True(sut.BeginMasterPasswordChangeCommand.CanExecute(null));
        Assert.False(sut.ChangeMasterPasswordCommand.CanExecute(null));
        Assert.False(sut.CancelMasterPasswordChangeCommand.CanExecute(null));

        await sut.BeginMasterPasswordChangeAsync();
        Assert.True(sut.IsMasterPasswordChangeExpanded);
        Assert.False(sut.BeginMasterPasswordChangeCommand.CanExecute(null));
        Assert.True(sut.ChangeMasterPasswordCommand.CanExecute(null));
        FillPasswordChangeEditor(context);
        await sut.CancelMasterPasswordChangeAsync();

        Assert.False(sut.IsMasterPasswordChangeExpanded);
        Assert.True(sut.BeginMasterPasswordChangeCommand.CanExecute(null));
        Assert.Empty(sut.CurrentMasterPassword);
        Assert.Empty(sut.NewMasterPassword);
        Assert.Empty(sut.NewMasterPasswordConfirmation);
        await sut.BeginMasterPasswordChangeAsync();
        Assert.True(sut.IsMasterPasswordChangeExpanded);
        Assert.Empty(sut.CurrentMasterPassword);
        context.Authorization.Verify(value => value.ChangePasswordAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task PasswordChangeEditor_CollapsedFormCannotInvokePasswordChange()
    {
        var context = await CreatePasswordChangeEditorContextAsync();
        using var sut = context.Sut;
        FillPasswordChangeEditor(context);

        await sut.ChangeMasterPasswordAsync();

        context.Authorization.Verify(value => value.ChangePasswordAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        Assert.False(sut.IsMasterPasswordChangeExpanded);
    }

    [Theory]
    [InlineData(AuthorizationResult.InvalidCredentials)]
    [InlineData(AuthorizationResult.Failed)]
    public async Task PasswordChangeEditor_BackendFailureClearsInputsAndKeepsFormOpenForRetry(AuthorizationResult result)
    {
        var context = await CreatePasswordChangeEditorContextAsync();
        using var sut = context.Sut;
        context.Authorization.Setup(value => value.ChangePasswordAsync("synthetic current", "synthetic replacement"))
            .ReturnsAsync(result);
        await sut.BeginMasterPasswordChangeAsync();
        FillPasswordChangeEditor(context);

        await sut.ChangeMasterPasswordAsync();

        Assert.True(sut.IsMasterPasswordChangeExpanded);
        Assert.True(sut.CancelMasterPasswordChangeCommand.CanExecute(null));
        Assert.Empty(sut.CurrentMasterPassword);
        Assert.Empty(sut.NewMasterPassword);
        Assert.Empty(sut.NewMasterPasswordConfirmation);
        Assert.NotEmpty(sut.NotificationText);
    }

    [Fact]
    public async Task PasswordChangeEditor_ValidationErrorKeepsFormOpenWithoutCallingService()
    {
        var context = await CreatePasswordChangeEditorContextAsync();
        using var sut = context.Sut;
        await sut.BeginMasterPasswordChangeAsync();
        FillPasswordChangeEditor(context);
        sut.NewMasterPasswordConfirmation = "synthetic mismatch";

        await sut.ChangeMasterPasswordAsync();

        Assert.True(sut.IsMasterPasswordChangeExpanded);
        Assert.NotEmpty(sut.NotificationText);
        context.Authorization.Verify(value => value.ChangePasswordAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Theory]
    [InlineData("categories")]
    [InlineData("accounts")]
    [InlineData("lock")]
    [InlineData("dispose")]
    public async Task PasswordChangeEditor_LeavingSecurityClearsAndCollapses(string exit)
    {
        var context = await CreatePasswordChangeEditorContextAsync();
        using var sut = context.Sut;
        await sut.BeginMasterPasswordChangeAsync();
        FillPasswordChangeEditor(context);

        if (exit == "categories") await sut.ShowSettingsCategoriesAsync();
        else if (exit == "accounts") await sut.ShowAccountsAsync();
        else if (exit == "lock") await sut.LockAsync();
        else sut.Dispose();

        Assert.False(sut.IsMasterPasswordChangeExpanded);
        Assert.Empty(sut.CurrentMasterPassword);
        Assert.Empty(sut.NewMasterPassword);
        Assert.Empty(sut.NewMasterPasswordConfirmation);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PasswordChangeEditor_PendingChangeDisablesCancelAndOnlyActiveSuccessCollapses(bool lockWhilePending)
    {
        var context = await CreatePasswordChangeEditorContextAsync();
        using var sut = context.Sut;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var complete = new TaskCompletionSource<AuthorizationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        context.Authorization.Setup(value => value.ChangePasswordAsync("synthetic current", "synthetic replacement"))
            .Returns(() => { entered.SetResult(); return complete.Task; });
        await sut.BeginMasterPasswordChangeAsync();
        FillPasswordChangeEditor(context);
        var changing = sut.ChangeMasterPasswordAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3), global::Xunit.TestContext.Current.CancellationToken);
        Assert.False(sut.CancelMasterPasswordChangeCommand.CanExecute(null));
        await sut.CancelMasterPasswordChangeAsync();
        Assert.True(sut.IsMasterPasswordChangeExpanded);
        if (lockWhilePending) await sut.LockAsync();

        complete.SetResult(AuthorizationResult.Success);
        await changing;

        Assert.False(sut.IsMasterPasswordChangeExpanded);
        Assert.Empty(sut.CurrentMasterPassword);
        Assert.Empty(sut.NewMasterPassword);
        if (lockWhilePending) Assert.True(sut.IsUnlockVisible);
        else Assert.True(sut.BeginMasterPasswordChangeCommand.CanExecute(null));
    }

    [Theory]
    [InlineData("en", "Change")]
    [InlineData("de", "Ändern")]
    [InlineData("fr", "Modifier")]
    [InlineData("es", "Cambiar")]
    public async Task PasswordChangeEditor_ChangeActionUsesActiveLocale(string locale, string expected)
    {
        var context = await CreatePasswordChangeEditorContextAsync(locale);
        using var sut = context.Sut;
        await sut.BeginMasterPasswordChangeAsync();
        Assert.Equal(expected, sut.ChangeText);
    }

    private static async Task<TestContext> CreatePasswordChangeEditorContextAsync(string locale = "en")
    {
        var context = CreateContext(isConfigured: true, cultureName: locale);
        context.Authorization.Setup(value => value.TryUnlockWithPasswordAsync("synthetic session"))
            .Callback(context.State.Unlock).ReturnsAsync(AuthorizationResult.Success);
        context.Authorization.Setup(value => value.Lock()).Callback(context.State.Lock);
        await context.Sut.InitializeAsync();
        context.Sut.UnlockPassword = "synthetic session";
        await context.Sut.UnlockAsync();
        await context.Sut.ShowSettingsAsync();
        context.Sut.ShowSecuritySettingsCommand.Execute(null);
        await WaitUntilAsync(() => context.Sut.IsSecuritySettingsVisible);
        return context;
    }

    private static void FillPasswordChangeEditor(TestContext context)
    {
        context.Sut.CurrentMasterPassword = "synthetic current";
        context.Sut.NewMasterPassword = "synthetic replacement";
        context.Sut.NewMasterPasswordConfirmation = "synthetic replacement";
    }
}
