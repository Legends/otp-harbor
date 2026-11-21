using System.Xml.Linq;
using TOTP.Avalonia.Desktop.Localization;
using System.Globalization;
using TOTP.Avalonia.Shared.Import;
using TOTP.Avalonia.Mobile.Localization;

namespace TOTP.Tests.Avalonia.Presentation;

public sealed class SettingsRecoveryFaqPresentationTests
{
    [Theory]
    [InlineData("en", "How do I create a backup?", "export password", "master password")]
    [InlineData("de", "Wie erstelle ich ein Backup?", "Exportpasswort", "Master-Passwort")]
    [InlineData("fr", "Comment créer une sauvegarde ?", "mot de passe d’exportation", "mot de passe principal")]
    [InlineData("es", "¿Cómo creo una copia de seguridad?", "contraseña de exportación", "contraseña maestra")]
    public void BackupCreationHelp_IsSharedAndExplainsBothPasswordBoundaries(string locale, string question, string exportPassword, string masterPassword)
    {
        var culture = CultureInfo.GetCultureInfo(locale);
        var desktop = new AvaloniaStringCatalog();
        var mobile = new MobileStringCatalog(culture);
        var answer = desktop.Get(AvaloniaStringKeys.FaqCreateBackupAnswer, culture);
        Assert.Equal(question, desktop.Get(AvaloniaStringKeys.FaqCreateBackupQuestion, culture));
        Assert.Equal(question, mobile.Get(MobileStringKeys.FaqCreateBackupQuestion));
        Assert.Equal(answer, mobile.Get(MobileStringKeys.FaqCreateBackupAnswer));
        Assert.Contains(exportPassword, answer);
        Assert.Contains(masterPassword, answer);
        Assert.Contains(desktop.Get(AvaloniaStringKeys.AutomaticBackups, culture), answer);
        Assert.Contains(desktop.Get(MigrationStringKeys.DataAndBackup, culture), answer);
        Assert.Equal(3, answer.ReplaceLineEndings("\n").Split("\n\n").Length);
    }

    [Fact]
    public void DesktopAndMobileFaqs_RetainSharedTopicsAndBackupGuidanceInTheirAccordions()
    {
        var fixtures = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Avalonia");
        XNamespace ui = "https://github.com/avaloniaui";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var desktop = XDocument.Load(Path.Combine(fixtures, "DesktopSettingsWindow.axaml"));
        var mobile = XDocument.Load(Path.Combine(fixtures, "MobileMainView.axaml"));
        var desktopSections = desktop.Descendants(ui + "Expander").Where(control =>
            (string?)control.Attribute("Classes") == "faq-section").ToArray();
        var mobileSections = mobile.Descendants(ui + "Expander").Where(control =>
            (string?)control.Attribute("Classes") == "mobile-faq-section").ToArray();
        Assert.Equal(7, desktopSections.Length);
        Assert.Equal(5, mobileSections.Length);
        Assert.All(mobileSections, section => Assert.Contains(desktopSections,
            desktopSection => (string?)desktopSection.Attribute(xaml + "Name") == (string?)section.Attribute(xaml + "Name")));
        foreach (var section in mobileSections)
            Assert.Equal("FaqSectionExpanded", (string?)section.Attribute("Expanded"));
        var source = File.ReadAllText(Path.Combine(fixtures, "MobileMainView.axaml.cs"));
        var accordion = source[source.IndexOf("private void FaqSectionExpanded", StringComparison.Ordinal)..
            source.IndexOf("private void FaqImportFormatSectionExpanded", StringComparison.Ordinal)];
        foreach (var section in mobileSections)
            Assert.Contains((string)section.Attribute(xaml + "Name")!, accordion, StringComparison.Ordinal);
        var desktopBackup = Assert.Single(desktopSections, control => (string?)control.Attribute(xaml + "Name") == "FaqCreateBackupExpander");
        Assert.Contains(desktopBackup.Descendants(ui + "TextBlock"), text => (string?)text.Attribute("Text") == "{DynamicResource FaqCreateBackupAnswer}");
        var mobileBackup = Assert.Single(mobileSections, control => (string?)control.Attribute(xaml + "Name") == "FaqCreateBackupExpander");
        Assert.Contains(mobileBackup.Descendants(ui + "TextBlock"), text => (string?)text.Attribute("Text") == "{Binding FaqCreateBackupAnswerText}"
            && (string?)text.Attribute("TextWrapping") == "Wrap");
        var about = Assert.Single(desktop.Descendants(), control => control.Name.LocalName == "SettingsAboutIdentityPanel");
        Assert.Equal("{Binding SettingsPage.About}", (string?)about.Attribute("DataContext"));
    }

