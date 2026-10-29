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
        Assert.Equal("UnlockCard", biometricCard.Attribute(xaml + "Name")?.Value);
        Assert.Equal("{Binding IsUnlockVisible}", biometricCard.Attribute("IsVisible")?.Value);
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

        Assert.DoesNotContain(
            document.Descendants(avalonia + "Button"),
            element => element.Attribute("Command")?.Value
                == "{Binding UsePasswordFallbackCommand}");
        Assert.DoesNotContain(
            document.Descendants(avalonia + "Button"),
            element => element.Attribute("Command")?.Value
                == "{Binding ReturnToQuickUnlockCommand}");

        var passwordInput = biometricCard
            .Descendants()
            .Single(element => element.Name.LocalName == "RevealableSecretInput"
                && element.Attribute(xaml + "Name")?.Value == "PasswordUnlockInput");
        Assert.Equal(
            "{Binding PasswordUnlock.Password, Mode=TwoWay}",
            passwordInput.Attribute("Text")?.Value);
        Assert.Equal("{Binding !IsQuickUnlockBusy}", passwordInput.Attribute("IsEnabled")?.Value);

        var passwordMessageSlot = biometricCard
            .Descendants(avalonia + "Border")
            .Single(element => element.Attribute(xaml + "Name")?.Value
                == "PasswordUnlockMessageSlot");
        Assert.Equal("54", passwordMessageSlot.Attribute("MinHeight")?.Value);
        Assert.Equal("StackPanel", passwordMessageSlot.Parent?.Name.LocalName);
        Assert.Equal("3", passwordMessageSlot.Parent?.Attribute("Spacing")?.Value);
        var passwordMessage = passwordMessageSlot
            .Descendants()
            .Single(element => element.Name.LocalName == "ValidationMessage");
        Assert.Equal(
            "PasswordUnlockValidationMessage",
            passwordMessage.Attribute(xaml + "Name")?.Value);
        Assert.Equal("Error", passwordMessage.Attribute("Severity")?.Value);
        Assert.Equal(
            "{Binding PasswordUnlock.Message}",
            passwordMessage.Attribute("Text")?.Value);

        var passwordUnlockButton = biometricCard
            .Descendants(avalonia + "Button")
            .Single(element => element.Attribute(xaml + "Name")?.Value
                == "MasterPasswordUnlockButton");
        Assert.Equal(
            "{Binding PasswordUnlock.UnlockCommand}",
            passwordUnlockButton.Attribute("Command")?.Value);
        Assert.Equal("{DynamicResource Unlock}", passwordUnlockButton.Attribute("Content")?.Value);
        Assert.Equal("True", passwordUnlockButton.Attribute("IsDefault")?.Value);
        Assert.Equal("{Binding !IsQuickUnlockBusy}", passwordUnlockButton.Attribute("IsEnabled")?.Value);

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

    }
}
