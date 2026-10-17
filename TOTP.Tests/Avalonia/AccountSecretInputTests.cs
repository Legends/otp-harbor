using System.Xml.Linq;

namespace TOTP.Tests.Avalonia;

public sealed class AccountSecretInputTests
{
    [Theory]
    [InlineData("DesktopMainWindow.axaml")]
    [InlineData("MobileMainView.axaml")]
    public void AccountEditorSecret_UsesSharedRevealablePlainTextControl(string fixtureName)
    {
        var document = XDocument.Load(Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "Avalonia",
            fixtureName));
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

        var input = document
            .Descendants()
            .Single(element => element.Name.LocalName == "RevealableSecretInput"
                && element.Attribute(xaml + "Name")?.Value == "AccountSecretInput");

        Assert.Equal("{Binding EditorSecret, Mode=TwoWay}", input.Attribute("Text")?.Value);
        Assert.Null(input.Attribute("PasswordChar"));
    }
}
