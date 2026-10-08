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
        Assert.Equal("68", button.Attribute("Width")?.Value);
        Assert.Equal("primary biometric-unlock", button.Attribute("Classes")?.Value);
        var fingerprint = button
            .Descendants()
            .Single(element => element.Name.LocalName == "FingerprintIcon");
        Assert.Equal("50", fingerprint.Attribute("IconSize")?.Value);

        var message = document
            .Descendants()
            .Single(element => element.Name.LocalName == "ValidationMessage"
                && element.Attribute("Text")?.Value == "{Binding QuickUnlockMessage}");
        Assert.Equal("quick-unlock-message", message.Attribute("Classes")?.Value);
        Assert.Equal("20", message.Parent?.Attribute("Height")?.Value);

        var progress = message.Parent?
            .Elements(avalonia + "ProgressBar")
            .Single();
        Assert.Equal("{Binding IsQuickUnlockBusy}", progress?.Attribute("IsVisible")?.Value);
        Assert.Equal("{Binding IsQuickUnlockBusy}", progress?.Attribute("IsIndeterminate")?.Value);
        Assert.Equal("4", progress?.Attribute("Height")?.Value);

        var messageStyle = document
            .Descendants(avalonia + "Style")
            .Single(element => element.Attribute("Selector")?.Value
                == "shared|ValidationMessage.quick-unlock-message /template/ TextBlock#PART_Message");
        var setters = messageStyle
            .Descendants(avalonia + "Setter")
            .ToDictionary(
                element => element.Attribute("Property")!.Value,
                element => element.Attribute("Value")!.Value);
        Assert.Equal("1", setters["MaxLines"]);
        Assert.Equal("CharacterEllipsis", setters["TextTrimming"]);
        Assert.Equal("NoWrap", setters["TextWrapping"]);

        var authorizationScroller = document
            .Descendants(avalonia + "ScrollViewer")
            .Single(element => element.Attribute("IsVisible")?.Value == "{Binding !IsShellVisible}");
        Assert.Equal(
            "Hidden",
            authorizationScroller.Attribute("VerticalScrollBarVisibility")?.Value);

        var biometricCard = document
            .Descendants(avalonia + "Border")
            .Single(element => element.Attribute("Classes")?.Value
                == "unlock-card biometric-card");
        Assert.Equal("unlock-card biometric-card", biometricCard.Attribute("Classes")?.Value);
        var biometricCardStyle = document
            .Descendants(avalonia + "Style")
            .Single(element => element.Attribute("Selector")?.Value
                == "Border.unlock-card.biometric-card");
        Assert.Contains(
            biometricCardStyle.Descendants(avalonia + "Setter"),
            setter => setter.Attribute("Property")?.Value == "Background"
                && setter.Attribute("Value")?.Value
                    == "{DynamicResource BrushBiometricCardBackground}");

        var artwork = document
            .Descendants(avalonia + "Border")
            .Single(element => element.Attribute(xaml + "Name")?.Value
                == "DesktopLockScreenArtwork");
        Assert.Equal(
            "{DynamicResource BrushBiometricLockScreenArtwork}",
            artwork.Attribute("Background")?.Value);
        Assert.Equal("{Binding IsQuickUnlockVisible}", artwork.Attribute("IsVisible")?.Value);

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
