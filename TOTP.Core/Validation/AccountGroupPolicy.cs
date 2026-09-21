using TOTP.Core.Models;

namespace TOTP.Core.Validation;

public static class AccountGroupPolicy
{
    public const int MaximumNameLength = 64;

    public static readonly IReadOnlyList<string> AllowedColors =
    [
        "#4C956C",
        "#18A999",
        "#4F6BED",
        "#F59E0B",
        "#E45757",
        "#B455C7"
    ];

    public static bool TryNormalize(AccountGroup? group, out AccountGroup? normalized)
    {
        if (group is null)
        {
            normalized = null;
            return true;
        }

        var name = group.Name?.Trim();
        var color = group.Color?.Trim().ToUpperInvariant();
        if (group.Id == Guid.Empty
            || string.IsNullOrWhiteSpace(name)
            || name.Length > MaximumNameLength
            || color is null
            || !AllowedColors.Contains(color, StringComparer.Ordinal))
        {
            normalized = null;
            return false;
        }

        normalized = new AccountGroup(group.Id, name, color);
        return true;
    }

    public static bool TryNormalizeStored(AccountGroup? group, out AccountGroup? normalized)
    {
        if (group is null)
        {
            normalized = null;
            return true;
        }

        var name = group.Name?.Trim();
        if (group.Id == Guid.Empty
            || string.IsNullOrWhiteSpace(name)
            || name.Length > MaximumNameLength)
        {
            normalized = null;
            return false;
        }

        normalized = new AccountGroup(group.Id, name, NormalizeColor(group.Color));
        return true;
    }

    public static string NormalizeColor(string? color)
    {
        var candidate = color?.Trim();
        return IsHexColor(candidate)
            ? candidate!.ToUpperInvariant()
            : AllowedColors[0];
    }

    private static bool IsHexColor(string? color) =>
        color is { Length: 7 }
        && color[0] == '#'
        && color.AsSpan(1).ToString().All(Uri.IsHexDigit);
}
