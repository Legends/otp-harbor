using System.Globalization;
using Moq;
using TOTP.Avalonia.Mobile.Localization;
using TOTP.Avalonia.Mobile.Platform;
using TOTP.Avalonia.Mobile.Presentation;
using TOTP.Core.Common;
using TOTP.Core.Enums;
using TOTP.Core.Security.Interfaces;
using TOTP.Core.Security.Models;
using TOTP.Core.Services.Interfaces;
using TOTP.Infrastructure.Security;
using TOTP.Infrastructure.Services;

namespace TOTP.Tests.Security;

public sealed partial class AuthenticationWorkflowTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MobileFirstInstall_PasswordThenBiometrics_DeactivationUsesFreshBiometricsWithRealServices(bool availableProbe)
    {
        using var harness = new AuthenticationWorkflowHarness();
        using var process = await harness.OpenAsync();
        using var model = new MobileShellViewModel(process.Authorization,
            new PasswordValidationService(), process.Accounts, Mock.Of<IAccountTotpService>(),
            Mock.Of<IAsyncClipboardService>(), Mock.Of<IMobileQrScanner>(), Mock.Of<IQrPayloadValidator>(),
            Mock.Of<IQrAccountImportService>(), Mock.Of<IAccountQrCodeService>(), Mock.Of<IMobileQrImageFactory>(),
            Mock.Of<IMobileDocumentService>(), new ExportService(Microsoft.Extensions.Logging.Abstractions.NullLogger<ExportService>.Instance),
            new AccountImportService(process.Accounts), process.Settings, Mock.Of<IPlatformApplicationPaths>(),
            new MobileStringCatalog(CultureInfo.GetCultureInfo("en")), TimeProvider.System);
        await model.InitializeAsync();
        await model.FinishOnboardingAsync();
        model.SetupPassword = AuthenticationWorkflowHarness.Password;
        model.SetupConfirmation = AuthenticationWorkflowHarness.Password;
        await model.ConfigureAsync();
        Assert.True(model.IsInitialQuickUnlockOfferVisible);
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        model.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(model.IsBusy) && !model.IsBusy) completed.TrySetResult();
        };
        model.ChooseInitialMasterPasswordCommand.Execute(null);
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await model.ShowSettingsAsync();
        await model.BeginBiometricEnrollmentAsync();
        Assert.True(model.IsBiometricEnrollmentVisible);
        model.BiometricRecoveryPassword = AuthenticationWorkflowHarness.Password;
        await model.EnableBiometricAsync();
        Assert.True(model.IsBiometricUnlockSelected);
        Assert.Equal(PreferredUnlockMethod.PlatformQuickUnlock, process.Authorization.State.PreferredUnlockMethod);
        Assert.False(model.IsBiometricEnrollmentVisible);

        harness.Biometrics.Availability = availableProbe
            ? PlatformQuickUnlockAvailability.Available : PlatformQuickUnlockAvailability.TemporarilyUnavailable;
        var previousPrompts = harness.Biometrics.AuthenticationCalls;
        await model.BeginDisableAppLockAsync();

        Assert.Equal(previousPrompts + 1, harness.Biometrics.AuthenticationCalls);
        Assert.Equal(PreferredUnlockMethod.PlatformQuickUnlock, model.SelectedAuthenticationMethod?.Method);
        Assert.False(model.IsAppLockEnabled);
        Assert.False(model.IsDisableAppLockConfirmationVisible);
        Assert.Empty(model.AppLockRecoveryPassword);
        Assert.True(process.Authorization.State.IsUnlocked);
    }
}
