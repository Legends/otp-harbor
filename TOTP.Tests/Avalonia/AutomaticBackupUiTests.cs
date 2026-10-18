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

        Assert.Equal("2", panel.Attribute("Grid.Row")?.Value);
        Assert.Equal("0", NamedBorder(document, "EncryptedBackupExportPanel").Attribute("Grid.Row")?.Value);
        Assert.Equal("1", NamedBorder(document, "EncryptedBackupRestorePanel").Attribute("Grid.Row")?.Value);
        Assert.Contains(panel.Descendants(Avalonia + "TextBlock"), element =>
            element.Attribute("Text")?.Value == "{DynamicResource AutomaticBackupDescription}");
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

    [Fact]
    public void AndroidSharedView_ExposesResponsiveAutomaticBackupWorkflow()
    {
        var document = LoadFixture("MobileMainView.axaml");

        var panel = NamedBorder(document, "AutomaticBackupPanel");

        Assert.Equal("2", panel.Attribute("Grid.Row")?.Value);
        Assert.Equal("0", NamedBorder(document, "BackupExportPanel").Attribute("Grid.Row")?.Value);
        Assert.Equal("1", NamedBorder(document, "BackupRestorePanel").Attribute("Grid.Row")?.Value);
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
