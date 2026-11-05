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
        var mainTheme = styles
            .Descendants("style")
            .Single(element => element.Attribute("name")?.Value == "OtpHarborTheme.Main");
        var animation = XDocument.Load(Path.Combine(
            fixtureRoot,
            "drawable-v31",
            "splash_icon_animated.xml"));

        Assert.Equal(
            "@drawable/splash_icon_animated",
            GetStyleItem(splashTheme, "android:windowSplashScreenAnimatedIcon"));
        Assert.Equal(
            "3200",
            GetStyleItem(splashTheme, "android:windowSplashScreenAnimationDuration"));
        Assert.Equal("3200", GetStyleItem(splashTheme, "windowSplashScreenAnimationDuration"));
        var mobileView = XDocument.Load(Path.Combine(
            AppContext.BaseDirectory, "Fixtures", "Avalonia", "MobileMainView.axaml"));
        XNamespace avalonia = "https://github.com/avaloniaui";
        var orbitStyle = mobileView.Descendants(avalonia + "Style").Single(style =>
            style.Attribute("Selector")?.Value == "Grid.startup-loading-orbit");
        var rotationDuration = TimeSpan.Parse(
            orbitStyle.Descendants(avalonia + "Animation").Single().Attribute("Duration")!.Value,
            System.Globalization.CultureInfo.InvariantCulture);
        var animator = Assert.Single(animation.Descendants("objectAnimator"));
        Assert.Equal(rotationDuration.TotalMilliseconds,
            double.Parse(animator.Attribute(AndroidNamespace + "duration")!.Value,
                System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(
            "@drawable/splash_screen",
            GetStyleItem(mainTheme, "android:windowBackground"));
        Assert.Equal("animated-vector", animation.Root!.Name.LocalName);
        Assert.Equal("-1", animator.Attribute(AndroidNamespace + "repeatCount")?.Value);
        Assert.Equal("@android:interpolator/linear", animator.Attribute(AndroidNamespace + "interpolator")?.Value);
        Assert.Equal("rotation", animator.Attribute(AndroidNamespace + "propertyName")?.Value);
        Assert.Equal("0", animator.Attribute(AndroidNamespace + "valueFrom")?.Value);
        Assert.Equal("360", animator.Attribute(AndroidNamespace + "valueTo")?.Value);
        Assert.Equal("stars", Assert.Single(animation.Descendants("target")).Attribute(AndroidNamespace + "name")?.Value);
        var vector = Assert.Single(animation.Descendants("vector"));
        Assert.Equal("432dp", vector.Attribute(AndroidNamespace + "width")?.Value);
        var logo = animation.Descendants("group").Single(group => group.Attribute(AndroidNamespace + "name")?.Value == "logo");
        Assert.Equal("0.13671875", logo.Attribute(AndroidNamespace + "scaleX")?.Value);
        Assert.NotEmpty(logo.Elements("path"));
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
            "splash_icon_animated.xml"));
        var mobileView = XDocument.Load(Path.Combine(
            fixtureRoot,
            "Avalonia",
            "MobileMainView.axaml"));
        XNamespace avalonia = "https://github.com/avaloniaui";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

        AssertNativeLoaderDimensions(legacySplash);
        Assert.Equal("288", android12Splash.Descendants("vector").Single()
            .Attribute(AndroidNamespace + "viewportWidth")?.Value);

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
        Assert.Equal("140", loaderLogo.Attribute("Width")?.Value);
        Assert.Equal("140", loaderLogo.Attribute("Height")?.Value);
        Assert.Empty(overlay.Descendants(avalonia + "Ellipse"));
        var mobileStars = loaderCanvas.Descendants(avalonia + "Path").ToArray();
        var nativeStars = android12Splash.Descendants("group")
            .Single(group => group.Attribute(AndroidNamespace + "name")?.Value == "stars")
            .Elements("path").ToArray();
        Assert.Equal(3, mobileStars.Length);
        for (var i = 0; i < mobileStars.Length; i++)
        {
            Assert.Equal(nativeStars[i].Attribute(AndroidNamespace + "pathData")!.Value,
                mobileStars[i].Attribute("Data")!.Value);
            // Rotation preserves this radius; every vertex must fit in Android's
            // 192dp-diameter visible region, with a small safety margin.
            var coordinates = System.Text.RegularExpressions.Regex.Matches(
                mobileStars[i].Attribute("Data")!.Value, @"-?\d+(?:\.\d+)?")
                .Select(match => double.Parse(match.Value,
                    System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            for (var point = 0; point < coordinates.Length; point += 2)
                Assert.True(double.Hypot(coordinates[point] - 112, coordinates[point + 1] - 112) <= 94);
        }
    }

    [Fact]
    public void Splash_HoldsNativeSurfaceUntilAvaloniaLoaderIsLaidOut()
    {
        var fixtureRoot = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Android");
        var activitySource = File.ReadAllText(Path.Combine(fixtureRoot, "MainActivity.cs"));
        var applicationSource = File.ReadAllText(Path.Combine(
            fixtureRoot,
            "OtpHarborApplication.cs"));

        var installIndex = activitySource.IndexOf(
            "AndroidXSplashScreen.InstallSplashScreen(this);",
            StringComparison.Ordinal);
        var baseCreateIndex = activitySource.IndexOf(
            "base.OnCreate(savedInstanceState);",
            StringComparison.Ordinal);
        Assert.True(installIndex >= 0 && installIndex < baseCreateIndex);
        Assert.Contains("SetOnExitAnimationListener(_startupSplashExitListener)", activitySource);
        Assert.Contains("IOnExitAnimationListener", activitySource);
        Assert.DoesNotContain("SetKeepOnScreenCondition", activitySource);
        Assert.Contains("view.LayoutUpdated += firstLayout;", applicationSource);
        Assert.Contains("view.LayoutUpdated -= firstLayout;", applicationSource);
        Assert.Contains("topLevel.RequestAnimationFrame", applicationSource);
        Assert.Contains("topLevel.RequestAnimationFrame(_ => NotifyStartupSurfaceReady())", applicationSource);
        Assert.Contains("activity.ReleaseStartupSplash();", applicationSource);
    }

    private static void AssertNativeLoaderDimensions(XDocument loader, string size = "224dp")
    {
        var items = loader.Root!.Elements("item").ToArray();
        var orbit = items.Single(item => item.Element("vector") is not null);
        var logo = items.Single(item =>
            item.Attribute(AndroidNamespace + "drawable")?.Value == "@drawable/app_icon");

        Assert.Equal(size, orbit.Attribute(AndroidNamespace + "width")?.Value);
        Assert.Equal(size, orbit.Attribute(AndroidNamespace + "height")?.Value);
        Assert.Equal("140dp", logo.Attribute(AndroidNamespace + "width")?.Value);
        Assert.Equal("140dp", logo.Attribute(AndroidNamespace + "height")?.Value);
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
