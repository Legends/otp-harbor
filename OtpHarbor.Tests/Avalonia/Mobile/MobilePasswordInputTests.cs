using System.Xml.Linq;

namespace TOTP.Tests.Avalonia.Mobile;

public sealed class MobilePasswordInputTests
{
    [Fact]
    public void EveryMaskedInput_UsesLocalizedRevealControl()
    {
        var document = XDocument.Load(Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "Avalonia",
            "MobileMainView.axaml"));

        var revealInputs = document
            .Descendants()
            .Where(element => element.Name.LocalName == "RevealableSecretInput")
            .ToArray();

        Assert.Equal(13, revealInputs.Length);
        Assert.All(revealInputs, input =>
        {
            Assert.Contains("RevealPasswordText", input.Attribute("RevealButtonAccessibleName")?.Value);
            Assert.Contains("RevealPasswordHelpText", input.Attribute("RevealButtonHelpText")?.Value);
        });
        Assert.DoesNotContain(
            document.Descendants(),
            element => element.Attribute("PasswordChar") is not null);

        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var accountSecretInput = Assert.Single(
            revealInputs,
            input => input.Attribute(xaml + "Name")?.Value == "AccountSecretInput");
        Assert.Equal(
            "{Binding EditorSecret, Mode=TwoWay}",
            accountSecretInput.Attribute("Text")?.Value);
    }
}
