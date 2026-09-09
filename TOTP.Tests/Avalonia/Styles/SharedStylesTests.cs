using System.Xml.Linq;

namespace TOTP.Tests.Avalonia.Styles;

public sealed class SharedStylesTests
{
    [Fact]
    public void FluentThemeUsesStaticWindowsBlueAcrossPlatforms()
    {
        var sourcePath = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "Avalonia",
            "App.axaml");
        var document = XDocument.Load(sourcePath);
        XNamespace avalonia = "https://github.com/avaloniaui";

        var palettes = document
            .Descendants(avalonia + "ColorPaletteResources")
            .ToArray();

        Assert.Equal(2, palettes.Length);
        Assert.Equal(
            ["Dark", "Light"],
            palettes
                .Select(GetResourceKey)
                .Order(StringComparer.Ordinal)
                .ToArray());
        Assert.All(
            palettes,
            palette => Assert.Equal("#0078D4", palette.Attribute("Accent")?.Value));
    }

    [Fact]
    public void ProgressBarsUseStaticWindowsBlueAcrossPlatforms()
    {
        var sourcePath = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "Avalonia",
            "SharedStyles.axaml");
        var document = XDocument.Load(sourcePath);
        XNamespace avalonia = "https://github.com/avaloniaui";

        var progressBrush = document
            .Descendants(avalonia + "SolidColorBrush")
            .Single(element =>
                string.Equals(
                    element.Attributes().Single(attribute => attribute.Name.LocalName == "Key").Value,
                    "BrushProgressIndicator",
                    StringComparison.Ordinal));

        Assert.Equal("#0078D4", progressBrush.Attribute("Color")?.Value);
        Assert.DoesNotContain(
            progressBrush.Ancestors(),
            ancestor => ancestor.Name == avalonia + "ResourceDictionary.ThemeDictionaries");

        var progressStyle = document
            .Descendants(avalonia + "Style")
            .Single(element => element.Attribute("Selector")?.Value == "ProgressBar");
        var foregroundSetter = progressStyle
            .Elements(avalonia + "Setter")
            .Single(element => element.Attribute("Property")?.Value == "Foreground");

        Assert.Equal(
            "{StaticResource BrushProgressIndicator}",
            foregroundSetter.Attribute("Value")?.Value);
    }

    private static string GetResourceKey(XElement element) =>
        element.Attributes().Single(attribute => attribute.Name.LocalName == "Key").Value;
}
