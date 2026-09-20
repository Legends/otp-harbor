using Avalonia.Platform;
using Avalonia.Styling;
using TOTP.Avalonia.Shared.Styles;
using TOTP.Core.Enums;
using TOTP.Core.Services.Interfaces;

namespace TOTP.Avalonia.Shared.Appearance;

public sealed class AvaloniaThemeService(
    IPlatformSettings? platformSettings,
    IAppearanceSettingsService appearanceSettings,
    Action<ThemeVariant> applyTheme) : IDisposable
{
    private bool _started;
    private bool _disposed;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_started) return;
        _started = true;

        appearanceSettings.PreferenceChanged += AppearancePreferenceChanged;
        if (platformSettings is not null)
            platformSettings.ColorValuesChanged += PlatformColorValuesChanged;
        ApplyCurrent();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (!_started) return;
        appearanceSettings.PreferenceChanged -= AppearancePreferenceChanged;
        if (platformSettings is not null)
            platformSettings.ColorValuesChanged -= PlatformColorValuesChanged;
    }

    private void AppearancePreferenceChanged(object? sender, EventArgs args) => ApplyCurrent();

    private void PlatformColorValuesChanged(object? sender, PlatformColorValues values) =>
        Apply(values);

    private void ApplyCurrent() => Apply(platformSettings?.GetColorValues());

    private void Apply(PlatformColorValues? values)
    {
        if (values?.ContrastPreference == ColorContrastPreference.High)
        {
            applyTheme(AvaloniaThemeVariants.HighContrast);
            return;
        }

        applyTheme(appearanceSettings.ThemePreference switch
        {
            AppThemePreference.Light => ThemeVariant.Light,
            AppThemePreference.System when values?.ThemeVariant == PlatformThemeVariant.Light =>
                ThemeVariant.Light,
            _ => ThemeVariant.Dark
        });
    }
}
