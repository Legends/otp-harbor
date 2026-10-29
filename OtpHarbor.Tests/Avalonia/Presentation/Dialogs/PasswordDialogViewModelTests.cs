using TOTP.Avalonia.Desktop.Presentation.Dialogs;

namespace TOTP.Tests.Avalonia.Presentation.Dialogs;

public sealed class PasswordDialogViewModelTests
{
    [Fact]
    public async Task ConfirmAsync_ClearsBoundValueBeforeValidationAndReturnsCandidate()
    {
        var passwordDuringValidation = "not-called";
        PasswordDialogViewModel? sut = null;
        sut = new PasswordDialogViewModel(CreateRequest((_, _) =>
        {
            passwordDuringValidation = sut!.Password;
            return Task.FromResult<string?>(null);
        }));
        string? result = null;
        sut.CloseRequested += value => result = value;
        sut.Password = "synthetic password";

        await sut.ConfirmAsync();

        Assert.Empty(passwordDuringValidation);
        Assert.Empty(sut.Password);
        Assert.Equal("synthetic password", result);
    }

    [Fact]
    public async Task ConfirmAsync_WhenRequired_FailsWithoutCallingValidator()
    {
        var called = false;
        var sut = new PasswordDialogViewModel(CreateRequest((_, _) =>
        {
            called = true;
            return Task.FromResult<string?>(null);
        }));

        await sut.ConfirmAsync();

        Assert.False(called);
        Assert.Equal("Password is required.", sut.ErrorMessage);
    }

    [Fact]
    public async Task ConfirmAsync_WhenValidatorFails_DoesNotReturnPassword()
    {
        var sut = new PasswordDialogViewModel(CreateRequest(
            (_, _) => Task.FromResult<string?>("Password was rejected.")))
        {
            Password = "synthetic password"
        };
        var closed = false;
        sut.CloseRequested += _ => closed = true;

        await sut.ConfirmAsync();

        Assert.False(closed);
        Assert.Empty(sut.Password);
        Assert.Equal("Password was rejected.", sut.ErrorMessage);
    }

    [Fact]
    public async Task ConfirmAsync_WhenValidatorThrows_ShowsOnlySafeFailure()
    {
        var sut = new PasswordDialogViewModel(CreateRequest(
            (_, _) => throw new InvalidOperationException("sensitive synthetic detail")))
        {
            Password = "synthetic password"
        };

        await sut.ConfirmAsync();

        Assert.Equal("Password could not be validated safely.", sut.ErrorMessage);
        Assert.DoesNotContain("sensitive", sut.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(sut.Password);
    }

    [Fact]
    public void Cancel_ClearsPasswordAndReturnsNoValue()
    {
        var sut = new PasswordDialogViewModel(CreateRequest())
        {
            Password = "synthetic password"
        };
        var sentinel = "not-called";
        sut.CloseRequested += value => sentinel = value ?? string.Empty;

        sut.Cancel();

        Assert.Empty(sut.Password);
        Assert.Empty(sentinel);
    }

    [Fact]
    public async Task ConfirmAsync_WhenConfirmationDoesNotMatch_ClearsBothAndDoesNotClose()
    {
        var sut = new PasswordDialogViewModel(CreateConfirmationRequest())
        {
            Password = "synthetic password",
            Confirmation = "different password"
        };
        var closed = false;
        sut.CloseRequested += _ => closed = true;

        await sut.ConfirmAsync();

        Assert.False(closed);
        Assert.Empty(sut.Password);
        Assert.Empty(sut.Confirmation);
        Assert.Equal("Passwords do not match.", sut.ErrorMessage);
    }

    [Fact]
    public async Task ConfirmAsync_WhenConfirmationMatches_ReturnsCandidateAndClearsBoth()
    {
        var sut = new PasswordDialogViewModel(CreateConfirmationRequest())
        {
            Password = "synthetic password",
            Confirmation = "synthetic password"
        };
        string? returned = null;
        sut.CloseRequested += value => returned = value;

        await sut.ConfirmAsync();

        Assert.Equal("synthetic password", returned);
        Assert.Empty(sut.Password);
        Assert.Empty(sut.Confirmation);
    }

    private static PasswordDialogRequest CreateRequest(
        Func<string, CancellationToken, Task<string?>>? validate = null) =>
        new(
            "Authorize action",
            "Enter the password to continue.",
            "Continue",
            "Cancel",
            "Password is required.",
            "Password could not be validated safely.",
            validate);

    private static PasswordDialogRequest CreateConfirmationRequest() => new(
        "Create export password",
        "Create and confirm a password.",
        "Continue",
        "Cancel",
        "Password is required.",
        "Password could not be validated safely.",
        RequireConfirmation: true,
        ConfirmationRequiredMessage: "Confirmation is required.",
        MismatchMessage: "Passwords do not match.");
}
