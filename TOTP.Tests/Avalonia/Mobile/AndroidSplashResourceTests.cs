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

        Assert.Equal("#FFFFFF", GetResource(lightResources, "color", "splash_background"));
        Assert.Equal("#168AE0", GetResource(lightResources, "color", "splash_loader_accent"));
        Assert.Equal("#0B6FB8", GetResource(lightResources, "color", "splash_loader_focus"));
        Assert.Equal("#619FB3C7", GetResource(lightResources, "color", "splash_loader_orbit"));
        Assert.Equal("true", GetResource(lightResources, "bool", "splash_light_system_bars"));
        Assert.Equal("#0C1C33", GetResource(darkResources, "color", "splash_background"));
        Assert.Equal("#7D7FF4", GetResource(darkResources, "color", "splash_loader_accent"));
        Assert.Equal("#9899FF", GetResource(darkResources, "color", "splash_loader_focus"));
        Assert.Equal("#612F5E9E", GetResource(darkResources, "color", "splash_loader_orbit"));
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

    [Fact]
    public void Splash_OnAndroid12AndLater_UsesTheAnimatedAppIcon()
    {
        var fixtureRoot = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Android");
        var styles = XDocument.Load(Path.Combine(fixtureRoot, "values-v31", "styles.xml"));
        var splashTheme = styles
            .Descendants("style")
            .Single(element => element.Attribute("name")?.Value == "OtpHarborTheme");
        var animation = XDocument.Load(Path.Combine(
            fixtureRoot,
            "drawable-v31",
            "splash_icon_animated.xml"));
        var orbitFrame = XDocument.Load(Path.Combine(
            fixtureRoot,
            "drawable-v31",
            "splash_icon_orbit_000.xml"));

        Assert.Equal(
            "@drawable/splash_icon_animated",
            GetStyleItem(splashTheme, "android:windowSplashScreenAnimatedIcon"));
        Assert.Equal(
            "4000",
            GetStyleItem(splashTheme, "android:windowSplashScreenAnimationDuration"));
        Assert.Equal("false", animation.Root?.Attribute(AndroidNamespace + "oneshot")?.Value);
        Assert.Equal(8, animation.Root?.Elements("item").Count());
        Assert.Contains(
            animation.Root!.Elements("item"),
            item => item.Attribute(AndroidNamespace + "drawable")?.Value
                == "@drawable/splash_icon_orbit_000");
        Assert.Equal(
            "@drawable/app_icon",
            orbitFrame.Descendants("item").Single(item =>
                    item.Attribute(AndroidNamespace + "drawable") is not null)
                .Attribute(AndroidNamespace + "drawable")?.Value);
        Assert.Equal(
            "224dp",
            orbitFrame.Descendants("item").First()
                .Attribute(AndroidNamespace + "width")?.Value);
        Assert.Equal(
            "176dp",
            orbitFrame.Descendants("item").Last()
                .Attribute(AndroidNamespace + "width")?.Value);
        Assert.Contains(
            orbitFrame.Descendants("group"),
            group => group.Attribute(AndroidNamespace + "rotation")?.Value == "0");
        Assert.True(orbitFrame.Descendants("path").Count() >= 5);
        Assert.Contains(
            orbitFrame.Descendants("path"),
            path => path.Attribute(AndroidNamespace + "strokeColor")?.Value
                == "@color/splash_loader_orbit");
    }

    [Fact]
    public void Splash_AllStartupSurfaces_UseTheSameLoaderComposition()
    {
        var fixtureRoot = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        var legacySplash = XDocument.Load(Path.Combine(
            fixtureRoot,
            "Android",
            "drawable",
            "splash_screen.xml"));
        var android12Splash = XDocument.Load(Path.Combine(
            fixtureRoot,
            "Android",
            "drawable-v31",
            "splash_icon_orbit_000.xml"));
        var mobileView = XDocument.Load(Path.Combine(
            fixtureRoot,
            "Avalonia",
            "MobileMainView.axaml"));
        XNamespace avalonia = "https://github.com/avaloniaui";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

        AssertNativeLoaderDimensions(legacySplash);
        AssertNativeLoaderDimensions(android12Splash);

        var overlay = mobileView
            .Descendants(avalonia + "Grid")
            .Single(element => element.Attribute(xaml + "Name")?.Value
                == "StartupLoadingOverlay");
        var loaderCanvas = overlay
            .Descendants(avalonia + "Canvas")
            .Single(element => element.Attribute("Width")?.Value == "224");
        var loaderLogo = overlay
            .Descendants(avalonia + "Image")
            .Single(element => element.Attribute("Classes")?.Value
                == "startup-loading-logo");

        Assert.Equal("224", loaderCanvas.Attribute("Height")?.Value);
        Assert.Equal("176", loaderLogo.Attribute("Width")?.Value);
        Assert.Equal("176", loaderLogo.Attribute("Height")?.Value);
        Assert.Contains(
            overlay.Descendants(avalonia + "Ellipse"),
            ellipse => ellipse.Attribute("Stroke")?.Value
                == "{DynamicResource BrushControlBorder}");
    }

    private static void AssertNativeLoaderDimensions(XDocument loader)
    {
        var items = loader.Root!.Elements("item").ToArray();
        var orbit = items.Single(item => item.Element("vector") is not null);
        var logo = items.Single(item =>
            item.Attribute(AndroidNamespace + "drawable")?.Value == "@drawable/app_icon");

        Assert.Equal("224dp", orbit.Attribute(AndroidNamespace + "width")?.Value);
        Assert.Equal("224dp", orbit.Attribute(AndroidNamespace + "height")?.Value);
        Assert.Equal("176dp", logo.Attribute(AndroidNamespace + "width")?.Value);
        Assert.Equal("176dp", logo.Attribute(AndroidNamespace + "height")?.Value);
    }

    private static readonly XNamespace AndroidNamespace =
        "http://schemas.android.com/apk/res/android";

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
