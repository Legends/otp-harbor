using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using TOTP.Avalonia.Shared.Controls;

namespace TOTP.Tests.Avalonia.Headless;

public sealed class ProtectedTextBoxTests
{
    [AvaloniaTheory]
    [InlineData(360)]
    [InlineData(720)]
    public void MaskedInput_AcceptsPointerFocusAndKeyboardTextThroughTheSharedTemplate(int width)
    {
        var secret = new RevealableSecretInput();
        var window = new Window { Width = width, Height = 200, Content = secret };
        try
        {
            window.Show();
            window.UpdateLayout();
            var input = Assert.Single(secret.GetVisualDescendants().OfType<ProtectedTextBox>());
            var point = input.TranslatePoint(new Point(20, input.Bounds.Height / 2), window)!.Value;
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            Assert.True(input.IsFocused);

            window.KeyTextInput("Synthetic typed password");

            Assert.Equal("Synthetic typed password", input.Text);
            Assert.Equal(input.Text, secret.Text);
            Assert.NotEqual('\0', input.PasswordChar);
            var provider = Assert.IsAssignableFrom<IValueProvider>(ControlAutomationPeer.CreatePeerForElement(input)!);
            Assert.Equal(string.Empty, provider.Value);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void MaskedInput_ConcealsItsAutomationValueWhileRemainingEditable()
    {
        var input = new ProtectedTextBox { PasswordChar = '●', Text = "Synthetic secret" };
        var peer = ControlAutomationPeer.CreatePeerForElement(input)!;
        var provider = Assert.IsAssignableFrom<IValueProvider>(peer);

        Assert.Equal(string.Empty, provider.Value);
        Assert.False(provider.IsReadOnly);
        provider.SetValue("Synthetic replacement");
        Assert.Equal("Synthetic replacement", input.Text);
        Assert.Equal(string.Empty, provider.Value);
    }

    [AvaloniaFact]
    public void ExplicitRevealAndConceal_UpdateTheExistingAutomationProvider()
    {
        var input = new ProtectedTextBox { PasswordChar = '●', Text = "Synthetic secret" };
        var provider = Assert.IsAssignableFrom<IValueProvider>(ControlAutomationPeer.CreatePeerForElement(input)!);

        input.PasswordChar = '\0';
        Assert.Equal(input.Text, provider.Value);
        input.PasswordChar = '●';
        Assert.Equal(string.Empty, provider.Value);
        Assert.Equal("Synthetic secret", input.Text);
    }
}
