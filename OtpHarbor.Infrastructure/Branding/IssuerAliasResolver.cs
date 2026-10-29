using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TOTP.Infrastructure.Branding;

internal sealed partial class IssuerAliasResolver
{
    private const int CurrentSchemaVersion = 1;
    private const int MaximumEntries = 512;
    private const int MaximumAliasesPerEntry = 32;
    private const int MaximumAliasLength = 128;
    private const string ResourceName = "TOTP.Infrastructure.Branding.issuer-resolver.v1.json";

    private readonly IReadOnlyDictionary<string, string> _aliases;

    private IssuerAliasResolver(IReadOnlyDictionary<string, string> aliases)
    {
        _aliases = aliases;
    }

    internal static IssuerAliasResolver Empty { get; } = new(
        new Dictionary<string, string>(StringComparer.Ordinal));

    internal int AliasCount => _aliases.Count;

    internal static IssuerAliasResolver LoadDefault()
    {
        using var stream = typeof(IssuerAliasResolver).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidDataException("The embedded issuer resolver database is missing.");
        return Load(stream);
    }

    internal static IssuerAliasResolver Load(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var document = JsonDocument.Parse(stream, new JsonDocumentOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = 8
        });
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("schemaVersion", out var schemaVersion)
            || schemaVersion.ValueKind != JsonValueKind.Number
            || !schemaVersion.TryGetInt32(out var version)
            || version != CurrentSchemaVersion
            || !root.TryGetProperty("entries", out var entries)
            || entries.ValueKind != JsonValueKind.Array
            || entries.GetArrayLength() is <= 0 or > MaximumEntries)
        {
            throw new InvalidDataException("The issuer resolver database has an unsupported shape or version.");
        }

        var brandIds = new HashSet<string>(StringComparer.Ordinal);
        var aliases = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in entries.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object
                || !entry.TryGetProperty("brandId", out var brandIdElement)
                || brandIdElement.ValueKind != JsonValueKind.String
                || !entry.TryGetProperty("aliases", out var aliasElements)
                || aliasElements.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException("The issuer resolver database contains an invalid entry.");
            }

            var brandId = brandIdElement.GetString();
            if (brandId is null
                || !SafeBrandIdRegex().IsMatch(brandId)
                || !brandIds.Add(brandId)
                || aliasElements.GetArrayLength() is <= 0 or > MaximumAliasesPerEntry)
            {
                throw new InvalidDataException("The issuer resolver database contains an invalid or duplicate brand id.");
            }

            foreach (var aliasElement in aliasElements.EnumerateArray())
            {
                if (aliasElement.ValueKind != JsonValueKind.String)
                    throw new InvalidDataException("The issuer resolver database contains a non-text alias.");
                var alias = aliasElement.GetString();
                if (string.IsNullOrWhiteSpace(alias) || alias.Length > MaximumAliasLength)
                    throw new InvalidDataException("The issuer resolver database contains an invalid alias.");
                var normalized = Normalize(alias);
                if (normalized.Length == 0)
                    throw new InvalidDataException("The issuer resolver database contains an invalid alias.");
                if (aliases.TryGetValue(normalized, out var existingBrandId))
                {
                    var conflict = !string.Equals(existingBrandId, brandId, StringComparison.Ordinal);
                    throw new InvalidDataException(conflict
                        ? "The issuer resolver database contains an ambiguous alias."
                        : "The issuer resolver database contains a duplicate alias.");
                }
                aliases.Add(normalized, brandId);
            }
        }

        return new IssuerAliasResolver(aliases);
    }

    internal bool TryResolve(string normalizedIssuer, out string brandId) =>
        _aliases.TryGetValue(normalizedIssuer, out brandId!);

    internal static string Normalize(string value)
    {
        var builder = new StringBuilder(value.Length);
        var needsSpace = false;
        foreach (var character in value.Normalize(NormalizationForm.FormKC))
        {
            if (char.IsLetterOrDigit(character))
            {
                if (needsSpace && builder.Length > 0) builder.Append(' ');
                builder.Append(char.ToLowerInvariant(character));
                needsSpace = false;
            }
            else needsSpace = true;
        }
        return builder.ToString();
    }

    [GeneratedRegex("^[a-z0-9_]+$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeBrandIdRegex();
}
