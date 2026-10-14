using System.Xml.Linq;

namespace TOTP.Tests.Avalonia.Mobile;

public sealed class AndroidSplashResourceTests
{
    [Fact]
    public void Splash_UsesSystemThemeSpecificBackgroundAndSystemBarAppearance()
    {
        var fixtureRoot = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Android");
        var lightResources = XDocument.Load(Path.Combine(fixtureRoot, "values", "colors.xml"));
        var darkResources = XDocument.Load(Path.Combine(fixtureRoot, "values-night", "colors.xml"));

        Assert.Equal("#F7FAFF", GetResource(lightResources, "color", "splash_background"));
        Assert.Equal("true", GetResource(lightResources, "bool", "splash_light_system_bars"));
        Assert.Equal("#0C1C33", GetResource(darkResources, "color", "splash_background"));
        Assert.Equal("false", GetResource(darkResources, "bool", "splash_light_system_bars"));

        foreach (var relativePath in new[]
                 {
                     Path.Combine("values", "styles.xml"),
                     Path.Combine("values-v31", "styles.xml")
                 })
        {
            var styles = XDocument.Load(Path.Combine(fixtureRoot, relativePath));
            var splashTheme = styles
                .Descendants("style")
                .Single(element => element.Attribute("name")?.Value == "OtpHarborTheme");

            Assert.Equal(
                "@bool/splash_light_system_bars",
                GetStyleItem(splashTheme, "android:windowLightStatusBar"));
            Assert.Equal(
                "@bool/splash_light_system_bars",
                GetStyleItem(splashTheme, "android:windowLightNavigationBar"));
            Assert.Equal(
                "@color/splash_background",
                GetStyleItem(splashTheme, "android:statusBarColor"));
            Assert.Equal(
                "@color/splash_background",
                GetStyleItem(splashTheme, "android:navigationBarColor"));
        }
    }

    private static string GetResource(XDocument document, string elementName, string resourceName) =>
        document
            .Descendants(elementName)
            .Single(element => element.Attribute("name")?.Value == resourceName)
            .Value;

    private static string GetStyleItem(XElement style, string itemName) =>
        style
            .Elements("item")
            .Single(element => element.Attribute("name")?.Value == itemName)
            .Value;
}
