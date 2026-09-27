using System.Xml.Linq;

namespace TOTP.Tests.Avalonia.Mobile;

public sealed class MobileAccountInteractionTests
{
    [Fact]
    public void AccountRows_ExposeFavoriteActionAndCopyOnRowTap()
    {
        var document = XDocument.Load(Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "Avalonia",
            "MobileMainView.axaml"));
        XNamespace avalonia = "https://github.com/avaloniaui";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

        var favoriteButton = document
            .Descendants(avalonia + "Button")
            .Single(element => element.Attribute(xaml + "Name")?.Value == "AccountFavoriteButton");
        Assert.Equal("ToggleAccountFavorite", favoriteButton.Attribute("Click")?.Value);
        Assert.Contains(
            "FavoriteActionText",
            favoriteButton.Attributes().Single(attribute =>
                attribute.Name.LocalName == "AutomationProperties.Name").Value);

        var codeDisplay = document
            .Descendants(avalonia + "StackPanel")
            .Single(element => element.Attribute(xaml + "Name")?.Value == "AccountCodeDisplay");
        Assert.Null(codeDisplay.Attribute("Click"));
        var copyConfirmation = codeDisplay
            .Descendants(avalonia + "TextBlock")
            .Single(element => element.Attribute("Text")?.Value.Contains(
                "CopyConfirmation",
                StringComparison.Ordinal) == true);
        Assert.Equal("16", copyConfirmation.Attribute("MinHeight")?.Value);
        Assert.Equal("NoWrap", copyConfirmation.Attribute("TextWrapping")?.Value);
        Assert.Null(copyConfirmation.Attribute("IsVisible"));

        var accountList = document
            .Descendants(avalonia + "ListBox")
            .Single(element => element.Attribute("Classes")?.Value.Contains(
                "mobile-accounts",
                StringComparison.Ordinal) == true);
        Assert.Equal("-14,0", accountList.Attribute("Margin")?.Value);
        Assert.Equal(
            "Disabled",
            accountList.Attribute("ScrollViewer.HorizontalScrollBarVisibility")?.Value);
        Assert.Equal(
            "False",
            accountList.Attribute("ScrollViewer.IsScrollChainingEnabled")?.Value);
        Assert.Single(accountList.Descendants(avalonia + "VirtualizingStackPanel"));
        var accountContainer = favoriteButton
            .Ancestors(avalonia + "Border")
            .Single(element => element.Attribute("Margin")?.Value == "2,4");
        Assert.NotNull(accountContainer);

        var swipeRow = document
            .Descendants(avalonia + "Border")
            .Single(element => element.Attribute("SwipeGesture")?.Value == "TrackAccountSwipe");
        Assert.Equal("CompleteAccountSwipe", swipeRow.Attribute("SwipeGestureEnded")?.Value);
        Assert.Equal("CopyAccountCode", swipeRow.Attribute("Tapped")?.Value);
        Assert.Contains(
            swipeRow.Descendants(),
            element => element.Name.LocalName == "SwipeGestureRecognizer"
                && element.Attribute("CanHorizontallySwipe")?.Value == "True"
                && element.Attribute("Threshold")?.Value == "24");
    }

    [Fact]
    public void FavoriteFilter_UsesPastelYellowPaletteWithoutDisabledFlicker()
    {
        var fixtureDirectory = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "Avalonia");
        var view = XDocument.Load(Path.Combine(fixtureDirectory, "MobileMainView.axaml"));
        var favoriteFilter = view
            .Descendants()
            .Single(element => element.Name.LocalName == "ToggleButton"
                && element.Attribute("Classes")?.Value == "favorites-filter");
        Assert.NotNull(favoriteFilter);
        var favoriteIcon = favoriteFilter
            .Descendants()
            .Single(element => element.Name.LocalName == "SymbolIcon"
                && element.Attribute("Kind")?.Value == "Favorite");
        Assert.Equal(
            "{DynamicResource BrushFavoriteFilterIcon}",
            favoriteIcon.Attribute("Foreground")?.Value);

        var app = XDocument.Load(Path.Combine(fixtureDirectory, "MobileApp.axaml"));
        Assert.Equal("#FFF4C2", ThemeResourceColor(app, "Light", "BrushFavoriteFilterBackground"));
        Assert.Equal("#FFE89A", ThemeResourceColor(app, "Light", "BrushFavoriteFilterBackgroundActive"));
        Assert.Equal("#FABD62", ThemeResourceColor(app, "Light", "BrushFavoriteFilterIcon"));
        Assert.Equal("#40361F", ThemeResourceColor(app, "Dark", "BrushFavoriteFilterBackground"));
        Assert.Equal("#514526", ThemeResourceColor(app, "Dark", "BrushFavoriteFilterBackgroundActive"));
        Assert.Equal("#9CA3AF", ThemeResourceColor(app, "Light", "BrushFavoriteFilterForeground"));
        Assert.Equal("#E5E7EB", ThemeResourceColor(app, "Dark", "BrushFavoriteFilterForeground"));
        Assert.Equal("#FFD58A", ThemeResourceColor(app, "Dark", "BrushFavoriteFilterIcon"));

        var stableDisabledSelectors = app
            .Descendants()
            .Where(element => element.Name.LocalName == "Style")
            .Where(element => element.Attribute("Selector")?.Value is
                "Button.icon-action:disabled" or
                "ToggleButton.favorites-filter:disabled")
            .ToArray();
        Assert.Equal(2, stableDisabledSelectors.Length);
        Assert.All(stableDisabledSelectors, style => Assert.Contains(
            style.Elements(),
            setter => setter.Attribute("Property")?.Value == "Opacity"
                && setter.Attribute("Value")?.Value == "1"));
    }

    private static string ThemeResourceColor(
        XDocument document,
        string theme,
        string resourceKey)
    {
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var themeDictionary = document
            .Descendants()
            .Single(element => element.Name.LocalName == "ResourceDictionary"
                && element.Attribute(xaml + "Key")?.Value == theme);

        return themeDictionary
            .Elements()
            .Single(element => element.Attribute(xaml + "Key")?.Value == resourceKey)
            .Attribute("Color")!
            .Value;
    }
}
