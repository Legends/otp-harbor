using System.Xml.Linq;

namespace TOTP.Tests.Avalonia.Mobile;

public sealed class MobileAccountInteractionTests
{
    [Fact]
    public void AppHeader_UsesOtpHarborLogoInsteadOfLockSymbol()
    {
        var document = XDocument.Load(Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "Avalonia",
            "MobileMainView.axaml"));
        XNamespace avalonia = "https://github.com/avaloniaui";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

        var header = document
            .Descendants(avalonia + "Border")
            .Single(element => element.Attribute(xaml + "Name")?.Value == "MobileAppHeader");
        var logo = header
            .Descendants(avalonia + "Image")
            .Single(element => element.Attribute(xaml + "Name")?.Value == "MobileAppLogo");

        Assert.Equal(
            "avares://TOTP.UI.Avalonia.Shared/Assets/Biometric/otp-harbor-mark.png",
            logo.Attribute("Source")?.Value);
        Assert.DoesNotContain(
            header.Elements(),
            element => element.Name.LocalName == "SymbolIcon"
                && element.Attribute("Grid.Column")?.Value == "0");
    }

    [Fact]
    public void AccountEditor_AdvancedOptionsOwnFavoriteAndBrandIconControls()
    {
        var document = XDocument.Load(Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "Avalonia",
            "MobileMainView.axaml"));
        XNamespace avalonia = "https://github.com/avaloniaui";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

        var advanced = document
            .Descendants(avalonia + "Expander")
            .Single(element => element.Attribute(xaml + "Name")?.Value
                == "AccountAdvancedOptions");

        Assert.Contains(
            advanced.Descendants(avalonia + "CheckBox"),
            control => control.Attribute("IsChecked")?.Value
                == "{Binding EditorIsFavorite, Mode=TwoWay}");
        Assert.Contains(
            advanced.Descendants().Where(element => element.Name.LocalName == "SearchableComboBox"),
            control => control.Attribute(xaml + "Name")?.Value
                == "AccountBrandIconComboBox");
        Assert.Contains(
            advanced.Descendants(avalonia + "Button"),
            control => control.Attribute(xaml + "Name")?.Value
                == "CustomSvgIconButton");
        Assert.DoesNotContain(
            advanced.Descendants(avalonia + "TextBlock"),
            control => control.Attribute("Text")?.Value == "{Binding OrText}");
        Assert.Contains(
            advanced.Descendants(avalonia + "TextBlock"),
            control => control.Attribute("Text")?.Value
                == "{Binding SelectedEditorCustomIconFileName}");
        Assert.Equal(
            2,
            advanced.Descendants(avalonia + "Border")
                .Count(border => border.Attribute("Height")?.Value == "1"
                    && border.Attribute("Background")?.Value
                        == "{DynamicResource BrushBorder}"));

        var exitModal = document
            .Descendants(avalonia + "Grid")
            .Single(element => element.Attribute(xaml + "Name")?.Value
                == "AccountEditorExitModalOverlay");
        Assert.Equal(
            "{Binding IsAccountEditorExitConfirmationVisible}",
            exitModal.Attribute("IsVisible")?.Value);
        Assert.Contains(
            exitModal.Descendants(avalonia + "Button"),
            button => button.Attribute("Command")?.Value
                == "{Binding SaveAccountAndNavigateBackCommand}");
        Assert.Contains(
            exitModal.Descendants(avalonia + "Button"),
            button => button.Attribute("Command")?.Value
                == "{Binding DiscardAccountChangesCommand}");
    }

    [Fact]
    public void Settings_UsesCategoryPagesWithLanguagePickerAndMiscLogging()
    {
        var document = XDocument.Load(Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "Avalonia",
            "MobileMainView.axaml"));
        XNamespace avalonia = "https://github.com/avaloniaui";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

        var categories = document
            .Descendants(avalonia + "StackPanel")
            .Single(element => element.Attribute("IsVisible")?.Value
                == "{Binding IsSettingsCategoryListVisible}");
        var categoryButtons = categories.Elements(avalonia + "Button").ToArray();
        Assert.Equal(7, categoryButtons.Length);
        Assert.Equal(
            "{Binding ShowFaqSettingsCommand}",
            categoryButtons[^1].Attribute("Command")?.Value);
        Assert.Equal(
            "{Binding ShowMiscSettingsCommand}",
            categoryButtons[^2].Attribute("Command")?.Value);
        Assert.Equal(
            "{Binding ShowImportExportSettingsCommand}",
            categoryButtons[^3].Attribute("Command")?.Value);

        var appearance = document
            .Descendants(avalonia + "Border")
            .Single(element => element.Attribute("IsVisible")?.Value
                == "{Binding IsAppearanceSettingsVisible}");
        var languagePicker = Assert.Single(appearance.Descendants(avalonia + "ComboBox"));
        Assert.Equal("{Binding Languages}", languagePicker.Attribute("ItemsSource")?.Value);
        Assert.Equal(
            "{Binding SelectedLanguage, Mode=TwoWay}",
            languagePicker.Attribute("SelectedItem")?.Value);
        Assert.Equal("88", languagePicker.Attribute("Width")?.Value);
        XNamespace controls = "using:TOTP.Avalonia.Shared.Controls";
        Assert.Equal(2, languagePicker.Descendants(controls + "LanguageFlagImage").Count());
        Assert.All(
            languagePicker.Descendants(controls + "LanguageFlagImage"),
            image =>
            {
                Assert.Equal("{Binding CultureName}", image.Attribute("CultureName")?.Value);
                Assert.Equal("40", image.Attribute("Width")?.Value);
                Assert.Equal("30", image.Attribute("Height")?.Value);
            });
        Assert.Contains(
            languagePicker.Descendants(avalonia + "ComboBox.SelectionBoxItemTemplate"),
            _ => true);

        var security = document
            .Descendants(avalonia + "Border")
            .Single(element => element.Attribute("IsVisible")?.Value
                == "{Binding IsSecuritySettingsVisible}");
        Assert.Contains(
            security.Descendants(avalonia + "Button"),
            button => button.Attribute("Command")?.Value
                == "{Binding ChangeMasterPasswordCommand}");

        var misc = document
            .Descendants(avalonia + "Border")
            .Single(element => element.Attribute("IsVisible")?.Value
                == "{Binding IsMiscSettingsVisible}");
        var loggingPicker = Assert.Single(misc.Descendants(avalonia + "ComboBox"));
        Assert.Equal("{Binding LogLevels}", loggingPicker.Attribute("ItemsSource")?.Value);
        Assert.Equal(
            "{Binding MinimumLogLevel, Mode=TwoWay}",
            loggingPicker.Attribute("SelectedItem")?.Value);

        var imports = document
            .Descendants(avalonia + "Border")
            .Single(element => element.Attribute("IsVisible")?.Value
                == "{Binding IsImportExportSettingsVisible}");
        Assert.DoesNotContain(
            imports.Descendants(avalonia + "TextBlock"),
            text => text.Attribute("Text")?.Value == "{Binding ImportSectionText}");
        var googleImportPanel = imports
            .Descendants(avalonia + "Border")
            .Single(element => element.Attribute(xaml + "Name")?.Value
                == "GoogleAuthenticatorImportPanel");
        var otherFormatsPanel = imports
            .Descendants(avalonia + "Border")
            .Single(element => element.Attribute(xaml + "Name")?.Value
                == "OtherFormatsImportPanel");
        Assert.All(
            new[] { googleImportPanel, otherFormatsPanel },
            panel => Assert.Equal("1", panel.Attribute("BorderThickness")?.Value));
        var importButtons = new[]
        {
            googleImportPanel.Descendants(avalonia + "Button").Single(button =>
                button.Attribute(xaml + "Name")?.Value == "GoogleAuthenticatorImportButton"),
            otherFormatsPanel.Descendants(avalonia + "Button").Single(button =>
                button.Attribute(xaml + "Name")?.Value == "OtherFormatsImportButton")
        };
        Assert.All(importButtons, button =>
        {
            Assert.Equal("wide secondary", button.Attribute("Classes")?.Value);
            Assert.Equal("{Binding ImportSectionText}", button.Attribute("Content")?.Value);
        });
        Assert.Equal(
            [
                "{Binding ImportFormatAegisText}",
                "{Binding ImportFormatTwoFasText}",
                "{Binding ImportFormatOtpAuthText}"
            ],
            otherFormatsPanel
                .Descendants(avalonia + "TextBlock")
                .Select(text => text.Attribute("Text")?.Value)
                .Where(value => value?.StartsWith("{Binding ImportFormat", StringComparison.Ordinal) == true));
        var faqLink = otherFormatsPanel.Descendants(avalonia + "Button").Single(button =>
            button.Attribute("Command")?.Value == "{Binding ShowImportFormatsFaqCommand}");
        Assert.Equal("link", faqLink.Attribute("Classes")?.Value);

        var backup = document
            .Descendants(avalonia + "Border")
            .Single(element => element.Attribute("IsVisible")?.Value
                == "{Binding IsBackupSettingsVisible}");
        var backupExportPanel = backup.Descendants(avalonia + "Border").Single(element =>
            element.Attribute(xaml + "Name")?.Value == "BackupExportPanel");
        var backupRestorePanel = backup.Descendants(avalonia + "Border").Single(element =>
            element.Attribute(xaml + "Name")?.Value == "BackupRestorePanel");
        Assert.Equal("{Binding BackupSectionText}", backupExportPanel.Descendants(avalonia + "TextBlock").First().Attribute("Text")?.Value);
        Assert.Equal("{Binding RestoreSectionText}", backupRestorePanel.Descendants(avalonia + "TextBlock").First().Attribute("Text")?.Value);

        var settingsRoot = document
            .Descendants(avalonia + "StackPanel")
            .Single(element => element.Attribute("Classes")?.Value == "settings-root");
        Assert.Equal("{Binding IsSettingsVisible}", settingsRoot.Attribute("IsVisible")?.Value);
        Assert.Contains(
            document.Descendants(avalonia + "Style"),
            style => style.Attribute("Selector")?.Value == "StackPanel.settings-root Button.wide");
        var categoryStyle = document
            .Descendants(avalonia + "Style")
            .Single(style => style.Attribute("Selector")?.Value
                == "StackPanel.settings-root Button.wide.settings-category");
        Assert.Contains(
            categoryStyle.Elements(avalonia + "Setter"),
            setter => setter.Attribute("Property")?.Value == "Background"
                && setter.Attribute("Value")?.Value == "{DynamicResource BrushSurface}");

        var faq = document
            .Descendants(avalonia + "Border")
            .Single(element => element.Attribute("IsVisible")?.Value
                == "{Binding IsFaqSettingsVisible}");
        var faqSections = faq
            .Descendants(avalonia + "Expander")
            .Where(section => section.Attribute("Classes")?.Value == "mobile-faq-section")
            .ToArray();
        Assert.Equal(2, faqSections.Length);
        Assert.All(
            faqSections,
            section => Assert.Equal(
                "FaqSectionExpanded",
                section.Attribute("Expanded")?.Value));
        Assert.Equal(
            ["{Binding FaqImportIconPacksQuestionText}", "{Binding FaqImportFormatsQuestionText}"],
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
                == "{Binding FaqImportFormatsAegisDescriptionText}");
        var importFormatSections = faqSections[1]
            .Descendants(avalonia + "Expander")
            .Where(section => section.Attribute("Classes")?.Value == "mobile-faq-subsection")
            .ToArray();
        Assert.Equal(3, importFormatSections.Length);
        Assert.All(
            importFormatSections,
            section => Assert.Equal(
                "FaqImportFormatSectionExpanded",
                section.Attribute("Expanded")?.Value));
        Assert.Equal(
            [
                "{Binding FaqImportFormatsAegisTitleText}",
                "{Binding FaqImportFormatsTwoFasTitleText}",
                "{Binding FaqImportFormatsOtpAuthTitleText}"
            ],
            importFormatSections.Select(section => section
                .Descendants(avalonia + "TextBlock")
                .First()
                .Attribute("Text")?.Value));
        foreach (var state in new[] { "pointerover", "pressed" })
        {
            var stateStyle = document
                .Descendants(avalonia + "Style")
                .Single(style => style.Attribute("Selector")?.Value
                    == $"Expander.mobile-faq-subsection /template/ ToggleButton#ExpanderHeader:{state}");
            Assert.Contains(
                stateStyle.Elements(avalonia + "Setter"),
                setter => setter.Attribute("Property")?.Value == "Background"
                    && setter.Attribute("Value")?.Value == "Transparent");
        }
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

        var backToTop = document
            .Descendants(avalonia + "Button")
            .Single(element => element.Attribute(xaml + "Name")?.Value
                == "SettingsBackToTopButton");
        Assert.Equal("ScrollMainViewToTop", backToTop.Attribute("Click")?.Value);
        Assert.Equal("settings-back-to-top", backToTop.Attribute("Classes")?.Value);
        Assert.Equal("12", backToTop.Attribute("Padding")?.Value);
        var backToTopIcon = Assert.Single(backToTop.Descendants(), element =>
            element.Name.LocalName == "SymbolIcon");
        Assert.Equal("18", backToTopIcon.Attribute("IconSize")?.Value);
        Assert.Equal("0.65", backToTopIcon.Attribute("Opacity")?.Value);
    }

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
            .Single(element => element.Name.LocalName == "Grid");
        Assert.Equal("Auto,*,Auto", favoriteContent.Attribute("ColumnDefinitions")?.Value);
        Assert.Equal("7", favoriteContent.Attribute("ColumnSpacing")?.Value);
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
