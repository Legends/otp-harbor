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
        Assert.Equal("64", button.Attribute("Width")?.Value);
        Assert.Equal("primary biometric-unlock", button.Attribute("Classes")?.Value);
        var fingerprint = button
            .Descendants()
            .Single(element => element.Name.LocalName == "FingerprintIcon");
        Assert.Equal("48", fingerprint.Attribute("IconSize")?.Value);

        var biometricCard = document
            .Descendants(avalonia + "Border")
            .Single(element => element.Attribute("Classes")?.Value
                == "unlock-card biometric-card");
        Assert.DoesNotContain(
            biometricCard.Descendants(avalonia + "TextBlock"),
            element => element.Attribute("Text")?.Value
                == "{DynamicResource ChooseUnlockMethod}");
        Assert.DoesNotContain(
            biometricCard.Descendants(avalonia + "TextBlock"),
            element => element.Attribute("Text")?.Value
                == "{DynamicResource QuickUnlockRecoveryNotice}");

        var statusBanner = document
            .Descendants()
            .Single(element => element.Name.LocalName == "NotificationBanner"
                && element.Attribute(xaml + "Name")?.Value == "QuickUnlockStatusBanner");
        Assert.Equal(
            "{DynamicResource VaultLockedTitle}",
            statusBanner.Attribute("Title")?.Value);
        Assert.Equal(
            "{DynamicResource VaultLockedQuickUnlock}",
            statusBanner.Attribute("Text")?.Value);

        var divider = biometricCard
            .Descendants(avalonia + "Grid")
            .Single(element => element.Attribute(xaml + "Name")?.Value == "UnlockMethodDivider");
        Assert.Equal(
            "{DynamicResource UnlockMethodSeparator}",
            divider.Descendants(avalonia + "TextBlock").Single().Attribute("Text")?.Value);

        var passwordButton = biometricCard
            .Descendants(avalonia + "Button")
            .Single(element => element.Attribute(xaml + "Name")?.Value
                == "MasterPasswordFallbackButton");
        Assert.Equal("{Binding UsePasswordFallbackCommand}", passwordButton.Attribute("Command")?.Value);
        Assert.Equal("wide master-password-option", passwordButton.Attribute("Classes")?.Value);
        Assert.Equal(
            "{DynamicResource UseMasterPassword}",
            passwordButton.Attribute(automation + "AutomationProperties.Name")?.Value);
        var passwordButtonContent = passwordButton
            .Descendants(avalonia + "Grid")
            .Single(element => element.Attribute(xaml + "Name")?.Value
                == "MasterPasswordFallbackContent");
        Assert.Equal("Center", passwordButtonContent.Attribute("VerticalAlignment")?.Value);
        var passwordButtonIcon = passwordButton
            .Descendants()
            .Single(element => element.Attribute(xaml + "Name")?.Value
                == "MasterPasswordFallbackIcon");
        Assert.Equal("Key", passwordButtonIcon.Attribute("Kind")?.Value);
        var passwordButtonText = passwordButton
            .Descendants(avalonia + "TextBlock")
            .Single(element => element.Attribute(xaml + "Name")?.Value
                == "MasterPasswordFallbackText");
        Assert.Equal(
            "{DynamicResource UseMasterPassword}",
            passwordButtonText.Attribute("Text")?.Value);
        Assert.Equal("Center", passwordButtonText.Attribute("VerticalAlignment")?.Value);
        Assert.Equal("Wrap", passwordButtonText.Attribute("TextWrapping")?.Value);

        var passwordButtonStyle = document
            .Descendants(avalonia + "Style")
            .Single(element => element.Attribute("Selector")?.Value
                == "Button.master-password-option");
        var passwordButtonSetters = passwordButtonStyle
            .Descendants(avalonia + "Setter")
            .ToDictionary(
                element => element.Attribute("Property")!.Value,
                element => element.Attribute("Value")!.Value);
        Assert.Equal("42", passwordButtonSetters["MinHeight"]);
        Assert.Equal("Stretch", passwordButtonSetters["HorizontalContentAlignment"]);
        Assert.Equal("Center", passwordButtonSetters["VerticalContentAlignment"]);
        Assert.Equal("{DynamicResource BrushAccent}", passwordButtonSetters["BorderBrush"]);
        Assert.Equal("{DynamicResource BrushSurfaceAlternate}", passwordButtonSetters["Background"]);

        var message = document
            .Descendants()
            .Single(element => element.Name.LocalName == "ValidationMessage"
                && element.Attribute("Text")?.Value == "{Binding QuickUnlockMessage}");
        Assert.Equal("quick-unlock-message", message.Attribute("Classes")?.Value);
        Assert.Equal("18", message.Parent?.Attribute("Height")?.Value);

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
            "Disabled",
            authorizationScroller.Attribute("VerticalScrollBarVisibility")?.Value);
        Assert.Equal("540", document.Root?.Attribute("MinHeight")?.Value);

        var authorizedShell = document
            .Descendants(avalonia + "Grid")
            .Single(element => element.Attribute(xaml + "Name")?.Value == "AuthorizedShell");
        Assert.Equal("authorized-shell", authorizedShell.Attribute("Classes")?.Value);
        var hiddenShellOpacity = document
            .Descendants(avalonia + "Style")
            .Single(element => element.Attribute("Selector")?.Value == "Grid.authorized-shell")
            .Descendants(avalonia + "Setter")
            .Single(element => element.Attribute("Property")?.Value == "Opacity")
            .Attribute("Value")?.Value;
        Assert.Equal("0", hiddenShellOpacity);
        var shellAnimation = document
            .Descendants(avalonia + "Style")
            .Single(element => element.Attribute("Selector")?.Value == "Grid.authorized-shell.presented")
            .Descendants(avalonia + "Animation")
            .Single();
        Assert.Equal("0:0:0.28", shellAnimation.Attribute("Duration")?.Value);
        Assert.Equal("CubicEaseInOut", shellAnimation.Attribute("Easing")?.Value);
        Assert.Equal("OnlyIfVisible", shellAnimation.Attribute("PlaybackBehavior")?.Value);
        var shellOpacityValues = shellAnimation
            .Descendants(avalonia + "Setter")
            .Where(element => element.Attribute("Property")?.Value == "Opacity")
            .Select(element => element.Attribute("Value")?.Value ?? string.Empty)
            .ToArray();
        Assert.Equal(["0", "1"], shellOpacityValues);

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
