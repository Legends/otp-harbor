using System.Xml.Linq;

namespace TOTP.Tests.Avalonia.Mobile;

public sealed class MobileAccountInteractionTests
{
    [Fact]
    public void LockScreen_UsesAccessibleFingerprintButtonForBiometricUnlock()
    {
        var document = XDocument.Load(Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "Avalonia",
            "MobileMainView.axaml"));
        XNamespace avalonia = "https://github.com/avaloniaui";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

        var button = document
            .Descendants(avalonia + "Button")
            .Single(element => element.Attribute(xaml + "Name")?.Value
                == "FingerprintUnlockButton");

        Assert.Equal("{Binding BiometricUnlockCommand}", button.Attribute("Command")?.Value);
        Assert.Null(button.Attribute("Content"));
        Assert.Contains(
            "BiometricUnlockText",
            button.Attributes().Single(attribute =>
                attribute.Name.LocalName == "AutomationProperties.Name").Value);
        Assert.Equal("92", button.Attribute("Width")?.Value);
        Assert.Equal("primary biometric-unlock", button.Attribute("Classes")?.Value);
        Assert.Equal("1", button.Attribute("Grid.Row")?.Value);
        Assert.Equal("Center", button.Attribute("VerticalAlignment")?.Value);
        var fingerprint = button
            .Descendants()
            .Single(element => element.Name.LocalName == "FingerprintIcon");
        Assert.Equal("72", fingerprint.Attribute("IconSize")?.Value);

        var anchor = document
            .Descendants(avalonia + "Grid")
            .Single(element => element.Attribute(xaml + "Name")?.Value
                == "FingerprintUnlockAnchor");
        Assert.Equal("1", anchor.Attribute("Grid.Row")?.Value);
        Assert.Equal("5*,0,*", anchor.Attribute("RowDefinitions")?.Value);
        Assert.Equal(
            "{Binding IsFingerprintUnlockVisible}",
            anchor.Attribute("IsVisible")?.Value);
        Assert.Same(anchor, button.Parent);

