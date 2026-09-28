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

        var groupItems = document
            .Descendants(avalonia + "ItemsControl")
            .Single(element => element.Attribute("ItemsSource")?.Value == "{Binding Groups}");
        var groupButton = groupItems
            .Descendants(avalonia + "Button")
            .Single(element => element.Attribute("Classes")?.Value.Contains(
                "account-group",
                StringComparison.Ordinal) == true);
        Assert.Equal("{Binding SelectCommand}", groupButton.Attribute("Command")?.Value);
        var groupCard = groupButton
            .Ancestors(avalonia + "Border")
            .Single(element => element.Attribute("Classes")?.Value.Contains(
                "group-card",
                StringComparison.Ordinal) == true);
        Assert.Equal("{Binding Background}", groupCard.Attribute("Background")?.Value);
        Assert.Contains("group-card", groupCard.Attribute("Classes")?.Value);
        Assert.Equal("{Binding IsSelected}", groupCard.Attributes().Single(attribute =>
            attribute.Name.LocalName == "Classes.selected").Value);
        var editGroupButton = groupCard
            .Descendants(avalonia + "Button")
            .Single(element => element.Attribute("Command")?.Value == "{Binding EditCommand}");
        Assert.Equal("1", editGroupButton.Attribute("Grid.Column")?.Value);

        var groupsStrip = document
            .Descendants(avalonia + "Grid")
            .Single(element => element.Attribute(xaml + "Name")?.Value == "AccountGroupsStrip");
        Assert.Equal("2", groupsStrip.Attribute("Grid.Row")?.Value);
        Assert.Equal("64", groupsStrip.Attribute("MinHeight")?.Value);
        Assert.Equal("Center", groupsStrip.Attribute("VerticalAlignment")?.Value);

        var createGroupButton = document
            .Descendants(avalonia + "Button")
            .Single(element => element.Attribute("Command")?.Value == "{Binding BeginAddGroupCommand}");
        Assert.Equal("2", createGroupButton.Attribute("Grid.Column")?.Value);
        Assert.Contains(
            createGroupButton.Descendants(),
            element => element.Name.LocalName == "SymbolIcon"
                && element.Attribute("Kind")?.Value == "FolderAdd");
        var cameraIcon = document
            .Descendants()
            .Single(element => element.Name.LocalName == "SymbolIcon"
                && element.Attribute("Kind")?.Value == "Camera");
        Assert.Equal("0,2,0,0", cameraIcon.Attribute("Margin")?.Value);

        Assert.DoesNotContain(
            document.Descendants(avalonia + "TextBlock"),
            element => element.Attribute("Text")?.Value == "{Binding SearchResultSummary}");
        Assert.DoesNotContain(
            document.Descendants(avalonia + "TextBlock"),
            element => element.Attribute("Text")?.Value == "{Binding AccountSwipeHintText}");

        var accountList = document
            .Descendants(avalonia + "ListBox")
            .Single(element => element.Attribute("Classes")?.Value.Contains(
                "mobile-accounts",
                StringComparison.Ordinal) == true);
        Assert.Equal("4", accountList.Attribute("Grid.Row")?.Value);
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
    public void GroupEditor_ExposesLocalizedCreateEditAndDeleteWorkflow()
    {
        var document = XDocument.Load(Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "Avalonia",
            "MobileMainView.axaml"));
        XNamespace avalonia = "https://github.com/avaloniaui";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

        var nameBox = document
            .Descendants(avalonia + "TextBox")
            .Single(element => element.Attribute(xaml + "Name")?.Value == "GroupNameBox");
        Assert.Equal("64", nameBox.Attribute("MaxLength")?.Value);
        Assert.Contains("GroupNameText", nameBox.Attribute("PlaceholderText")?.Value);

        var colorPicker = document
            .Descendants(avalonia + "ListBox")
            .Single(element => element.Attribute(xaml + "Name")?.Value == "GroupColorPicker");
        Assert.Equal("{Binding GroupColorOptions}", colorPicker.Attribute("ItemsSource")?.Value);
        Assert.Equal("{Binding SelectedGroupColor, Mode=TwoWay}", colorPicker.Attribute("SelectedItem")?.Value);

        Assert.Contains(
            document.Descendants(avalonia + "ItemsControl"),
            element => element.Attribute("ItemsSource")?.Value == "{Binding GroupEditorAccounts}");
        var accountSearch = document
            .Descendants(avalonia + "TextBox")
            .Single(element => element.Attribute(xaml + "Name")?.Value == "GroupAccountSearchBox");
        Assert.Equal(
            "{Binding GroupEditorSearchText, Mode=TwoWay}",
            accountSearch.Attribute("Text")?.Value);
        var accountPickerScroller = document
            .Descendants(avalonia + "ScrollViewer")
            .Single(element => element.Attribute("MaxHeight")?.Value == "220");
        Assert.Equal("Hidden", accountPickerScroller.Attribute("VerticalScrollBarVisibility")?.Value);
        Assert.Equal("False", accountPickerScroller.Attribute("IsScrollChainingEnabled")?.Value);
        var accountSelection = accountPickerScroller
            .Descendants(avalonia + "CheckBox")
            .Single(element => element.Attribute("IsChecked")?.Value.Contains(
                "IsSelected",
                StringComparison.Ordinal) == true);
        Assert.Equal("Center", accountSelection.Attribute("VerticalContentAlignment")?.Value);
        Assert.Contains(
            document.Descendants(avalonia + "Button"),
            element => element.Attribute("Command")?.Value == "{Binding SaveGroupCommand}");
        Assert.Contains(
            document.Descendants(avalonia + "Button"),
            element => element.Attribute("Command")?.Value == "{Binding BeginDeleteGroupCommand}");
        Assert.Contains(
            document.Descendants(avalonia + "Button"),
            element => element.Attribute("Command")?.Value == "{Binding ConfirmDeleteGroupCommand}");
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
        var favoriteContent = favoriteFilter
            .Elements()
            .Single(element => element.Name.LocalName == "StackPanel");
        Assert.Equal("Horizontal", favoriteContent.Attribute("Orientation")?.Value);
        Assert.Equal(2, favoriteContent.Elements().Count(element => element.Name.LocalName == "TextBlock"));
        Assert.DoesNotContain(
            favoriteContent.Elements(),
            element => element.Name.LocalName == "StackPanel");

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

        var selectedGroupStyle = app
            .Descendants()
            .Single(element => element.Name.LocalName == "Style"
                && element.Attribute("Selector")?.Value == "Border.group-card.selected");
        Assert.Contains(
            selectedGroupStyle.Elements(),
            setter => setter.Attribute("Property")?.Value == "BorderThickness"
                && setter.Attribute("Value")?.Value == "1");
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
