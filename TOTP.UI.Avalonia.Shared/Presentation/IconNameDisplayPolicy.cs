namespace TOTP.Avalonia.Shared.Presentation;

public static class IconNameDisplayPolicy
{
    public const int MaximumVisibleCharacters = 50;

    public static string Truncate(string? value)
    {
        var text = value ?? string.Empty;
        return text.Length <= MaximumVisibleCharacters
            ? text
            : text[..MaximumVisibleCharacters] + "...";
    }
}
