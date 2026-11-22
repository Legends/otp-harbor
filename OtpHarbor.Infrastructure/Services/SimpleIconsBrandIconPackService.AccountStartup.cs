using System.Text.Json;
using Microsoft.Extensions.Logging;
using TOTP.Core.Icons;
using TOTP.Core.Services.Models;
using TOTP.Infrastructure.Branding;

namespace TOTP.Infrastructure.Services;

public sealed partial class SimpleIconsBrandIconPackService
{
    private int _accountLookupInitialized;

    public Task InitializeForAccountsAsync(
        IReadOnlyCollection<string> issuers,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(issuers);
        var ownedIssuers = issuers.ToArray();
        return Task.Run(() =>
        {
            lock (_initializationLock)
            {
                if (_initialized != 0 || _accountLookupInitialized != 0) return;
                cancellationToken.ThrowIfCancellationRequested();
                _issuerAliases = LoadIssuerAliases(_logger);
                TryLoadDisplaySettings();
                TryLoadAccountBrandSettings();
                TryLoadCustomIcons();
                try
                {
                    var registrations = LoadInstalledPackRegistrations();
                    var snapshot = LoadCombinedCatalog(registrations, ownedIssuers);
                    cancellationToken.ThrowIfCancellationRequested();
                    Volatile.Write(ref _catalog, snapshot);
                }
                catch (Exception exception) when (exception is IOException
                    or UnauthorizedAccessException or JsonException or InvalidDataException
                    or InvalidOperationException or KeyNotFoundException)
                {
                    _logger.LogWarning("The local account icon lookup could not be loaded.");
                }
                Volatile.Write(ref _accountLookupInitialized, 1);
            }
        }, cancellationToken);
    }

    private void EnsureLookupInitialized()
    {
        if (Volatile.Read(ref _accountLookupInitialized) == 0)
            EnsureInitialized();
    }

    private BrandIndex? TryReadAccountBrandIndex(string packId, IReadOnlyCollection<string> issuers)
    {
        if (!SafePackIdRegex().IsMatch(packId)) return null;
        var packDirectory = Path.Combine(_packsRoot, packId);
        if (!IsDescendantOf(packDirectory, _packsRoot)) return null;
        var indexPath = Path.Combine(packDirectory, IndexFileName);
        if (!File.Exists(indexPath) || new FileInfo(indexPath).Length is <= 0 or > MaximumInstalledIndexBytes)
            return null;

        using var stream = File.OpenRead(indexPath);
        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;
        var header = root.Deserialize(IconJsonContext.Default.AccountIndexHeader)
            ?? throw new InvalidDataException("The installed icon index is invalid.");
        // Older provider formats retain their existing matching and validation path.
        if (header.Format != BrandIconPackFormat.OtpHarbor)
            return root.Deserialize(IconJsonContext.Default.BrandIndex);

        var metadata = header.Metadata
            ?? throw new InvalidDataException("The installed icon provenance is missing.");
        if (metadata.IssuerAliases is not { Count: > 0 })
            throw new InvalidDataException("The installed issuer index is missing.");
        var requested = issuers.Select(OtpHarborIssuerNormalizer.Normalize).ToHashSet(StringComparer.Ordinal);
        var selectedIds = _accountBrandIds.Values.ToHashSet(StringComparer.Ordinal);
        var aliases = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var alias in metadata.IssuerAliases)
        {
            if (string.IsNullOrWhiteSpace(alias.Key)
                || !string.Equals(OtpHarborIssuerNormalizer.Normalize(alias.Key), alias.Key, StringComparison.Ordinal)
                || !aliases.TryAdd(alias.Key, alias.BrandId))
                throw new InvalidDataException("The installed issuer index is invalid.");
            if (requested.Contains(alias.Key)) selectedIds.Add(alias.BrandId);
        }

        var brands = new List<IndexBrand>();
        var allIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var element in root.GetProperty(nameof(BrandIndex.Brands)).EnumerateArray())
        {
            var id = element.GetProperty(nameof(IndexBrand.Id)).GetString();
            if (id is null || !OtpHarborCanonicalIdRegex().IsMatch(id) || !allIds.Add(id))
                throw new InvalidDataException("The installed icon index contains invalid brand IDs.");
            if (selectedIds.Contains(id))
                brands.Add(element.Deserialize(IconJsonContext.Default.IndexBrand)
                    ?? throw new InvalidDataException("The installed account icon is invalid."));
        }
        if (aliases.Values.Any(id => !allIds.Contains(id)))
            throw new InvalidDataException("The installed issuer index references a missing brand.");

        // Validate each selected brand with the same provenance, alias and asset checks
        // as the complete catalogue, without materializing unused brand records.
        return new BrandIndex(header.Version, brands, header.ProviderId, header.ProviderDisplayName,
            header.Format, metadata with
            {
                IssuerAliases = metadata.IssuerAliases.Where(alias => selectedIds.Contains(alias.BrandId)).ToArray()
            });
    }

    private sealed record AccountIndexHeader(
        string Version,
        string ProviderId,
        string ProviderDisplayName,
        BrandIconPackFormat Format,
        IconPackMetadata? Metadata);
}
