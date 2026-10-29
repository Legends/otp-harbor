using TOTP.Avalonia.Shared.Controls;
using Avalonia.Media;

namespace TOTP.Tests.Avalonia.Shared;

public sealed class ValidationMessageTests
{
    [Fact]
    public void Text_ControlsVisibilityWithoutASeparateBinding()
    {
        var sut = new ValidationMessage();

        Assert.False(sut.IsVisible);

        sut.Text = "A safe validation message.";
        Assert.True(sut.IsVisible);

        sut.Text = "  ";
        Assert.False(sut.IsVisible);
    }

    [Theory]
    [InlineData(ValidationSeverity.Information)]
    [InlineData(ValidationSeverity.Warning)]
    [InlineData(ValidationSeverity.Error)]
    public void Severity_RoundTripsSemanticState(ValidationSeverity severity)
    {
        var sut = new ValidationMessage { Severity = severity };

        Assert.Equal(severity, sut.Severity);
    }

    [Fact]
    public void TextAlignment_CanBeSetByReusableConsumers()
    {
        var sut = new ValidationMessage { TextAlignment = TextAlignment.Center };

        Assert.Equal(TextAlignment.Center, sut.TextAlignment);
    }
}
