using Avalonia.Controls;
using Moq;
using TOTP.Avalonia.Desktop.Localization;
using TOTP.Avalonia.Desktop.Presentation;
using TOTP.Core.Security.Interfaces;
using TOTP.Core.Security.Models;

namespace TOTP.Tests.Avalonia.Presentation;

public sealed class PasswordSetupViewModelTests
{
    [Fact]
    public async Task ConfigureAsync_WhenSuccessful_ClearsInputsAndSignalsConfigured()
    {
        var authorization = new Mock<IAuthorizationService>();
        authorization.Setup(value => value.ConfigurePasswordAsync("synthetic password", "synthetic password"))
            .ReturnsAsync(AuthorizationResult.Success);
        var sut = CreateSut(authorization.Object);
        var configured = false;
        sut.Configured += (_, _) => configured = true;
        sut.Password = "synthetic password";
        sut.ConfirmPassword = "synthetic password";

        await sut.ConfigureAsync();

        Assert.True(configured);
        Assert.Empty(sut.Password);
        Assert.Empty(sut.ConfirmPassword);
        Assert.Empty(sut.Message);
    }

    [Fact]
    public async Task ConfigureAsync_WhenTooShort_StopsBeforeAuthorization()
    {
        var authorization = new Mock<IAuthorizationService>();
        var sut = CreateSut(authorization.Object);
        sut.Password = "short";
        sut.ConfirmPassword = "short";

        await sut.ConfigureAsync();

        authorization.VerifyNoOtherCalls();
        Assert.Contains("8", sut.Message, StringComparison.Ordinal);
        Assert.Empty(sut.Password);
        Assert.Empty(sut.ConfirmPassword);
    }

    [Fact]
    public async Task ConfigureAsync_WhenPasswordsDoNotMatch_PreservesInputsForReview()
    {
        var authorization = new Mock<IAuthorizationService>();
        var sut = CreateSut(authorization.Object);
        sut.Password = "first synthetic password";
        sut.ConfirmPassword = "second synthetic password";

        await sut.ConfigureAsync();

        authorization.VerifyNoOtherCalls();
        Assert.Equal("first synthetic password", sut.Password);
        Assert.Equal("second synthetic password", sut.ConfirmPassword);
        Assert.Contains("match", sut.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ConfigureAsync_WhenExistingVaultConflicts_ExplainsNonDestructiveRecoveryPath()
    {
        var authorization = new Mock<IAuthorizationService>();
        authorization.Setup(value => value.ConfigurePasswordAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(AuthorizationResult.ExistingVaultConflict);
        var sut = CreateSut(authorization.Object);
        sut.Password = "synthetic password";
        sut.ConfirmPassword = "synthetic password";

        await sut.ConfigureAsync();

        Assert.Contains("not replaced", sut.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("recovery", sut.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ConfigureAsync_WhenBoundaryThrows_ShowsOnlySafeFailure()
    {
        var authorization = new Mock<IAuthorizationService>();
        authorization.Setup(value => value.ConfigurePasswordAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new InvalidOperationException("sensitive synthetic detail"));
        var sut = CreateSut(authorization.Object);
        sut.Password = "synthetic password";
        sut.ConfirmPassword = "synthetic password";

        await sut.ConfigureAsync();

        Assert.DoesNotContain("sensitive", sut.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("No existing data", sut.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static PasswordSetupViewModel CreateSut(IAuthorizationService authorization)
    {
        var validation = new Mock<IPasswordValidationService>();
        validation.SetupGet(value => value.MinimumLength).Returns(8);
        var resources = new ResourceDictionary();
        var localization = new AvaloniaLocalizationService(resources, new AvaloniaStringCatalog());
        localization.ApplyCulture("en");
        return new PasswordSetupViewModel(authorization, validation.Object, localization);
    }
}
