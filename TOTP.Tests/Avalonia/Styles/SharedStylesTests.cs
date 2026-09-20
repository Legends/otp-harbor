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

    [Fact]
    public void MobileDarkTheme_PreservesOriginalNavyAndPurplePalette()
    {
        var sourcePath = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "Avalonia",
            "MobileApp.axaml");
        var document = XDocument.Load(sourcePath);
        XNamespace avalonia = "https://github.com/avaloniaui";
        var dark = document
            .Descendants(avalonia + "ResourceDictionary")
            .Single(element => GetResourceKeyOrDefault(element) == "Dark");

        Assert.Equal("#0C1C33", BrushColor(dark, avalonia, "BrushWindowBackground"));
        Assert.Equal("#10213F", BrushColor(dark, avalonia, "BrushSurface"));
        Assert.Equal("#7D7FF4", BrushColor(dark, avalonia, "BrushAccent"));
    }

    [Fact]
    public void MobileAccountPresentation_UsesStableSelectionColorAndHairlineCountdown()
    {
        var viewPath = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "Avalonia",
            "MobileMainView.axaml");
        var view = XDocument.Load(viewPath);
        XNamespace avalonia = "https://github.com/avaloniaui";

        var accountList = view
            .Descendants(avalonia + "ListBox")
            .Single(element =>
                element.Attribute("Classes")?.Value.Contains(
                    "mobile-accounts",
                    StringComparison.Ordinal) == true);
        Assert.Contains("accounts", accountList.Attribute("Classes")?.Value);

        var countdown = accountList
            .Descendants(avalonia + "ProgressBar")
            .Single(element => element.Attribute("Classes")?.Value == "account-countdown");
        Assert.Null(countdown.Attribute("Height"));

        var sharedStylesPath = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "Avalonia",
            "SharedStyles.axaml");
        var sharedStyles = XDocument.Load(sharedStylesPath);
        var countdownStyle = sharedStyles
            .Descendants(avalonia + "Style")
            .Single(element =>
                element.Attribute("Selector")?.Value == "ProgressBar.account-countdown");
        Assert.Contains(
            countdownStyle.Elements(avalonia + "Setter"),
            setter => setter.Attribute("Property")?.Value == "Height"
                && setter.Attribute("Value")?.Value == "1");
        Assert.Contains(
            countdownStyle.Elements(avalonia + "Setter"),
            setter => setter.Attribute("Property")?.Value == "MinHeight"
                && setter.Attribute("Value")?.Value == "0");
        Assert.Contains(
            countdownStyle.Elements(avalonia + "Setter"),
            setter => setter.Attribute("Property")?.Value == "ClipToBounds"
                && setter.Attribute("Value")?.Value == "True");

        var appPath = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "Avalonia",
            "MobileApp.axaml");
        var app = XDocument.Load(appPath);
        var iconStyle = app
            .Descendants(avalonia + "Style")
            .Single(element => element.Attribute("Selector")?.Value == "Button.icon-action");
        Assert.Contains(
            iconStyle.Elements(avalonia + "Setter"),
            setter => setter.Attribute("Property")?.Value == "VerticalContentAlignment"
                && setter.Attribute("Value")?.Value == "Center");
    }

    [Fact]
    public void PeriodEditors_UseFiveSecondStepAndClearAffordance()
    {
        XNamespace avalonia = "https://github.com/avaloniaui";
        var fixtureDirectory = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "Avalonia");

        foreach (var fixtureName in new[] { "DesktopMainWindow.axaml", "MobileMainView.axaml" })
        {
            var document = XDocument.Load(Path.Combine(fixtureDirectory, fixtureName));
            var periodEditor = document
                .Descendants(avalonia + "NumericUpDown")
                .Single(element =>
                    element.Attribute("Value")?.Value.Contains(
                        "EditorPeriodSeconds",
                        StringComparison.Ordinal) == true);
            Assert.Equal("5", periodEditor.Attribute("Increment")?.Value);

            var innerRightContent = periodEditor
                .Elements(avalonia + "NumericUpDown.InnerRightContent")
                .Single();
            var clearButton = innerRightContent
                .Elements(avalonia + "Button")
                .Single(element => element.Attribute("Classes")?.Value == "numeric-clear");
            Assert.Equal(
                "{Binding ClearEditorPeriodCommand}",
                clearButton.Attribute("Command")?.Value);
            Assert.Equal(
                "{Binding HasEditorPeriodSeconds}",
                clearButton.Attribute("IsVisible")?.Value);
        }

        var mobileApp = XDocument.Load(Path.Combine(fixtureDirectory, "MobileApp.axaml"));
        var spinnerButtonStyle = mobileApp
            .Descendants(avalonia + "Style")
            .Single(element => element.Attribute("Selector")?.Value ==
                "NumericUpDown.mobile-period /template/ ButtonSpinner#PART_Spinner /template/ RepeatButton");
        Assert.Contains(
            spinnerButtonStyle.Elements(avalonia + "Setter"),
            setter => setter.Attribute("Property")?.Value == "MinWidth"
                && setter.Attribute("Value")?.Value == "48");

        var sharedStyles = XDocument.Load(Path.Combine(fixtureDirectory, "SharedStyles.axaml"));
        var clearStyle = sharedStyles
            .Descendants(avalonia + "Style")
            .Single(element => element.Attribute("Selector")?.Value == "Button.numeric-clear");
        Assert.Contains(
            clearStyle.Elements(avalonia + "Setter"),
            setter => setter.Attribute("Property")?.Value == "VerticalAlignment"
                && setter.Attribute("Value")?.Value == "Center");
        Assert.Contains(
            clearStyle.Elements(avalonia + "Setter"),
            setter => setter.Attribute("Property")?.Value == "Margin"
                && setter.Attribute("Value")?.Value == "0");
    }

    private static string BrushColor(XElement dictionary, XNamespace avalonia, string key) =>
        dictionary
            .Elements(avalonia + "SolidColorBrush")
            .Single(element => GetResourceKey(element) == key)
            .Attribute("Color")?.Value
        ?? throw new InvalidOperationException($"The {key} color is missing.");

    private static string? GetResourceKeyOrDefault(XElement element) =>
        element.Attributes()
            .SingleOrDefault(attribute => attribute.Name.LocalName == "Key")?
            .Value;

    private static string GetResourceKey(XElement element) =>
        element.Attributes().Single(attribute => attribute.Name.LocalName == "Key").Value;
}
