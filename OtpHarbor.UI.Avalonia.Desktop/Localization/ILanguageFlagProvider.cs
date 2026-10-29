using Avalonia.Media;

namespace TOTP.Avalonia.Desktop.Localization;

public interface ILanguageFlagProvider
{
    IImage GetFlag(string cultureName);
}
