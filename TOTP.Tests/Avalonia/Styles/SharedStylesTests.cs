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
    public void ProgressBarsUseThemeAccentAcrossPlatforms()
    {
        var sourcePath = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "Avalonia",
            "SharedStyles.axaml");
        var document = XDocument.Load(sourcePath);
        XNamespace avalonia = "https://github.com/avaloniaui";

        var progressStyle = document
            .Descendants(avalonia + "Style")
            .Single(element => element.Attribute("Selector")?.Value == "ProgressBar");
        var foregroundSetter = progressStyle
            .Elements(avalonia + "Setter")
            .Single(element => element.Attribute("Property")?.Value == "Foreground");

        Assert.Equal(
            "{DynamicResource BrushAccent}",
            foregroundSetter.Attribute("Value")?.Value);
    }

    [Fact]
    public void EveryBuiltInThemeProvidesTheSemanticPresentationPalette()
    {
        var document = XDocument.Load(Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "Avalonia",
            "SharedStyles.axaml"));
        XNamespace avalonia = "https://github.com/avaloniaui";
        var themeDictionaries = document
            .Descendants(avalonia + "ResourceDictionary.ThemeDictionaries")
            .Single()
            .Elements(avalonia + "ResourceDictionary")
            .ToArray();
        var requiredBrushes = new[]
        {
            "BrushWindowBackground",
            "BrushSurface",
            "BrushTextPrimary",
            "BrushTextSecondary",
            "BrushToolbarSearchIcon",
            "BrushFaqHeaderBackground",
            "BrushFaqHeaderForeground",
            "BrushFaqHeaderIcon",
            "BrushFaqContentBackground",
            "BrushAccent",
            "BrushAccentHover",
            "BrushAccentPressed",
            "BrushOnAccent",
            "BrushDanger",
            "BrushDangerPressed",
            "BrushOnDanger",
            "BrushSuccess",
            "BrushQrBackground",
            "BrushQrForeground",
            "BrushBrandTileForeground",
            "BrushOverlayLight",
            "BrushOverlayStrong"
        };

        Assert.Equal(4, themeDictionaries.Length);
        Assert.All(themeDictionaries, dictionary =>
        {
            var brushKeys = dictionary
                .Elements(avalonia + "SolidColorBrush")
                .Select(GetResourceKey)
                .ToHashSet(StringComparer.Ordinal);
            Assert.All(requiredBrushes, key => Assert.Contains(key, brushKeys));
        });
    }

    [Fact]
    public void AppViewsUseSemanticThemeResourcesInsteadOfLiteralColors()
    {
        var fixtureDirectory = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "Avalonia");
        var colorProperties = new HashSet<string>(StringComparer.Ordinal)
        {
            "Background",
            "BorderBrush",
            "Fill",
            "Foreground",
            "Stroke"
        };
        var namedColors = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Black",
            "Blue",
            "Green",
            "Red",
            "White",
            "Yellow"
        };

        foreach (var fixtureName in new[]
                 {
                     "DesktopMainWindow.axaml",
                     "DesktopSettingsWindow.axaml",
                     "MobileMainView.axaml"
                 })
        {
            var document = XDocument.Load(Path.Combine(fixtureDirectory, fixtureName));
            var literalColors = document
                .Descendants()
                .Attributes()
                .Where(attribute => colorProperties.Contains(attribute.Name.LocalName))
                .Where(attribute => attribute.Value.StartsWith('#') || namedColors.Contains(attribute.Value))
                .Select(attribute => $"{attribute.Parent?.Name.LocalName}.{attribute.Name.LocalName}={attribute.Value}")
                .ToArray();

            Assert.Empty(literalColors);
        }
    }

    [Fact]
    public void DesktopSearchIconAndFaqUseThemeableInteractivePresentation()
    {
        var fixtureDirectory = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "Avalonia");
        XNamespace avalonia = "https://github.com/avaloniaui";
        var mainWindow = XDocument.Load(Path.Combine(fixtureDirectory, "DesktopMainWindow.axaml"));
        var searchIcon = mainWindow
            .Descendants()
            .Single(element => element.Name.LocalName == "SymbolIcon"
                && element.Attribute("Kind")?.Value == "Search");
        Assert.Equal(
            "{DynamicResource BrushToolbarSearchIcon}",
            searchIcon.Attribute("Foreground")?.Value);

        var settings = XDocument.Load(Path.Combine(fixtureDirectory, "DesktopSettingsWindow.axaml"));
        var faqTab = settings
            .Descendants(avalonia + "TabItem")
            .Single(element => element.Attribute("Header")?.Value == "{DynamicResource Faq}");
        var faqSections = faqTab.Descendants(avalonia + "Expander").ToArray();
        Assert.Equal(2, faqSections.Length);
        Assert.All(
            faqSections,
            section => Assert.Equal(
                "FaqSectionExpanded",
                section.Attribute("Expanded")?.Value));
        Assert.Equal(
            ["{DynamicResource FaqImportIconPacksQuestion}", "{DynamicResource FaqImportFormatsQuestion}"],
            faqSections.Select(section => section
                .Descendants(avalonia + "TextBlock")
                .First()
                .Attribute("Text")?.Value));
        Assert.Equal(
            ["Folder", "QrCode"],
            faqSections.Select(section => section
                .Descendants()
                .Single(element => element.Name.LocalName == "SymbolIcon")
                .Attribute("Kind")?.Value));
        Assert.Equal(
            [
                "https://github.com/simple-icons/simple-icons",
                "https://github.com/beemdevelopment/Aegis/blob/master/docs/iconpacks.md"
            ],
            faqSections[0]
                .Descendants(avalonia + "HyperlinkButton")
                .Select(link => link.Attribute("NavigateUri")?.Value));
        Assert.Contains(
            faqSections[1].Descendants(avalonia + "TextBlock"),
            element => element.Attribute("Text")?.Value
                == "{DynamicResource FaqImportFormatsAegisDescription}");
        Assert.All(
            faqSections,
            section =>
            {
                var card = Assert.IsType<XElement>(section.Parent);
                Assert.Equal(avalonia + "Border", card.Name);
                Assert.Equal("Stretch", card.Attribute("HorizontalAlignment")?.Value);
                Assert.Equal("{DynamicResource BrushFaqHeaderBackground}", card.Attribute("Background")?.Value);
                Assert.Equal("{DynamicResource ElevationCard}", card.Attribute("BoxShadow")?.Value);
                Assert.Null(card.Attribute("BorderBrush"));
                Assert.Equal("Stretch", section.Attribute("HorizontalAlignment")?.Value);
                var content = section.Elements(avalonia + "Border").Single();
                Assert.Equal("{DynamicResource BrushFaqContentBackground}", content.Attribute("Background")?.Value);
                Assert.Null(content.Attribute("BorderBrush"));
            });
        Assert.DoesNotContain(
            faqTab.Descendants(avalonia + "Border"),
            border => border.Attribute("Classes")?.Value == "panel");

        var styles = XDocument.Load(Path.Combine(fixtureDirectory, "SharedStyles.axaml"));
        var light = styles
            .Descendants(avalonia + "ResourceDictionary")
            .Single(element => GetResourceKeyOrDefault(element) == "Light");
        var dark = styles
            .Descendants(avalonia + "ResourceDictionary")
            .Single(element => GetResourceKeyOrDefault(element) == "Dark");
        Assert.Equal("#FFFFFF", BrushColor(light, avalonia, "BrushFaqHeaderBackground"));
        Assert.Equal("#4F6EF7", BrushColor(light, avalonia, "BrushFaqHeaderIcon"));
        Assert.Equal("#F8FAFC", BrushColor(light, avalonia, "BrushFaqContentBackground"));
        Assert.Equal("#0E192D", BrushColor(dark, avalonia, "BrushFaqHeaderBackground"));
        Assert.Equal("#4EA1FF", BrushColor(dark, avalonia, "BrushFaqHeaderIcon"));
        Assert.Equal("#12203A", BrushColor(dark, avalonia, "BrushFaqContentBackground"));
    }

    [Fact]
    public void DesktopMinimumLoggingLevelLivesOnlyInMiscTab()
    {
        var document = XDocument.Load(Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "Avalonia",
            "DesktopSettingsWindow.axaml"));
        XNamespace avalonia = "https://github.com/avaloniaui";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

        var appearanceTab = document
            .Descendants(avalonia + "TabItem")
            .Single(element => element.Attribute("Header")?.Value == "{DynamicResource Appearance}");
        var miscTab = document
            .Descendants(avalonia + "TabItem")
            .Single(element => element.Attribute("Header")?.Value == "{DynamicResource Miscellaneous}");

        Assert.DoesNotContain(
            appearanceTab.Descendants(avalonia + "ComboBox"),
            element => element.Attribute(xaml + "Name")?.Value == "MinimumLoggingLevelPicker");
        Assert.Single(
            miscTab.Descendants(avalonia + "ComboBox"),
            element => element.Attribute(xaml + "Name")?.Value == "MinimumLoggingLevelPicker");
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
    public void CopyConfirmationAndSelectedFavorite_UseDarkOnlyEmphasis()
    {
        var fixtureDirectory = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "Avalonia");
        XNamespace avalonia = "https://github.com/avaloniaui";
        var styles = XDocument.Load(Path.Combine(fixtureDirectory, "SharedStyles.axaml"));
        var light = styles
            .Descendants(avalonia + "ResourceDictionary")
            .Single(element => GetResourceKeyOrDefault(element) == "Light");
        var dark = styles
            .Descendants(avalonia + "ResourceDictionary")
            .Single(element => GetResourceKeyOrDefault(element) == "Dark");

        Assert.Equal("#137333", BrushColor(light, avalonia, "BrushCopyConfirmation"));
        Assert.Equal("#282828", BrushColor(light, avalonia, "BrushQrInstruction"));
        Assert.Equal("#168AE0", BrushColor(light, avalonia, "BrushFavoriteStarFill"));
        Assert.Equal("Transparent", BrushColor(light, avalonia, "BrushFavoriteStarOutline"));
        Assert.Equal("#B7FF4A", BrushColor(dark, avalonia, "BrushCopyConfirmation"));
        Assert.Equal("#F4CA16", BrushColor(dark, avalonia, "BrushQrInstruction"));
        Assert.Equal("Transparent", BrushColor(dark, avalonia, "BrushFavoriteStarFill"));
        Assert.Equal("#FFD166", BrushColor(dark, avalonia, "BrushFavoriteStarOutline"));
        Assert.Equal("Transparent", BrushColor(dark, avalonia, "BrushFavoriteButtonInteractiveBackground"));
        Assert.Equal("Transparent", BrushColor(dark, avalonia, "BrushFavoriteButtonPressedBackground"));
        Assert.Equal("Transparent", BrushColor(dark, avalonia, "BrushFavoriteButtonInteractiveBorder"));
        Assert.Equal("Transparent", BrushColor(dark, avalonia, "BrushFavoriteButtonFocusBorder"));

        var mainWindow = XDocument.Load(Path.Combine(fixtureDirectory, "DesktopMainWindow.axaml"));
        var confirmation = mainWindow
            .Descendants(avalonia + "TextBlock")
            .Single(element => element.Attribute("Text")?.Value == "{Binding CopyConfirmation}");
        Assert.Equal(
            "{DynamicResource BrushCopyConfirmation}",
            confirmation.Attribute("Foreground")?.Value);

        var selectedFavoriteButton = mainWindow
            .Descendants(avalonia + "Button")
            .Single(element => element.Attribute("Classes")?.Value == "icon account-favorite-selected");
        var selectedFavoritePath = selectedFavoriteButton
            .Descendants(avalonia + "Path")
            .Single();
        Assert.Equal(
            "{DynamicResource BrushFavoriteStarFill}",
            selectedFavoritePath.Attribute("Fill")?.Value);
        Assert.Equal(
            "{DynamicResource BrushFavoriteStarOutline}",
            selectedFavoritePath.Attribute("Stroke")?.Value);
    }

    [Fact]
    public void FingerprintUnlock_UsesDistinctThemeAwareSharedArtwork()
    {
        var styles = XDocument.Load(Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "Avalonia",
            "SharedStyles.axaml"));
        XNamespace avalonia = "https://github.com/avaloniaui";
        var light = styles
            .Descendants(avalonia + "ResourceDictionary")
            .Single(element => GetResourceKeyOrDefault(element) == "Light");
        var dark = styles
            .Descendants(avalonia + "ResourceDictionary")
            .Single(element => GetResourceKeyOrDefault(element) == "Dark");

        Assert.Equal("#087FC1", BrushColor(light, avalonia, "BrushBiometricIconPrimary"));
        Assert.Equal("#6859E8", BrushColor(light, avalonia, "BrushBiometricIconSecondary"));
        Assert.Equal("#07162F", BrushColor(dark, avalonia, "BrushBiometricButtonBackground"));
        Assert.Equal("#48DAFF", BrushColor(dark, avalonia, "BrushBiometricIconPrimary"));
        Assert.Equal("#8587FF", BrushColor(dark, avalonia, "BrushBiometricIconSecondary"));
        Assert.Equal("#2DD9FF", BrushColor(dark, avalonia, "BrushBiometricButtonBorder"));

        var fingerprintStyle = styles
            .Descendants(avalonia + "Style")
            .Single(element => element.Attribute("Selector")?.Value == "controls|FingerprintIcon");
        Assert.Empty(fingerprintStyle.Descendants(avalonia + "Path"));
        Assert.True(fingerprintStyle.Descendants(avalonia + "Ellipse").Count() >= 3);
        var fingerprintImage = fingerprintStyle
            .Descendants(avalonia + "Image")
            .Single(image => image.Attribute("Source")?.Value.EndsWith(
                "/fingerprint.png",
                StringComparison.Ordinal) == true);
        Assert.Equal("36", fingerprintImage.Attribute("Width")?.Value);
        Assert.Equal("42", fingerprintImage.Attribute("Height")?.Value);
        Assert.Equal("Fill", fingerprintImage.Attribute("Stretch")?.Value);
        Assert.Contains(
            fingerprintStyle.Descendants(avalonia + "Image"),
            image => image.Attribute("Source")?.Value.EndsWith(
                "/otp-harbor-mark.png",
                StringComparison.Ordinal) == true);

        var buttonStyle = styles
            .Descendants(avalonia + "Style")
            .Single(element => element.Attribute("Selector")?.Value == "Button.biometric-unlock");
        Assert.Contains(
            buttonStyle.Elements(avalonia + "Setter"),
            setter => setter.Attribute("Property")?.Value == "CornerRadius"
                && setter.Attribute("Value")?.Value == "48");
        Assert.Contains(
            buttonStyle.Elements(avalonia + "Setter"),
            setter => setter.Attribute("Property")?.Value == "BorderThickness"
                && setter.Attribute("Value")?.Value == "2");

        var hoverStyle = styles
            .Descendants(avalonia + "Style")
            .Single(element => element.Attribute("Selector")?.Value
                == "Button.biometric-unlock:pointerover");
        Assert.Contains(
            hoverStyle.Elements(avalonia + "Setter"),
            setter => setter.Attribute("Property")?.Value == "Background"
                && setter.Attribute("Value")?.Value
                == "{DynamicResource BrushBiometricButtonBackground}");
        Assert.Contains(
            hoverStyle.Elements(avalonia + "Setter"),
            setter => setter.Attribute("Property")?.Value == "BorderBrush"
                && setter.Attribute("Value")?.Value
                == "{DynamicResource BrushBiometricButtonBorder}");

        var rippleStyle = styles
            .Descendants(avalonia + "Style")
            .Single(element => element.Attribute("Selector")?.Value.Contains(
                "PART_FingerprintRipple",
                StringComparison.Ordinal) == true);
        Assert.Contains(":pressed", rippleStyle.Attribute("Selector")?.Value);
        var rippleAnimation = rippleStyle.Descendants(avalonia + "Animation").Single();
        Assert.Equal("0:0:0.5", rippleAnimation.Attribute("Duration")?.Value);
        Assert.Equal(2, rippleAnimation.Descendants(avalonia + "KeyFrame").Count());
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
        Assert.Equal(
            "Hidden",
            accountList.Attribute("ScrollViewer.VerticalScrollBarVisibility")?.Value);

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
    public void AccountLists_HideVerticalScrollBarsWithoutDisablingScrolling()
    {
        var fixtureDirectory = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "Avalonia");

        foreach (var (fixtureName, listClass) in new[]
                 {
                     ("DesktopMainWindow.axaml", "desktop-accounts"),
                     ("MobileMainView.axaml", "mobile-accounts"),
                 })
        {
            var document = XDocument.Load(Path.Combine(fixtureDirectory, fixtureName));
            var accountList = document
                .Descendants()
                .Single(element =>
                    element.Name.LocalName.EndsWith("ListBox", StringComparison.Ordinal)
                    && element.Attribute("Classes")?.Value.Contains(
                        listClass,
                        StringComparison.Ordinal) == true);

            Assert.Equal(
                "Hidden",
                accountList.Attribute("ScrollViewer.VerticalScrollBarVisibility")?.Value);
        }
    }

    [Fact]
    public void RecentlyAddedAccount_AnimatesOnlyAReservedGoldenOutline()
    {
        var document = XDocument.Load(Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "Avalonia",
            "SharedStyles.axaml"));
        XNamespace avalonia = "https://github.com/avaloniaui";

        var rowStyle = document
            .Descendants(avalonia + "Style")
            .Single(element => element.Attribute("Selector")?.Value
                == "Border.account-row-container");
        Assert.Contains(
            rowStyle.Elements(avalonia + "Setter"),
            setter => setter.Attribute("Property")?.Value == "BorderThickness"
                && setter.Attribute("Value")?.Value == "2");

        var highlightStyle = document
            .Descendants(avalonia + "Style")
            .Single(element => element.Attribute("Selector")?.Value
                == "Border.account-row-container.recently-added");
        var animation = highlightStyle.Descendants(avalonia + "Animation").Single();
        Assert.Equal("0:0:1", animation.Attribute("Duration")?.Value);
        Assert.Equal("None", animation.Attribute("FillMode")?.Value);
        var setters = animation.Descendants(avalonia + "Setter").ToArray();
        Assert.DoesNotContain(
            setters,
            setter => setter.Attribute("Property")?.Value == "Background");
        Assert.All(
            setters,
            setter => Assert.Equal("BorderBrush", setter.Attribute("Property")?.Value));
        Assert.Contains(
            setters,
            setter => setter.Attribute("Value")?.Value
                == "{DynamicResource BrushRecentAccountHighlight}");
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
            Assert.Equal(
                "RefocusAccountPeriodAfterClear",
                clearButton.Attribute("Click")?.Value);
        }

        foreach (var fixtureName in new[] { "DesktopMainWindow.axaml", "MobileMainView.axaml" })
        {
            var document = XDocument.Load(Path.Combine(fixtureDirectory, fixtureName));
            var searchEditor = document
                .Descendants(avalonia + "TextBox")
                .Single(element => element.Attributes().Any(attribute =>
                    attribute.Name.LocalName == "Name"
                    && attribute.Value == "AccountSearchBox"));
            var searchContainer = searchEditor.Parent
                ?? throw new InvalidOperationException("The search editor container is missing.");
            var clearSearchButton = searchContainer
                .Elements(avalonia + "Button")
                .Single(element => element.Attribute("Command")?.Value.Contains(
                    "ClearSearchCommand",
                    StringComparison.Ordinal) == true);
            Assert.Equal(
                "RefocusAccountSearchAfterClear",
                clearSearchButton.Attribute("Click")?.Value);
        }

        var mobileApp = XDocument.Load(Path.Combine(fixtureDirectory, "MobileApp.axaml"));
        var mobilePeriodStyle = mobileApp
            .Descendants(avalonia + "Style")
            .Single(element => element.Attribute("Selector")?.Value == "NumericUpDown.mobile-period");
        Assert.Contains(
            mobilePeriodStyle.Elements(avalonia + "Setter"),
            setter => setter.Attribute("Property")?.Value == "VerticalContentAlignment"
                && setter.Attribute("Value")?.Value == "Center");

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
            setter => setter.Attribute("Property")?.Value == "HorizontalContentAlignment"
                && setter.Attribute("Value")?.Value == "Center");
        Assert.Contains(
            clearStyle.Elements(avalonia + "Setter"),
            setter => setter.Attribute("Property")?.Value == "VerticalContentAlignment"
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