        var artwork = document
            .Descendants(avalonia + "Border")
            .Single(element => element.Attribute(xaml + "Name")?.Value
                == "MobileLockScreenArtwork");
        Assert.Equal(
            "{DynamicResource BrushBiometricLockScreenArtwork}",
            artwork.Attribute("Background")?.Value);
        Assert.Equal("{Binding IsUnlockVisible}", artwork.Attribute("IsVisible")?.Value);
    }

    [Fact]
    public void LockScreen_DeviceCredentialButtonUsesDedicatedLocalizedTextBinding()
    {
        var document = XDocument.Load(Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "Avalonia",
            "MobileMainView.axaml"));
        XNamespace avalonia = "https://github.com/avaloniaui";

        var button = document
            .Descendants(avalonia + "Button")
            .Single(element => element.Attribute("IsVisible")?.Value
                == "{Binding IsDeviceCredentialUnlockVisible}");

        Assert.Equal("{Binding UnlockWithDevicePinText}", button.Attribute("ToolTip.Tip")?.Value);
        Assert.Equal("68", button.Attribute("MinHeight")?.Value);
        Assert.Equal("Stretch", button.Attribute("HorizontalContentAlignment")?.Value);
        Assert.Equal("Center", button.Attribute("VerticalContentAlignment")?.Value);
        var buttonText = button
            .Elements(avalonia + "TextBlock")
            .Single();
        Assert.Equal(
            "{Binding DeviceCredentialUnlockButtonText}",
            buttonText.Attribute("Text")?.Value);
        Assert.Equal("Center", buttonText.Attribute("TextAlignment")?.Value);
        Assert.Equal("Wrap", buttonText.Attribute("TextWrapping")?.Value);
    }

    [Fact]
    public void LongImports_ShowAnIndeterminateProgressOverlay()
    {
        var document = XDocument.Load(Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "Avalonia",
            "MobileMainView.axaml"));
        XNamespace avalonia = "https://github.com/avaloniaui";

        var overlay = document
            .Descendants(avalonia + "Grid")
            .Single(element => element.Attribute("IsVisible")?.Value
                == "{Binding IsImportProgressVisible}");

        Assert.Equal("90", overlay.Attribute("Panel.ZIndex")?.Value);
        Assert.Contains(
            overlay.Descendants(avalonia + "ProgressBar"),
            progress => progress.Attribute("IsIndeterminate")?.Value == "True");
        Assert.Contains(
            overlay.Descendants(avalonia + "TextBlock"),
            text => text.Attribute("Text")?.Value == "{Binding ImportProgressText}");
    }

    [Fact]
    public void AccountSearch_ClearButtonUsesCenteredVectorIcon()
    {
        var document = XDocument.Load(Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "Avalonia",
            "MobileMainView.axaml"));
        XNamespace avalonia = "https://github.com/avaloniaui";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

        var searchBox = document
            .Descendants(avalonia + "TextBox")
            .Single(element => element.Attribute(xaml + "Name")?.Value == "AccountSearchBox");
        var clearButton = searchBox.Parent?
            .Elements(avalonia + "Button")
            .Single();

        Assert.NotNull(clearButton);
        Assert.Equal("{Binding ClearSearchCommand}", clearButton.Attribute("Command")?.Value);
        Assert.Equal("Center", clearButton.Attribute("HorizontalContentAlignment")?.Value);
        Assert.Equal("Center", clearButton.Attribute("VerticalContentAlignment")?.Value);
        var closeIcon = clearButton
            .Descendants()
            .Single(element => element.Name.LocalName == "SymbolIcon");
        Assert.Equal("Close", closeIcon.Attribute("Kind")?.Value);
        Assert.Equal("18", closeIcon.Attribute("IconSize")?.Value);
    }

    [Fact]
    public void AccountList_BuffersVirtualizedRowsForSmoothMobileScrolling()
    {
        var document = XDocument.Load(Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "Avalonia",
            "MobileMainView.axaml"));
        XNamespace avalonia = "https://github.com/avaloniaui";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

        var accountList = document
            .Descendants(avalonia + "ListBox")
            .Single(element => element.Attribute(xaml + "Name")?.Value == "AccountList");
        var itemsPanel = accountList
            .Descendants(avalonia + "VirtualizingStackPanel")
            .Single();

        Assert.Equal("1", itemsPanel.Attribute("CacheLength")?.Value);
    }

    [Fact]
    public void MainShell_DefinesCompactPhoneBreakpointsWithoutShrinkingTouchHeight()
    {
        var document = XDocument.Load(Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "Avalonia",
            "MobileMainView.axaml"));
        XNamespace avalonia = "https://github.com/avaloniaui";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

        var shell = document
            .Descendants(avalonia + "Grid")
            .Single(element => element.Attribute("Container.Name")?.Value == "MobileShell");
        Assert.Equal("Width", shell.Attribute("Container.Sizing")?.Value);

        var compactQuery = document
            .Descendants(avalonia + "ContainerQuery")
            .Single(element => element.Attribute("Query")?.Value == "max-width:380");
        Assert.Contains(
            compactQuery.Descendants(avalonia + "Style"),
            style => style.Attribute("Selector")?.Value == "Grid#MobileMainContent");
        var compactActionStyle = compactQuery
            .Descendants(avalonia + "Style")
            .Single(style => style.Attribute("Selector")?.Value == "Button.account-toolbar-action");
        Assert.Contains(
            compactActionStyle.Elements(avalonia + "Setter"),
            setter => setter.Attribute("Property")?.Value == "MinWidth"
                && setter.Attribute("Value")?.Value == "48");

        var toolbar = document
            .Descendants(avalonia + "Grid")
            .Single(element => element.Attribute(xaml + "Name")?.Value == "AccountToolbar");
        Assert.Equal(
            4,
            toolbar.Elements(avalonia + "Button").Count(button =>
                button.Attribute("Classes")?.Value.Contains(
                    "account-toolbar-action",
                    StringComparison.Ordinal) == true));
    }

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

        var countdown = document
            .Descendants(avalonia + "ProgressBar")
            .Single(element => element.Attribute("Classes")?.Value == "account-countdown");
        Assert.Equal(
            "{Binding IsCodeLoading}",
            countdown.Attribute("IsIndeterminate")?.Value);

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
        var groupName = groupButton
            .Descendants(avalonia + "TextBlock")
            .Single(element => element.Attribute("Text")?.Value == "{Binding Name}");
        Assert.Equal("CharacterEllipsis", groupName.Attribute("TextTrimming")?.Value);
        Assert.Equal("NoWrap", groupName.Attribute("TextWrapping")?.Value);

        var favoriteName = document
            .Descendants(avalonia + "TextBlock")
            .Single(element => element.Attribute("Text")?.Value == "{Binding FavoritesText}"
                && element.Ancestors(avalonia + "ToggleButton").Any());
        Assert.Equal("CharacterEllipsis", favoriteName.Attribute("TextTrimming")?.Value);
        Assert.Equal("NoWrap", favoriteName.Attribute("TextWrapping")?.Value);

        var groupsStrip = document
            .Descendants(avalonia + "Grid")
            .Single(element => element.Attribute(xaml + "Name")?.Value == "AccountGroupsStrip");
        Assert.Equal("2", groupsStrip.Attribute("Grid.Row")?.Value);
        Assert.Equal("64", groupsStrip.Attribute("MinHeight")?.Value);
        Assert.Equal("0,6,0,6", groupsStrip.Attribute("Margin")?.Value);
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

        var accountCountSummary = document
            .Descendants(avalonia + "TextBlock")
            .Single(element => element.Attribute("Text")?.Value == "{Binding SearchResultSummary}");
        var accountCountFooter = accountCountSummary.Parent;
        Assert.NotNull(accountCountFooter);
        Assert.Equal("AccountCountFooter", accountCountFooter.Attribute(xaml + "Name")?.Value);
        var accountListFooter = accountCountFooter.Parent;
        Assert.NotNull(accountListFooter);
        Assert.Equal("AccountListFooter", accountListFooter.Attribute(xaml + "Name")?.Value);
        Assert.Equal("2", accountListFooter.Attribute("Grid.Row")?.Value);
        Assert.Equal("40", accountListFooter.Attribute("Height")?.Value);
        Assert.Equal("{Binding IsAccountListVisible}", accountListFooter.Attribute("IsVisible")?.Value);
        var accountListNotification = accountListFooter
            .Elements()
            .Single(element => element.Name.LocalName == "NotificationBanner");
        Assert.Equal("{Binding NotificationText}", accountListNotification.Attribute("Text")?.Value);
        Assert.Equal("Stretch", accountListNotification.Attribute("VerticalAlignment")?.Value);
        Assert.Equal("Center", accountCountSummary.Attribute("VerticalAlignment")?.Value);
        Assert.Equal("11", accountCountSummary.Attribute("FontSize")?.Value);
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

        var nativeGroupStrip = document
            .Descendants(avalonia + "ContentControl")
            .Single(element => element.Attribute(xaml + "Name")?.Value == "NativeAccountGroupsPresenter");
        Assert.Equal(
            "{Binding IsNativeAccountGroupsVisible}",
            nativeGroupStrip.Attribute("IsVisible")?.Value);
        Assert.Equal("64", nativeGroupStrip.Attribute("MinHeight")?.Value);
        Assert.Equal("0,6,0,6", nativeGroupStrip.Attribute("Margin")?.Value);

        var accountPicker = document
            .Descendants(avalonia + "ListBox")
            .Single(element => element.Attribute(xaml + "Name")?.Value == "GroupAccountPicker");
        Assert.Equal("{Binding GroupEditorAccounts}", accountPicker.Attribute("ItemsSource")?.Value);
        Assert.Equal("220", accountPicker.Parent?.Attribute("Height")?.Value);
        Assert.Contains(
            accountPicker.Descendants(avalonia + "VirtualizingStackPanel"),
            element => element.Attribute("CacheLength")?.Value == "0.5");
        var nativeAccountPicker = document
            .Descendants(avalonia + "ContentControl")
            .Single(element => element.Attribute(xaml + "Name")?.Value
                == "NativeGroupAccountPickerPresenter");
        Assert.Same(accountPicker.Parent, nativeAccountPicker.Parent);
        Assert.Equal("False", nativeAccountPicker.Attribute("IsHitTestVisible")?.Value);
        Assert.Equal(
            "{Binding !IsDeleteGroupConfirmationVisible}",
            nativeAccountPicker.Attribute("IsVisible")?.Value);
        var accountSearch = document
            .Descendants(avalonia + "TextBox")
            .Single(element => element.Attribute(xaml + "Name")?.Value == "GroupAccountSearchBox");
        Assert.Equal(
            "{Binding GroupEditorSearchText, Mode=TwoWay}",
            accountSearch.Attribute("Text")?.Value);
        var clearGroupSearchButton = accountSearch.Parent?
            .Elements(avalonia + "Button")
            .Single();
        Assert.Equal(
            "{Binding ClearGroupEditorSearchCommand}",
            clearGroupSearchButton?.Attribute("Command")?.Value);
        Assert.Equal(
            "{Binding HasGroupEditorSearchText}",
            clearGroupSearchButton?.Attribute("IsVisible")?.Value);
        Assert.Equal("Hidden", accountPicker.Attribute("ScrollViewer.VerticalScrollBarVisibility")?.Value);
        Assert.Equal("False", accountPicker.Attribute("ScrollViewer.IsScrollChainingEnabled")?.Value);
        var accountSelection = accountPicker
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
        var deleteOverlay = document
            .Descendants(avalonia + "Grid")
            .Single(element => element.Attribute(xaml + "Name")?.Value
                == "GroupDeleteModalOverlay");
        Assert.Equal("3", deleteOverlay.Attribute("Grid.RowSpan")?.Value);
        Assert.Equal("102", deleteOverlay.Attribute("Panel.ZIndex")?.Value);
        Assert.Equal(
            "{Binding IsDeleteGroupConfirmationVisible}",
            deleteOverlay.Attribute("IsVisible")?.Value);
        var deleteDialog = deleteOverlay.Elements(avalonia + "Border").Single();
        Assert.Null(deleteDialog.Attribute("BorderBrush"));
        Assert.Null(deleteDialog.Attribute("BorderThickness"));
        Assert.Contains(
            deleteOverlay.Descendants(avalonia + "Button"),
            element => element.Attribute("Command")?.Value
                == "{Binding ConfirmDeleteGroupCommand}");

        var noSearchResults = document
            .Descendants(avalonia + "TextBlock")
            .Single(element => element.Attribute("IsVisible")?.Value
                == "{Binding HasNoSearchResults}");
        Assert.Equal("Center", noSearchResults.Attribute("HorizontalAlignment")?.Value);
        Assert.Equal("Center", noSearchResults.Attribute("TextAlignment")?.Value);
        Assert.Equal("0,24,0,0", noSearchResults.Attribute("Margin")?.Value);
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
