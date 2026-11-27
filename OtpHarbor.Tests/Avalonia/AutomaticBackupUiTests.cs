using System.Xml.Linq;

namespace TOTP.Tests.Avalonia;

public sealed class AutomaticBackupUiTests
{
    private static readonly XNamespace Avalonia = "https://github.com/avaloniaui";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void DesktopSettings_ExposesConfiguredAutomaticBackupWorkflow()
    {
        var document = LoadFixture("DesktopSettingsWindow.axaml");

        var panel = NamedBorder(document, "AutomaticBackupPanel");

        var export = NamedBorder(document, "EncryptedBackupExportPanel");
        Assert.Same(export.Parent, panel.Parent);
        Assert.Contains(panel, export.NodesAfterSelf());
        Assert.DoesNotContain(document.Descendants(), element => element.Attribute(Xaml + "Name")?.Value == "EncryptedBackupRestorePanel");
        Assert.DoesNotContain(document.Descendants(Avalonia + "Button"), element => element.Attribute("Command")?.Value == "{Binding RestoreEncryptedBackupCommand}");
        Assert.Contains(panel.Descendants(Avalonia + "TextBlock"), element =>
            element.Attribute("Text")?.Value == "{DynamicResource DataBackupAutomaticHelp}");
        Assert.Contains(panel.Descendants(Avalonia + "TextBlock"), element =>
            element.Attribute("Text")?.Value == "{DynamicResource AutomaticBackupFolderAccessLabel}");
        Assert.Contains(panel.Descendants(Avalonia + "TextBlock"), element =>
            element.Attribute("Text")?.Value == "{Binding AutomaticBackupDestinationAccessValue}");
        Assert.Contains(panel.Descendants(Avalonia + "TextBlock"), element =>
            element.Attribute("Text")?.Value == "{DynamicResource AutomaticBackupLastSuccessLabel}");
        Assert.Contains(panel.Descendants(Avalonia + "TextBlock"), element =>
            element.Attribute("Text")?.Value == "{Binding AutomaticBackupLastSuccessValue}");
        Assert.Contains(panel.Descendants(Avalonia + "TextBlock"), element =>
            element.Attribute("Text")?.Value == "{Binding AutomaticBackupLastFailureText}");
        Assert.Contains(panel.Descendants(Avalonia + "Button"), element =>
            element.Attribute("Command")?.Value == "{Binding ConfigureAutomaticBackupCommand}");
        Assert.Contains(panel.Descendants(Avalonia + "Button"), element =>
            element.Attribute("Command")?.Value == "{Binding AutomaticBackupNowCommand}");
        Assert.Contains(panel.Descendants(), element =>
            element.Attribute("Command")?.Value == "{Binding ToggleAutomaticBackupCommand}");
        var location = panel.Descendants(Avalonia + "StackPanel").Single(element =>
            element.Attribute(Xaml + "Name")?.Value == "AutomaticBackupLocationSection");
        Assert.Equal("{Binding IsAutomaticBackupConfigured}", location.Attribute("IsEnabled")?.Value);
        Assert.Null(location.Attribute("IsVisible"));
    }

    [Fact]
    public void AndroidSharedView_ExposesResponsiveAutomaticBackupWorkflow()
    {
        var document = LoadFixture("MobileMainView.axaml");

        var panel = NamedBorder(document, "AutomaticBackupPanel");

        Assert.Equal("1", panel.Attribute("Grid.Row")?.Value);
        Assert.Equal("0", NamedBorder(document, "BackupExportPanel").Attribute("Grid.Row")?.Value);
        Assert.DoesNotContain(document.Descendants(), element => element.Attribute(Xaml + "Name")?.Value == "BackupRestorePanel");
        Assert.DoesNotContain(document.Descendants(Avalonia + "Button"), element => element.Attribute("Command")?.Value == "{Binding ImportBackupCommand}");
        Assert.Contains(panel.Descendants(Avalonia + "TextBlock"), element =>
            element.Attribute("Text")?.Value == "{Binding AutomaticBackupDescriptionText}");
        Assert.Contains(panel.Descendants(Avalonia + "TextBlock"), element =>
            element.Attribute("Text")?.Value == "{Binding AutomaticBackupFolderGuidanceText}");
        Assert.Contains(panel.Descendants(Avalonia + "TextBlock"), element =>
            element.Attribute("Text")?.Value == "{Binding AutomaticBackupDestinationAccessText}");
        Assert.Contains(panel.Descendants(Avalonia + "TextBlock"), element =>
            element.Attribute("Text")?.Value == "{Binding AutomaticBackupLastFailureText}");
        Assert.Contains(panel.Descendants(Avalonia + "Button"), element =>
            element.Attribute("Command")?.Value == "{Binding ConfigureAutomaticBackupCommand}");
        Assert.Contains(panel.Descendants(Avalonia + "Button"), element =>
            element.Attribute("Command")?.Value == "{Binding AutomaticBackupNowCommand}");
        Assert.Contains(panel.Descendants(Avalonia + "Button"), element =>
            element.Attribute("Command")?.Value == "{Binding DisableAutomaticBackupCommand}");
    }

    private static XElement NamedBorder(XDocument document, string name) =>
        Assert.Single(document.Descendants(Avalonia + "Border"), element =>
            element.Attribute(Xaml + "Name")?.Value == name);

    private static XDocument LoadFixture(string name) => XDocument.Load(Path.Combine(
        AppContext.BaseDirectory,
        "Fixtures",
        "Avalonia",
        name));
}
