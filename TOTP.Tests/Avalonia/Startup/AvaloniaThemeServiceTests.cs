using Avalonia.Platform;
using Avalonia.Styling;
using Moq;
using TOTP.Avalonia.Shared.Appearance;
using TOTP.Avalonia.Shared.Styles;
using TOTP.Core.Enums;
using TOTP.Core.Services.Interfaces;

namespace TOTP.Tests.Avalonia.Startup;

public sealed class AvaloniaThemeServiceTests
{
    [Fact]
    public void Start_WhenPlatformRequestsHighContrast_AppliesDedicatedVariant()
    {
        var platform = new Mock<IPlatformSettings>();
        platform.Setup(value => value.GetColorValues()).Returns(Colors(ColorContrastPreference.High));
        var appearance = Appearance(AppThemePreference.Light);
        ThemeVariant? applied = null;
        using var sut = new AvaloniaThemeService(
            platform.Object,
            appearance.Object,
            value => applied = value);

        sut.Start();

        Assert.Same(AvaloniaThemeVariants.HighContrast, applied);
    }

    [Fact]
    public void ColorChange_ReturnsToSelectedDarkThemeAndDisposeUnsubscribes()
    {
        var platform = new Mock<IPlatformSettings>();
        platform.Setup(value => value.GetColorValues()).Returns(Colors(ColorContrastPreference.High));
        var appearance = Appearance(AppThemePreference.Dark);
        var applied = new List<ThemeVariant>();
        var sut = new AvaloniaThemeService(platform.Object, appearance.Object, applied.Add);
        sut.Start();

        platform.Raise(
            value => value.ColorValuesChanged += null,
            platform.Object,
            Colors(ColorContrastPreference.NoPreference));
        sut.Dispose();
        platform.Raise(
            value => value.ColorValuesChanged += null,
            platform.Object,
            Colors(ColorContrastPreference.High));

        Assert.Equal([AvaloniaThemeVariants.HighContrast, ThemeVariant.Dark], applied);
    }

    [Fact]
    public void Start_WithoutPlatformSettings_UsesSafeDefault()
    {
        var appearance = Appearance(AppThemePreference.System);
        ThemeVariant? applied = null;
        using var sut = new AvaloniaThemeService(null, appearance.Object, value => applied = value);

        sut.Start();

        Assert.Same(ThemeVariant.Dark, applied);
    }

    [Fact]
    public void PreferenceChange_AppliesLightThemeImmediately()
    {
        var platform = new Mock<IPlatformSettings>();
        platform.Setup(value => value.GetColorValues()).Returns(Colors(
            ColorContrastPreference.NoPreference,
            PlatformThemeVariant.Dark));
        var appearance = Appearance(AppThemePreference.Dark);
        var applied = new List<ThemeVariant>();
        using var sut = new AvaloniaThemeService(platform.Object, appearance.Object, applied.Add);
        sut.Start();

        appearance.SetupGet(value => value.ThemePreference).Returns(AppThemePreference.Light);
        appearance.Raise(value => value.PreferenceChanged += null, appearance.Object, EventArgs.Empty);

        Assert.Equal([ThemeVariant.Dark, ThemeVariant.Light], applied);
    }

    [Fact]
    public void SystemPreference_FollowsPlatformTheme()
    {
        var platform = new Mock<IPlatformSettings>();
        platform.Setup(value => value.GetColorValues()).Returns(Colors(
            ColorContrastPreference.NoPreference,
            PlatformThemeVariant.Light));
        var appearance = Appearance(AppThemePreference.System);
        ThemeVariant? applied = null;
        using var sut = new AvaloniaThemeService(
            platform.Object,
            appearance.Object,
            value => applied = value);

        sut.Start();

        Assert.Same(ThemeVariant.Light, applied);
    }

    private static Mock<IAppearanceSettingsService> Appearance(AppThemePreference preference)
    {
        var appearance = new Mock<IAppearanceSettingsService>();
        appearance.SetupGet(value => value.ThemePreference).Returns(preference);
        return appearance;
    }

    private static PlatformColorValues Colors(
        ColorContrastPreference contrast,
        PlatformThemeVariant theme = PlatformThemeVariant.Dark) =>
        new() { ContrastPreference = contrast, ThemeVariant = theme };
}
