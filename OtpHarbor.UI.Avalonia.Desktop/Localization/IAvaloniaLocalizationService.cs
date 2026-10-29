using Avalonia.Media;

namespace TOTP.Avalonia.Desktop.Localization;

public interface IAvaloniaLocalizationService
{
    event EventHandler? CultureChanged;
    IReadOnlyList<LanguageOption> SupportedLanguages { get; }
    LanguageOption CurrentLanguage { get; }
    void ApplyCulture(string cultureName);
    string GetString(string key);
}

public sealed record LanguageOption(string CultureName, string DisplayName)
{
    public IImage? Icon { get; init; }
}
