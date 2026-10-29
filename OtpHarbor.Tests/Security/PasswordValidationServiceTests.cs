using TOTP.Core.Security.Models;
using TOTP.Infrastructure.Security;

namespace TOTP.Tests.Security;

public sealed class PasswordValidationServiceTests
{
    private readonly PasswordValidationService _sut = new();

    [Fact]
    public void MinimumLength_IsEight()
    {
        Assert.Equal(8, _sut.MinimumLength);
    }

    [Fact]
    public void IsValidRequired_HandlesNullWhitespaceAndValue()
    {
        Assert.False(_sut.IsValidRequired(null));
        Assert.False(_sut.IsValidRequired("  "));
        Assert.True(_sut.IsValidRequired("abc"));
    }

    [Fact]
    public void IsValidNew_RequiresMinimumLength()
    {
        Assert.False(_sut.IsValidNew("1234567"));
        Assert.True(_sut.IsValidNew("12345678"));
    }

    [Fact]
    public void IsValidNewWithConfirmation_RequiresMatchAndConfirmation()
    {
        Assert.False(_sut.IsValidNewWithConfirmation("12345678", null));
        Assert.False(_sut.IsValidNewWithConfirmation("12345678", "different"));
        Assert.True(_sut.IsValidNewWithConfirmation("12345678", "12345678"));
    }

    [Fact]
    public void ValidateRequired_ReturnsErrorWhenMissing()
    {
        var result = _sut.ValidateRequired("", "required");

        Assert.False(result.IsValid);
        Assert.Equal("required", result.PasswordError);
    }

    [Fact]
    public void ValidateNew_ReturnsRequiredThenLengthThenSuccess()
    {
        var required = _sut.ValidateNew(null, "required", "min {0}");
        Assert.False(required.IsValid);
        Assert.Equal("required", required.PasswordError);

        var min = _sut.ValidateNew("1234567", "required", "min {0}");
        Assert.False(min.IsValid);
        Assert.Equal("min 8", min.PasswordError);

        var ok = _sut.ValidateNew("12345678", "required", "min {0}");
        Assert.True(ok.IsValid);
        Assert.Null(ok.PasswordError);
    }

    [Fact]
    public void ValidateNewWithConfirmation_CoversAllErrorBranches()
    {
        var required = _sut.ValidateNewWithConfirmation(
            null,
            "",
            "required",
            "min {0}",
            "confirm required",
            "mismatch");

        Assert.False(required.IsValid);
        Assert.Equal("required", required.PasswordError);
        Assert.Equal("confirm required", required.ConfirmPasswordError);

        var mismatch = _sut.ValidateNewWithConfirmation(
            "12345678",
            "87654321",
            "required",
            "min {0}",
            "confirm required",
            "mismatch");

        Assert.False(mismatch.IsValid);
        Assert.Null(mismatch.PasswordError);
        Assert.Equal("mismatch", mismatch.ConfirmPasswordError);

        var ok = _sut.ValidateNewWithConfirmation(
            "12345678",
            "12345678",
            "required",
            "min {0}",
            "confirm required",
            "mismatch");

        Assert.True(ok.IsValid);
        Assert.Null(ok.PasswordError);
        Assert.Null(ok.ConfirmPasswordError);
    }
}
