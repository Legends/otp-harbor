using System.Xml.Linq;

namespace TOTP.Tests.Avalonia;

public sealed class DesktopUnlockInteractionTests
{
    [Fact]
    public void BiometricUnlock_UsesAccessibleFingerprintIconButton()
    {
        var document = XDocument.Load(Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "Avalonia",
            "DesktopMainWindow.axaml"));
        XNamespace avalonia = "https://github.com/avaloniaui";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        XNamespace automation = "using:Avalonia.Automation";

        var button = document
            .Descendants(avalonia + "Button")
            .Single(element => element.Attribute(xaml + "Name")?.Value == "BiometricUnlockButton");

        Assert.Equal("{Binding QuickUnlockCommand}", button.Attribute("Command")?.Value);
        Assert.Null(button.Attribute("Content"));
        Assert.Equal(
            "{DynamicResource UnlockWithQuickUnlock}",
            button.Attribute(automation + "AutomationProperties.Name")?.Value);
        Assert.Equal(
            "{DynamicResource UnlockWithQuickUnlock}",
            button.Attribute("ToolTip.Tip")?.Value);
        Assert.Equal(5, button.Descendants(avalonia + "Path").Count());

        var returnButton = document
            .Descendants(avalonia + "Button")
            .Single(element => element.Attribute("Command")?.Value
                == "{Binding ReturnToQuickUnlockCommand}");
        Assert.Equal(
            "{Binding IsReturnToQuickUnlockVisible}",
            returnButton.Attribute("IsVisible")?.Value);
        Assert.Equal(
            "{DynamicResource UnlockWithQuickUnlock}",
            returnButton.Attribute("Content")?.Value);
    }
}