    [Fact]
    public void IconPackHelp_BothPresentationsKeepLegalNoticesAndWrappingProviderLinks()
    {
        var fixtures = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Avalonia");
        XNamespace ui = "https://github.com/avaloniaui";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        foreach (var mobile in new[] { false, true })
        {
            var document = XDocument.Load(Path.Combine(fixtures, mobile ? "MobileMainView.axaml" : "DesktopSettingsWindow.axaml"));
            var section = document.Descendants(ui + "Expander").Single(value => (string?)value.Attribute(xaml + "Name") == "FaqIconPacksExpander");
            Assert.Contains(section.Descendants(ui + "TextBlock"), value => (string?)value.Attribute("Text") ==
                (mobile ? "{Binding FaqImportIconPacksDisclaimerText}" : "{DynamicResource FaqImportIconPacksDisclaimer}"));
            var links = section.Descendants(ui + "HyperlinkButton").ToArray();
            Assert.Equal(new[]
            {
                "https://github.com/Legends/otp-harbor-icon-pack-builder/releases/latest",
                "https://github.com/simple-icons/simple-icons/blob/develop/DISCLAIMER.md",
                "https://github.com/aegis-icons/aegis-icons#disclaimer",
                "https://github.com/homarr-labs/dashboard-icons#legal"
            }, links.Select(value => (string?)value.Attribute("NavigateUri")));
            foreach (var link in links.Skip(1))
                Assert.Contains(link.Descendants(ui + "TextBlock"), value => (string?)value.Attribute("TextWrapping") == "Wrap");
            Assert.Contains(section.Descendants(ui + "TextBlock"), value => (string?)value.Attribute("Text") ==
                (mobile ? "{Binding FaqImportIconPacksCustomIconTipText}" : "{DynamicResource FaqImportIconPacksCustomIconTip}"));
        }
    }

    [Theory]
    [InlineData("en", "Can I import a vault from an older version of OTP Harbor?")]
    [InlineData("de", "Kann ich einen Tresor aus einer älteren Version von OTP Harbor importieren?")]
    [InlineData("fr", "Puis-je importer un coffre-fort provenant d’une ancienne version d’OTP Harbor ?")]
    [InlineData("es", "¿Puedo importar una caja fuerte de una versión anterior de OTP Harbor?")]
    public void OlderVaultHelp_DescribesSupportedImportUsingLocalizedNavigation(string locale, string question)
    {
        var catalog = new AvaloniaStringCatalog();
        var culture = CultureInfo.GetCultureInfo(locale);
        Assert.Equal(question, catalog.Get(AvaloniaStringKeys.FaqOlderVaultsQuestion, culture));
        var answer = catalog.Get(AvaloniaStringKeys.FaqOlderVaultsAnswer, culture);
        foreach (var key in new[] { AvaloniaStringKeys.Settings, MigrationStringKeys.DataAndBackup, AvaloniaStringKeys.ImportAccounts })
            Assert.Contains(catalog.Get(key, culture), answer, StringComparison.Ordinal);
        foreach (var extension in new[] { ".totp", ".otphbp", ".otphbackup" })
            Assert.Contains(extension, answer, StringComparison.Ordinal);
        Assert.DoesNotContain("FaqAuthorizationChangesQuestion", AvaloniaStringKeys.All);
        Assert.DoesNotContain("FaqAuthorizationChangesAnswer", AvaloniaStringKeys.All);
    }

