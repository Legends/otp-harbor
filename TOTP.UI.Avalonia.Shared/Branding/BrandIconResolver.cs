using System.Collections.Concurrent;
using System.Text;
using Avalonia.Media;
using TOTP.Core.Services.Interfaces;

namespace TOTP.Avalonia.Shared.Branding;

public sealed class BrandIconResolver : IBrandIconResolver, IDisposable
{
    private static readonly string[] FallbackPalette =
    [
        "#334155", "#1E3A8A", "#075985", "#115E59", "#166534", "#3F6212",
        "#854D0E", "#9A3412", "#991B1B", "#9D174D", "#6B21A8", "#4338CA"
    ];

    private readonly IBrandIconPackService _packService;
    private readonly ConcurrentDictionary<string, BrandInfo> _known =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, BrandInfo> _fallback =
        new(StringComparer.Ordinal);
    private bool _disposed;

    public BrandIconResolver(IBrandIconPackService packService)
    {
        _packService = packService ?? throw new ArgumentNullException(nameof(packService));
        _packService.CatalogChanged += PackCatalogChanged;
    }

    public event EventHandler? CatalogChanged;

    public bool ShowIssuerLogo => _packService.ShowIssuerLogo;

    public BrandInfo Resolve(string? issuer, string? explicitBrandId = null)
    {
        var definition = _packService.Resolve(issuer, explicitBrandId);
        return definition is not null
            ? _known.GetOrAdd(definition.Id, _ => CreateKnown(definition))
            : CreateFallbackForIssuer(issuer);
    }

    public BrandInfo ResolveAccount(string? issuer, string? accountName, string? explicitBrandId = null)
    {
        var definition = _packService.ResolveAccount(issuer, accountName, explicitBrandId);
        if (definition is not null)
            return _known.GetOrAdd(definition.Id, _ => CreateKnown(definition));

        return CreateFallbackForIssuer(string.IsNullOrWhiteSpace(issuer) ? accountName : issuer);
    }

    public BrandInfo ResolveAccount(Guid accountId, string? issuer, string? accountName) =>
        ResolveAccount(issuer, accountName, _packService.GetAccountBrandId(accountId));

    private BrandInfo CreateFallbackForIssuer(string? issuer)
    {
        var displayName = string.IsNullOrWhiteSpace(issuer) ? string.Empty : issuer.Trim();
        var key = Normalize(displayName);
        return _fallback.GetOrAdd(key, _ => CreateFallback(displayName, key));
    }

    private BrandInfo CreateKnown(TOTP.Core.Services.Models.BrandDefinition definition)
    {
        string? geometry = null;
        if (_packService.TryGetIconPathData(definition.Id, out var pathData))
            geometry = pathData;
        var color = NormalizeColor(definition.BackgroundColor);
        return new BrandInfo(
            definition.Id,
            definition.DisplayName,
            InitialsFor(definition.DisplayName),
            color,
            new SolidColorBrush(Color.Parse(color)),
            geometry);
    }

    private static BrandInfo CreateFallback(string issuer, string normalized)
    {
        var color = FallbackPalette[StableHash(normalized) % (uint)FallbackPalette.Length];
        return new BrandInfo(
            null,
            issuer,
            InitialsFor(issuer),
            color,
            new SolidColorBrush(Color.Parse(color)),
            null);
    }

    private void PackCatalogChanged(object? sender, EventArgs args)
    {
        _known.Clear();
        _fallback.Clear();
        CatalogChanged?.Invoke(this, EventArgs.Empty);
    }

    private static string InitialsFor(string value)
    {
        var words = value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 0) return "?";
        if (words.Length > 1)
            return string.Concat(FirstLetterOrDigit(words[0]), FirstLetterOrDigit(words[^1])).ToUpperInvariant();
        var characters = words[0].Where(char.IsLetterOrDigit).Take(2).ToArray();
        return characters.Length == 0 ? "?" : new string(characters).ToUpperInvariant();
    }

    private static char FirstLetterOrDigit(string value) =>
        value.FirstOrDefault(char.IsLetterOrDigit) is var character && character != default ? character : '?';

    private static string Normalize(string value)
    {
        if (value.Length == 0) return "unknown";
        var builder = new StringBuilder(value.Length);
        foreach (var character in value.Normalize(NormalizationForm.FormKC))
            if (char.IsLetterOrDigit(character)) builder.Append(char.ToLowerInvariant(character));
        return builder.Length == 0 ? "unknown" : builder.ToString();
    }

    private static uint StableHash(string value)
    {
        var hash = 2166136261u;
        foreach (var character in value)
        {
            hash ^= character;
            hash *= 16777619u;
        }
        return hash;
    }

    private static string NormalizeColor(string value) =>
        value.Length == 7 && value[0] == '#' ? value : "#334155";

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _packService.CatalogChanged -= PackCatalogChanged;
    }
}
