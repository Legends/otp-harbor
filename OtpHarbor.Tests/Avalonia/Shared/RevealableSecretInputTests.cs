using TOTP.Avalonia.Shared.Controls;

namespace TOTP.Tests.Avalonia.Shared;

public sealed class RevealableSecretInputTests
{
    [Fact]
    public void Defaults_AreFailClosedAndAccessible()
    {
        var sut = new RevealableSecretInput();

        Assert.False(sut.IsRevealed);
        Assert.Equal("Secret", sut.AccessibleName);
        Assert.Equal("Secret", sut.PlaceholderText);
    }

    [Fact]
    public void Conceal_EndsAnActiveDisclosure()
    {
        var sut = new RevealableSecretInput
        {
            Text = "synthetic secret"
        };
        sut.SetValue(RevealableSecretInput.IsRevealedProperty, true);

        sut.Conceal();

        Assert.False(sut.IsRevealed);
    }

    [Fact]
    public void ClearingText_AlsoEndsDisclosure()
    {
        var sut = new RevealableSecretInput
        {
            Text = "synthetic secret"
        };
        sut.SetValue(RevealableSecretInput.IsRevealedProperty, true);

        sut.Text = string.Empty;

        Assert.False(sut.IsRevealed);
    }

    [Fact]
    public void UnauthorizedInput_FailsClosedWhenDisclosureIsSetExternally()
    {
        var sut = new RevealableSecretInput
        {
            IsRevealAuthorized = false,
            Text = "synthetic secret"
        };

        sut.IsRevealed = true;

        Assert.False(sut.IsRevealed);
    }
}
