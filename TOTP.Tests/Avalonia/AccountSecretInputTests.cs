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
        Assert.Equal(
            "{Binding IsEditorSecretRevealAuthorized}",
            input.Attribute("IsRevealAuthorized")?.Value);
        Assert.Equal(
            "{Binding IsEditorSecretRevealed, Mode=TwoWay}",
            input.Attribute("IsRevealed")?.Value);
        Assert.Equal(
            "{Binding AuthorizeEditorSecretCommand}",
            input.Attribute("RevealRequestedCommand")?.Value);
        Assert.Equal(
            fixtureName == "DesktopMainWindow.axaml"
                ? "{DynamicResource Secret}"
                : "{Binding EditorSecretPlaceholder}",
            input.Attribute("PlaceholderText")?.Value);
        Assert.Null(input.Attribute("IsVisible"));
        Assert.Null(input.Attribute("PasswordChar"));

        Assert.DoesNotContain(
            document.Descendants(),
            element => element.Attribute(xaml + "Name")?.Value
                == "AuthorizeAccountSecretEditButton");
    }

    [Theory]
    [InlineData("DesktopMainWindow.axaml", "{DynamicResource IssuerPlaceholder}")]
    [InlineData("MobileMainView.axaml", "{Binding IssuerPlaceholderText}")]
    public void AccountEditorIssuer_UsesLocalizedExamplesPlaceholder(
        string fixtureName,
        string expectedPlaceholder)
    {
        var document = XDocument.Load(Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "Avalonia",
            fixtureName));
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

        var input = document
            .Descendants()
            .Single(element => element.Name.LocalName == "TextBox"
                && element.Attribute(xaml + "Name")?.Value == "AccountIssuerBox");

        Assert.Equal(expectedPlaceholder, input.Attribute("PlaceholderText")?.Value);
    }
}