    [Fact]
    public void AboutKeepsAppInformationAndUpdates_DiagnosticsAndStorageBelongToSupport()
    {
        var document = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Avalonia", "DesktopSettingsWindow.axaml"));
        XNamespace ui = "https://github.com/avaloniaui";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var tabs = document.Descendants(ui + "TabItem").ToArray();
        var about = tabs[6];
        var support = tabs[4];
        foreach (var name in new[] { "AppInformationPanel", "SoftwareUpdatesHost" })
        {
            Assert.Single(about.Descendants(), control => (string?)control.Attribute(xaml + "Name") == name);
            Assert.DoesNotContain(support.Descendants(), control => (string?)control.Attribute(xaml + "Name") == name);
        }
        foreach (var name in new[] { "MiscSettingsHost", "SupportDiagnosticsHost", "StorageLocationsHost" })
        {
            var host = Assert.Single(support.Descendants(), control => (string?)control.Attribute(xaml + "Name") == name);
            Assert.Contains(host.Ancestors(ui + "ScrollViewer"), control => (string?)control.Attribute(xaml + "Name") == "SupportSettingsScroll");
            Assert.DoesNotContain(about.Descendants(), control => (string?)control.Attribute(xaml + "Name") == name);
        }
        Assert.Equal("{Binding Diagnostics}", (string?)support.Descendants(ui + "ContentControl")
            .Single(control => (string?)control.Attribute(xaml + "Name") == "SupportDiagnosticsHost").Attribute("Content"));
        Assert.Equal("{Binding SettingsPage.StorageLocations}", (string?)support.Descendants(ui + "ContentControl")
            .Single(control => (string?)control.Attribute(xaml + "Name") == "StorageLocationsHost").Attribute("Content"));
        var updates = about.Descendants(ui + "ContentControl").Single(control => (string?)control.Attribute(xaml + "Name") == "SoftwareUpdatesHost");
        Assert.Equal("{Binding UpdateCheck.IsSoftwareUpdatesVisible}", (string?)updates.Attribute("IsVisible"));
        Assert.Equal("{Binding IsSoftwareUpdatesVisible}", (string?)updates.Descendants(ui + "Border")
            .Single(control => (string?)control.Attribute(xaml + "Name") == "AboutSoftwareUpdatesCard").Attribute("IsVisible"));
        Assert.Equal("{Binding Diagnostics}", (string?)about.Descendants(ui + "ContentControl")
            .Single(control => (string?)control.Attribute(xaml + "Name") == "AboutDiagnosticsHost").Attribute("Content"));
    }

    [Fact]
    public void RecoveryHelp_IsOnlyInFaqWhileSecurityActionsAndBackupExportRemainInPlace()
    {
        var document = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Avalonia", "DesktopSettingsWindow.axaml"));
        XNamespace ui = "https://github.com/avaloniaui";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var tabs = document.Descendants(ui + "TabControl").Single(control =>
            ((string?)control.Attribute("Classes"))?.Contains("settings-tabs", StringComparison.Ordinal) == true)
            .Elements(ui + "TabItem").ToArray();
        var questions = new[] { AvaloniaStringKeys.FaqMasterPasswordRecoveryQuestion,
            AvaloniaStringKeys.FaqOlderVaultsQuestion };
        foreach (var key in questions)
        {
            Assert.Single(tabs[5].Descendants(ui + "TextBlock"), text => (string?)text.Attribute("Text") == "{DynamicResource " + key + "}");
            Assert.DoesNotContain(tabs[2].Descendants(ui + "TextBlock"), text => (string?)text.Attribute("Text") == "{DynamicResource " + key + "}");
        }
        Assert.DoesNotContain(document.Descendants(ui + "TextBlock"), text =>
            (string?)text.Attribute("Text") == "{DynamicResource RecoveryAndCompatibility}");
        Assert.DoesNotContain(document.Descendants(), control => (string?)control.Attribute(xaml + "Name") == "FaqAuthorizationChangesExpander");
        foreach (var command in new[] { "UsePasswordCommand", "EnableQuickUnlockCommand", "ChangePasswordCommand" })
            Assert.Contains(tabs[2].Descendants(), control => (string?)control.Attribute("Command") == "{Binding " + command + "}");
        var manualBackup = Assert.Single(tabs[3].Descendants(ui + "Border"), control =>
            (string?)control.Attribute(xaml + "Name") == "EncryptedBackupExportPanel");
        Assert.Contains(manualBackup.Descendants(ui + "TextBlock"), text =>
            (string?)text.Attribute("Text") == "{DynamicResource DataBackupManualTitle}");
        Assert.Contains(manualBackup.Descendants(ui + "Button"), button => (string?)button.Attribute("Command") == "{Binding ExportCommand}");
    }
}
