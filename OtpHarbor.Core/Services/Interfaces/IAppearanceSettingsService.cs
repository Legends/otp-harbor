using FluentResults;
using TOTP.Core.Enums;

namespace TOTP.Core.Services.Interfaces;

/// <summary>
/// Stores non-sensitive visual preferences separately from the versioned vault preferences.
/// </summary>
public interface IAppearanceSettingsService
{
    event EventHandler? PreferenceChanged;

    AppThemePreference ThemePreference { get; }

    Task<Result> SetThemePreferenceAsync(
        AppThemePreference preference,
        CancellationToken cancellationToken = default);
}
