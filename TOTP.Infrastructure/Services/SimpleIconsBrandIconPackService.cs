using System.Collections.Concurrent;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using FluentResults;
using Microsoft.Extensions.Logging;
using TOTP.Core.Services.Interfaces;
using TOTP.Core.Services.Models;
using TOTP.Infrastructure.Branding;

namespace TOTP.Infrastructure.Services;

public sealed partial class SimpleIconsBrandIconPackService : IBrandIconPackService
{
    private const int MaximumArchiveBytes = 25 * 1024 * 1024;
    private const long MaximumExpandedBytes = 64L * 1024 * 1024;
    private const int MaximumEntries = 10_000;
    private const int MaximumMetadataBytes = 8 * 1024 * 1024;
    private const int MaximumSvgBytes = 64 * 1024;
    private const int MaximumGenericIcons = 5_000;
    private const int MaximumGenericNotices = 64;
    private const int MaximumNoticeBytes = 128 * 1024;
    private const int MaximumAliasesPerBrand = 32;
    private const int PackFormatVersion = 3;
    private const string StorageDirectoryName = "BrandIcons";
    private const string CurrentPackFileName = "current.json";
    private const string DisplaySettingsFileName = "display-settings.json";
    private const string AccountBrandSettingsFileName = "account-brand-settings.json";
    private const string IndexFileName = "brand-index.json";

    private readonly string _storageRoot;
    private readonly string _packsRoot;
    private readonly string _currentPackPath;
    private readonly string _displaySettingsPath;
    private readonly string _accountBrandSettingsPath;
    private readonly IPlatformFileSecurity _fileSecurity;
    private readonly ILogger<SimpleIconsBrandIconPackService> _logger;
    private readonly IssuerAliasResolver _issuerAliases;
    private readonly SemaphoreSlim _importLock = new(1, 1);
    private readonly ConcurrentDictionary<string, string> _pathDataCache =
        new(StringComparer.Ordinal);
    private CatalogSnapshot _catalog = CatalogSnapshot.Empty;
    private IReadOnlyDictionary<Guid, string> _accountBrandIds =
        new Dictionary<Guid, string>();
    private bool _showIssuerLogo = true;

    public SimpleIconsBrandIconPackService(
        IPlatformApplicationPaths applicationPaths,
        IPlatformFileSecurity fileSecurity,
        ILogger<SimpleIconsBrandIconPackService> logger)
    {
        ArgumentNullException.ThrowIfNull(applicationPaths);
        _fileSecurity = fileSecurity ?? throw new ArgumentNullException(nameof(fileSecurity));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _issuerAliases = LoadIssuerAliases(_logger);
        _storageRoot = Path.Combine(applicationPaths.ApplicationDataDirectory, StorageDirectoryName);
        _packsRoot = Path.Combine(_storageRoot, "packs");
        _currentPackPath = Path.Combine(_storageRoot, CurrentPackFileName);
        _displaySettingsPath = Path.Combine(_storageRoot, DisplaySettingsFileName);
        _accountBrandSettingsPath = Path.Combine(_storageRoot, AccountBrandSettingsFileName);
        TryLoadDisplaySettings();
        TryLoadAccountBrandSettings();
        TryLoadInstalledCatalog();
    }

    public event EventHandler? CatalogChanged;

    public BrandIconPackStatus Status => Volatile.Read(ref _catalog).Status;

    public bool ShowIssuerLogo => Volatile.Read(ref _showIssuerLogo);

    public IReadOnlyList<BrandDefinition> AvailableBrands =>
        Volatile.Read(ref _catalog).Brands;

    public string? GetAccountBrandId(Guid accountId)
    {
        if (accountId == Guid.Empty) return null;
        var mappings = Volatile.Read(ref _accountBrandIds);
        return mappings.TryGetValue(accountId, out var brandId) ? brandId : null;
    }

    public BrandDefinition? Resolve(string? issuer, string? explicitBrandId = null)
    {
        var catalog = Volatile.Read(ref _catalog);
        if (!string.IsNullOrWhiteSpace(explicitBrandId)
            && catalog.ById.TryGetValue(explicitBrandId.Trim(), out var explicitlySelected))
        {
            return explicitlySelected;
        }

        if (string.IsNullOrWhiteSpace(issuer)) return null;
        var trimmed = issuer.Trim();
        if (catalog.ByAlias.TryGetValue(trimmed, out var exact)) return exact;

        var normalized = IssuerAliasResolver.Normalize(trimmed);
        if (TryResolveNormalized(catalog, normalized, out var normalizedMatch)) return normalizedMatch;

        return null;
    }

    public BrandDefinition? ResolveAccount(
        string? issuer,
        string? accountName,
        string? explicitBrandId = null)
    {
        var issuerMatch = Resolve(issuer, explicitBrandId);
        if (issuerMatch is not null || !string.IsNullOrWhiteSpace(issuer)) return issuerMatch;
        return Resolve(accountName, explicitBrandId);
    }

    private bool TryResolveNormalized(
        CatalogSnapshot catalog,
        string normalized,
        out BrandDefinition? definition)
    {
        if (catalog.ByNormalizedAlias.TryGetValue(normalized, out definition)) return true;
        if (_issuerAliases.TryResolve(normalized, out var exactKnownId))
            return catalog.ById.TryGetValue(exactKnownId, out definition);

        // Some imported labels contain a human qualifier (for example
        // "GitHub test"). Only accept a known alias at the beginning and
        // prefer the longest match; this avoids broad fuzzy matching.
        var tokens = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var length = tokens.Length - 1; length > 0; length--)
        {
            var prefix = string.Join(' ', tokens, 0, length);
            if (prefix.Length < 3) continue;
            if (catalog.ByNormalizedAlias.TryGetValue(prefix, out definition)) return true;
            if (_issuerAliases.TryResolve(prefix, out var knownId))
                return catalog.ById.TryGetValue(knownId, out definition);
        }

        definition = null;
        return false;
    }

    private static IssuerAliasResolver LoadIssuerAliases(
        ILogger<SimpleIconsBrandIconPackService> logger)
    {
        try
        {
            return IssuerAliasResolver.LoadDefault();
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException)
        {
            logger.LogWarning("The built-in issuer resolver database could not be loaded.");
            return IssuerAliasResolver.Empty;
        }
    }

    public bool TryGetIconPathData(string brandId, out string pathData)
    {
        pathData = string.Empty;
        if (string.IsNullOrWhiteSpace(brandId)) return false;
        var catalog = Volatile.Read(ref _catalog);
        if (!catalog.ById.TryGetValue(brandId, out var brand)) return false;
        if (_pathDataCache.TryGetValue(brand.Id, out pathData!)) return true;

        try
        {
            var iconPath = Path.Combine(catalog.PackDirectory!, "icons", brand.IconFileName);
            if (!IsDescendantOf(iconPath, catalog.PackDirectory!)) return false;
            using var stream = new FileStream(iconPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length is <= 0 or > MaximumSvgBytes) return false;
            using var reader = XmlReader.Create(stream, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = MaximumSvgBytes
            });
            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "path") continue;
                var data = reader.GetAttribute("d");
                if (string.IsNullOrWhiteSpace(data) || data.Length > MaximumSvgBytes) return false;
                pathData = _pathDataCache.GetOrAdd(brand.Id, data);
                return true;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or XmlException)
        {
            _logger.LogWarning("A locally installed brand icon could not be read.");
        }
        return false;
    }

    public async Task<Result<BrandIconPackImportResult>> ImportAsync(
        Stream zipStream,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(zipStream);
        await _importLock.WaitAsync(cancellationToken);
        string? stagingDirectory = null;
        string? temporaryArchive = null;
        try
        {
            Directory.CreateDirectory(_storageRoot);
            Directory.CreateDirectory(_packsRoot);
            _fileSecurity.RestrictDirectoryToCurrentUser(_storageRoot);
            _fileSecurity.RestrictDirectoryToCurrentUser(_packsRoot);

            temporaryArchive = Path.Combine(_storageRoot, $"import-{Guid.NewGuid():N}.tmp");
            var archiveHash = await CopyBoundedAndHashAsync(zipStream, temporaryArchive, cancellationToken);
            _fileSecurity.RestrictFileToCurrentUser(temporaryArchive);

            using var archive = ZipFile.OpenRead(temporaryArchive);
            ValidateArchiveShape(archive);
            var metadataEntry = FindMetadataEntry(archive);
            var parsed = metadataEntry is null
                ? ParseFilenameIndexedPack(archive)
                : await ParseSimpleIconsMetadataAsync(metadataEntry, cancellationToken);
            var packageVersion = parsed.Version;
            var packPrefix = parsed.Format == BrandIconPackFormat.SimpleIcons
                ? "simple-icons"
                : "local-icons";
            var packId = $"{packPrefix}-{SanitizeVersion(packageVersion)}-v{PackFormatVersion}-{archiveHash[..12].ToLowerInvariant()}";
            stagingDirectory = Path.Combine(_packsRoot, $".{packId}-{Guid.NewGuid():N}.staging");
            Directory.CreateDirectory(stagingDirectory);
            _fileSecurity.RestrictDirectoryToCurrentUser(stagingDirectory);
            var iconsDirectory = Path.Combine(stagingDirectory, "icons");
            Directory.CreateDirectory(iconsDirectory);
            _fileSecurity.RestrictDirectoryToCurrentUser(iconsDirectory);

            var imported = new List<IndexBrand>(parsed.Brands.Count);
            foreach (var sourceBrand in parsed.Brands.OrderBy(value => value.Id, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var iconEntry = archive.GetEntry(sourceBrand.ArchiveEntryName);
                if (iconEntry is null || iconEntry.Length is <= 0 or > MaximumSvgBytes) continue;
                var destination = Path.Combine(iconsDirectory, $"{sourceBrand.Id}.svg");
                await ExtractValidatedSvgAsync(iconEntry, destination, cancellationToken);
                imported.Add(new IndexBrand(
                    sourceBrand.Id,
                    sourceBrand.DisplayName,
                    sourceBrand.BackgroundColor,
                    $"{sourceBrand.Id}.svg",
                    sourceBrand.Aliases));
            }
            if (imported.Count == 0) return Result.Fail("The archive did not contain usable brand SVG files.");

            if (parsed.Format == BrandIconPackFormat.SimpleIcons)
            {
                var archivePrefix = metadataEntry!.FullName[..^"data/simple-icons.json".Length];
                await CopyNoticeIfPresentAsync(archive, archivePrefix, "LICENSE.md", stagingDirectory, cancellationToken);
                await CopyNoticeIfPresentAsync(archive, archivePrefix, "DISCLAIMER.md", stagingDirectory, cancellationToken);
            }
            else
            {
                await CopyGenericNoticesAsync(archive, stagingDirectory, cancellationToken);
            }
            var index = new BrandIndex(packageVersion, imported);
            var indexPath = Path.Combine(stagingDirectory, IndexFileName);
            await WriteJsonAsync(indexPath, index, cancellationToken);
            _fileSecurity.RestrictFileToCurrentUser(indexPath);

            var finalDirectory = Path.Combine(_packsRoot, packId);
            if (!Directory.Exists(finalDirectory))
            {
                Directory.Move(stagingDirectory, finalDirectory);
                stagingDirectory = null;
            }
            var pointerPath = Path.Combine(_storageRoot, $"{CurrentPackFileName}.{Guid.NewGuid():N}.tmp");
            await WriteJsonAsync(pointerPath, new CurrentPack(packId), cancellationToken);
            _fileSecurity.RestrictFileToCurrentUser(pointerPath);
            File.Move(pointerPath, _currentPackPath, overwrite: true);
            _fileSecurity.RestrictFileToCurrentUser(_currentPackPath);

            var snapshot = CreateSnapshot(finalDirectory, index);
            Volatile.Write(ref _catalog, snapshot);
            _pathDataCache.Clear();
            CatalogChanged?.Invoke(this, EventArgs.Empty);
            return Result.Ok(new BrandIconPackImportResult(
                packageVersion,
                imported.Count,
                parsed.Format));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "A local brand icon pack could not be imported.");
            return Result.Fail("The selected brand icon archive is invalid or could not be installed safely.");
        }
        finally
        {
            TryDeleteFile(temporaryArchive);
            TryDeleteDirectory(stagingDirectory);
            _importLock.Release();
        }
    }

    public async Task<Result> ResetAsync(CancellationToken cancellationToken = default)
    {
        await _importLock.WaitAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            // These paths are fixed application-owned locations; never accept a
            // caller-supplied path for this destructive operation.
            if (!IsDescendantOf(_packsRoot, _storageRoot)
                || !string.Equals(Path.GetFileName(_packsRoot), "packs", StringComparison.Ordinal))
            {
                return Result.Fail("The local brand-icon storage location is invalid.");
            }

            await Task.Run(RemoveImportedPackFiles, cancellationToken);
            _pathDataCache.Clear();
            Volatile.Write(ref _catalog, CatalogSnapshot.Empty);
            CatalogChanged?.Invoke(this, EventArgs.Empty);
            return Result.Ok();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "The local brand icon pack could not be removed.");
            return Result.Fail("The local brand-icon pack could not be removed.");
        }
        finally
        {
            _importLock.Release();
        }
    }

    public async Task<Result> SetShowIssuerLogoAsync(
        bool showIssuerLogo,
        CancellationToken cancellationToken = default)
    {
        if (ShowIssuerLogo == showIssuerLogo) return Result.Ok();

        await _importLock.WaitAsync(cancellationToken);
        string? temporaryPath = null;
        try
        {
            if (ShowIssuerLogo == showIssuerLogo) return Result.Ok();
            Directory.CreateDirectory(_storageRoot);
            _fileSecurity.RestrictDirectoryToCurrentUser(_storageRoot);
            temporaryPath = Path.Combine(
                _storageRoot,
                $"{DisplaySettingsFileName}.{Guid.NewGuid():N}.tmp");
            await WriteJsonAsync(
                temporaryPath,
                new BrandDisplaySettings(showIssuerLogo),
                cancellationToken);
            _fileSecurity.RestrictFileToCurrentUser(temporaryPath);
            File.Move(temporaryPath, _displaySettingsPath, overwrite: true);
            temporaryPath = null;
            _fileSecurity.RestrictFileToCurrentUser(_displaySettingsPath);
            Volatile.Write(ref _showIssuerLogo, showIssuerLogo);
            CatalogChanged?.Invoke(this, EventArgs.Empty);
            return Result.Ok();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "The local brand-icon display preference could not be saved.");
            return Result.Fail("The brand-icon display preference could not be saved.");
        }
        finally
        {
            TryDeleteFile(temporaryPath);
            _importLock.Release();
        }
    }

    public async Task<Result> SetAccountBrandIdAsync(
        Guid accountId,
        string? brandId,
        CancellationToken cancellationToken = default)
    {
        if (accountId == Guid.Empty)
            return Result.Fail("The account identifier is invalid.");
        var normalizedBrandId = string.IsNullOrWhiteSpace(brandId)
            ? null
            : brandId.Trim().ToLowerInvariant();
        await _importLock.WaitAsync(cancellationToken);
        string? temporaryPath = null;
        try
        {
            if (normalizedBrandId is not null
                && !Volatile.Read(ref _catalog).ById.ContainsKey(normalizedBrandId))
                return Result.Fail("The selected local brand icon is unavailable.");

            var updated = new Dictionary<Guid, string>(
                Volatile.Read(ref _accountBrandIds));
            var changed = normalizedBrandId is null
                ? updated.Remove(accountId)
                : !updated.TryGetValue(accountId, out var current)
                    || !string.Equals(current, normalizedBrandId, StringComparison.Ordinal);
            if (normalizedBrandId is not null) updated[accountId] = normalizedBrandId;
            if (!changed) return Result.Ok();

            Directory.CreateDirectory(_storageRoot);
            _fileSecurity.RestrictDirectoryToCurrentUser(_storageRoot);
            temporaryPath = Path.Combine(
                _storageRoot,
                $"{AccountBrandSettingsFileName}.{Guid.NewGuid():N}.tmp");
            var settings = new AccountBrandSettings(
                1,
                updated.OrderBy(pair => pair.Key)
                    .Select(pair => new AccountBrandOverride(pair.Key, pair.Value))
                    .ToList());
            await WriteJsonAsync(temporaryPath, settings, cancellationToken);
            _fileSecurity.RestrictFileToCurrentUser(temporaryPath);
            File.Move(temporaryPath, _accountBrandSettingsPath, overwrite: true);
            temporaryPath = null;
            _fileSecurity.RestrictFileToCurrentUser(_accountBrandSettingsPath);
            Volatile.Write(ref _accountBrandIds, updated);
            CatalogChanged?.Invoke(this, EventArgs.Empty);
            return Result.Ok();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "A local per-account brand-icon preference could not be saved.");
            return Result.Fail("The per-account brand-icon preference could not be saved.");
        }
        finally
        {
            TryDeleteFile(temporaryPath);
            _importLock.Release();
        }
    }

    private void RemoveImportedPackFiles()
    {
        if (Directory.Exists(_packsRoot))
            Directory.Delete(_packsRoot, recursive: true);
        if (File.Exists(_currentPackPath))
            File.Delete(_currentPackPath);
        Directory.CreateDirectory(_packsRoot);
        _fileSecurity.RestrictDirectoryToCurrentUser(_packsRoot);
    }

    private void TryLoadDisplaySettings()
    {
        try
        {
            if (!File.Exists(_displaySettingsPath)) return;
            var file = new FileInfo(_displaySettingsPath);
            if (file.Length is <= 0 or > 4096) return;
            var settings = JsonSerializer.Deserialize<BrandDisplaySettings>(
                File.ReadAllText(_displaySettingsPath));
            if (settings is not null)
                Volatile.Write(ref _showIssuerLogo, settings.ShowIssuerLogo);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _logger.LogWarning("The local brand-icon display preference could not be loaded.");
        }
    }

    private void TryLoadAccountBrandSettings()
    {
        try
        {
            if (!File.Exists(_accountBrandSettingsPath)) return;
            var file = new FileInfo(_accountBrandSettingsPath);
            if (file.Length is <= 0 or > 1024 * 1024) return;
            var settings = JsonSerializer.Deserialize<AccountBrandSettings>(
                File.ReadAllText(_accountBrandSettingsPath));
            if (settings is null
                || settings.Version != 1
                || settings.Overrides is null
                || settings.Overrides.Count > MaximumEntries) return;
            var mappings = new Dictionary<Guid, string>();
            foreach (var item in settings.Overrides)
            {
                if (item is null
                    || item.AccountId == Guid.Empty
                    || string.IsNullOrWhiteSpace(item.BrandId)
                    || !SafeSlugRegex().IsMatch(item.BrandId)
                    || !mappings.TryAdd(item.AccountId, item.BrandId)) return;
            }
            Volatile.Write(ref _accountBrandIds, mappings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _logger.LogWarning("The local per-account brand-icon preferences could not be loaded.");
        }
    }

    private void TryLoadInstalledCatalog()
    {
        try
        {
            if (!File.Exists(_currentPackPath)) return;
            var pointer = JsonSerializer.Deserialize<CurrentPack>(File.ReadAllText(_currentPackPath));
            if (pointer is null || !SafePackIdRegex().IsMatch(pointer.PackId)) return;
            var packDirectory = Path.Combine(_packsRoot, pointer.PackId);
            if (!IsDescendantOf(packDirectory, _packsRoot)) return;
            var indexPath = Path.Combine(packDirectory, IndexFileName);
            var index = JsonSerializer.Deserialize<BrandIndex>(File.ReadAllText(indexPath));
            if (index is null) return;
            Volatile.Write(ref _catalog, CreateSnapshot(packDirectory, index));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _logger.LogWarning("The installed local brand icon index could not be loaded.");
        }
    }

    private static CatalogSnapshot CreateSnapshot(string packDirectory, BrandIndex index)
    {
        var byId = new Dictionary<string, BrandDefinition>(StringComparer.OrdinalIgnoreCase);
        var byAlias = new Dictionary<string, BrandDefinition>(StringComparer.OrdinalIgnoreCase);
        var byNormalizedAlias = new Dictionary<string, BrandDefinition>(StringComparer.Ordinal);
        var ambiguousAliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ambiguousNormalizedAliases = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in index.Brands)
        {
            if (!SafeSlugRegex().IsMatch(item.Id)
                || !SafeSvgNameRegex().IsMatch(item.IconFileName)
                || !IsSafeDisplayText(item.DisplayName, 256)
                || item.Aliases is null) continue;
            var definition = new BrandDefinition(item.Id, item.DisplayName, item.BackgroundColor, item.IconFileName);
            if (!byId.TryAdd(item.Id, definition)) continue;
            foreach (var alias in item.Aliases.Append(item.Id).Append(item.DisplayName))
            {
                if (string.IsNullOrWhiteSpace(alias)) continue;
                AddUnambiguousAlias(byAlias, ambiguousAliases, alias.Trim(), definition);
                AddUnambiguousAlias(
                    byNormalizedAlias,
                    ambiguousNormalizedAliases,
                    IssuerAliasResolver.Normalize(alias),
                    definition);
            }
        }
        return new CatalogSnapshot(
            packDirectory,
            byId,
            byAlias,
            byNormalizedAlias,
            byId.Values.OrderBy(value => value.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(value => value.Id, StringComparer.Ordinal)
                .ToArray(),
            new BrandIconPackStatus(true, index.Version, byId.Count));
    }

    private static void AddUnambiguousAlias(
        IDictionary<string, BrandDefinition> aliases,
        ISet<string> ambiguousAliases,
        string alias,
        BrandDefinition definition)
    {
        if (alias.Length == 0 || ambiguousAliases.Contains(alias)) return;
        if (!aliases.TryGetValue(alias, out var existing))
        {
            aliases.Add(alias, definition);
            return;
        }
        if (string.Equals(existing.Id, definition.Id, StringComparison.OrdinalIgnoreCase)) return;
        aliases.Remove(alias);
        ambiguousAliases.Add(alias);
    }

    private static async Task<string> CopyBoundedAndHashAsync(Stream source, string destination, CancellationToken token)
    {
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        var total = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, token)) > 0)
        {
            total += read;
            if (total > MaximumArchiveBytes) throw new InvalidDataException("Brand archive is too large.");
            hash.AppendData(buffer, 0, read);
            await output.WriteAsync(buffer.AsMemory(0, read), token);
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static void ValidateArchiveShape(ZipArchive archive)
    {
        if (archive.Entries.Count is 0 or > MaximumEntries) throw new InvalidDataException("Unexpected archive entry count.");
        long expanded = 0;
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
        {
            var normalized = entry.FullName.Replace('\\', '/');
            if (!names.Add(normalized)) throw new InvalidDataException("Duplicate archive entry.");
            expanded = checked(expanded + entry.Length);
            if (expanded > MaximumExpandedBytes) throw new InvalidDataException("Expanded archive is too large.");
            if (normalized.StartsWith("/", StringComparison.Ordinal)
                || normalized.Split('/').Any(segment => segment is ".." or "."))
                throw new InvalidDataException("Unsafe archive path.");
        }
    }

    private static ZipArchiveEntry? FindMetadataEntry(ZipArchive archive)
    {
        var matches = archive.Entries.Where(entry =>
            entry.FullName.Replace('\\', '/').EndsWith(
                "data/simple-icons.json",
                StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length == 0) return null;
        if (matches.Length != 1 || matches[0].Length is <= 0 or > MaximumMetadataBytes)
            throw new InvalidDataException("Simple Icons metadata is invalid or ambiguous.");
        return matches[0];
    }

    private static async Task<ParsedMetadata> ParseSimpleIconsMetadataAsync(
        ZipArchiveEntry entry,
        CancellationToken token)
    {
        await using var stream = entry.Open();
        using var document = await JsonDocument.ParseAsync(stream, new JsonDocumentOptions { MaxDepth = 16 }, token);
        if (document.RootElement.ValueKind != JsonValueKind.Array) throw new InvalidDataException("Unexpected Simple Icons metadata.");
        var archivePrefix = entry.FullName[..^"data/simple-icons.json".Length];
        var brands = new List<SourceBrand>();
        foreach (var icon in document.RootElement.EnumerateArray())
        {
            if (!icon.TryGetProperty("title", out var titleElement)
                || !icon.TryGetProperty("hex", out var hexElement)) continue;
            var title = titleElement.GetString();
            var hex = hexElement.GetString();
            if (hex is null
                || !IsSafeDisplayText(title, 256)
                || hex.Length is not 6
                ) continue;
            var safeTitle = title!;
            var slug = icon.TryGetProperty("slug", out var slugElement)
                ? slugElement.GetString()
                : TitleToSlug(safeTitle);
            if (slug is null
                || !SafeSlugRegex().IsMatch(slug) || !HexRegex().IsMatch(hex)) continue;
            var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { slug, safeTitle };
            if (icon.TryGetProperty("aliases", out var aliasObject))
            {
                AddAliases(aliasObject, "aka", aliases);
                AddAliases(aliasObject, "old", aliases);
            }
            brands.Add(new SourceBrand(
                slug,
                safeTitle,
                $"#{hex.ToUpperInvariant()}",
                aliases.OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                    .Take(MaximumAliasesPerBrand)
                    .ToArray(),
                $"{archivePrefix}icons/{slug}.svg"));
        }
        var version = ReadVersionFromArchivePrefix(entry.FullName) ?? "unknown";
        return new ParsedMetadata(version, BrandIconPackFormat.SimpleIcons, brands);
    }

    private static ParsedMetadata ParseFilenameIndexedPack(ZipArchive archive)
    {
        var brands = new List<SourceBrand>();
        var brandIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
        {
            if (entry.Name.Length == 0
                || !entry.Name.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)) continue;
            if (entry.Length is <= 0 or > MaximumSvgBytes)
                throw new InvalidDataException("A filename-indexed SVG has an invalid size.");
            if (!SafeGenericSvgNameRegex().IsMatch(entry.Name))
                throw new InvalidDataException("A filename-indexed SVG has an unsafe brand id.");
            var sourceName = Path.GetFileNameWithoutExtension(entry.Name);
            var brandId = sourceName.ToLowerInvariant();
            if (!brandIds.Add(brandId))
                throw new InvalidDataException("A filename-indexed brand id is duplicated or ambiguous.");
            if (brands.Count >= MaximumGenericIcons)
                throw new InvalidDataException("The filename-indexed icon count exceeds the supported limit.");
            brands.Add(new SourceBrand(
                brandId,
                sourceName,
                "#334155",
                [brandId, sourceName],
                entry.FullName));
        }
        if (brands.Count == 0)
            throw new InvalidDataException("The archive contains no filename-indexed SVG files.");
        return new ParsedMetadata("filename-indexed", BrandIconPackFormat.FilenameIndexed, brands);
    }

    private static void AddAliases(JsonElement aliasObject, string propertyName, HashSet<string> target)
    {
        if (!aliasObject.TryGetProperty(propertyName, out var aliases) || aliases.ValueKind != JsonValueKind.Array) return;
        foreach (var alias in aliases.EnumerateArray())
        {
            string? value = alias.ValueKind == JsonValueKind.String ? alias.GetString()
                : alias.ValueKind == JsonValueKind.Object && alias.TryGetProperty("title", out var title) ? title.GetString()
                : null;
            if (IsSafeDisplayText(value, 128)) target.Add(value!);
        }
    }

    private static bool IsSafeDisplayText(string? value, int maximumLength) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length <= maximumLength
        && !value.Any(char.IsControl);

    private static async Task ExtractValidatedSvgAsync(ZipArchiveEntry entry, string destination, CancellationToken token)
    {
        await using var input = entry.Open();
        using var memory = new MemoryStream((int)entry.Length);
        await input.CopyToAsync(memory, token);
        if (memory.Length != entry.Length || memory.Length > MaximumSvgBytes) throw new InvalidDataException("Invalid SVG size.");
        memory.Position = 0;
        using (var reader = XmlReader.Create(memory, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaximumSvgBytes
        }))
        {
            var foundSvg = false;
            var foundPath = false;
            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element) continue;
                foundSvg |= reader.LocalName == "svg";
                foundPath |= reader.LocalName == "path" && !string.IsNullOrWhiteSpace(reader.GetAttribute("d"));
            }
            if (!foundSvg || !foundPath) throw new InvalidDataException("Unsupported SVG content.");
        }
        await File.WriteAllBytesAsync(destination, memory.ToArray(), token);
    }

    private static async Task CopyNoticeIfPresentAsync(ZipArchive archive, string prefix, string name, string destinationDirectory, CancellationToken token)
    {
        var entry = archive.GetEntry(prefix + name);
        if (entry is null || entry.Length <= 0 || entry.Length > MaximumNoticeBytes) return;
        await using var input = entry.Open();
        await using var output = new FileStream(Path.Combine(destinationDirectory, name), FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
        await input.CopyToAsync(output, token);
    }

    private static async Task CopyGenericNoticesAsync(
        ZipArchive archive,
        string destinationDirectory,
        CancellationToken token)
    {
        var notices = archive.Entries.Where(entry =>
            entry.Name.Length > 0
            && GenericNoticeNameRegex().IsMatch(entry.Name)).ToArray();
        if (notices.Length == 0) return;
        if (notices.Length > MaximumGenericNotices
            || notices.Any(entry => entry.Length is <= 0 or > MaximumNoticeBytes))
            throw new InvalidDataException("The filename-indexed pack contains invalid notice files.");

        var noticesDirectory = Path.Combine(destinationDirectory, "notices");
        Directory.CreateDirectory(noticesDirectory);
        for (var index = 0; index < notices.Length; index++)
        {
            token.ThrowIfCancellationRequested();
            var entry = notices[index];
            var destination = Path.Combine(
                noticesDirectory,
                $"{index + 1:D2}-{entry.Name.ToUpperInvariant()}");
            await using var input = entry.Open();
            await using var output = new FileStream(
                destination,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                81920,
                true);
            await input.CopyToAsync(output, token);
        }
    }

    private static async Task WriteJsonAsync<T>(string path, T value, CancellationToken token)
    {
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
        await JsonSerializer.SerializeAsync(stream, value, cancellationToken: token);
        await stream.FlushAsync(token);
    }

    // Mirrors Simple Icons' titleToSlug implementation so metadata entries that
    // omit an explicit slug resolve to the same SVG filename as the source pack.
    private static string TitleToSlug(string title)
    {
        var lowered = title.ToLowerInvariant();
        var replaced = new StringBuilder(lowered.Length);
        foreach (var character in lowered)
        {
            replaced.Append(character switch
            {
                '+' => "plus",
                '.' => "dot",
                '&' => "and",
                'đ' => "d",
                'ħ' => "h",
                'ı' => "i",
                'ĸ' => "k",
                'ŀ' => "l",
                'ł' => "l",
                'ß' => "ss",
                'ŧ' => "t",
                'ø' => "o",
                _ => character.ToString()
            });
        }

        var normalized = replaced.ToString().Normalize(NormalizationForm.FormD);
        var slug = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            if (character is >= 'a' and <= 'z' or >= '0' and <= '9') slug.Append(character);
        }
        return slug.ToString();
    }

    private static string? ReadVersionFromArchivePrefix(string fullName)
    {
        var match = VersionRegex().Match(fullName.Replace('\\', '/'));
        return match.Success ? match.Groups[1].Value : null;
    }

    private static string SanitizeVersion(string value) =>
        Regex.Replace(value, "[^0-9A-Za-z.-]", "-").Trim('-');

    private static bool IsDescendantOf(string path, string directory)
    {
        var fullPath = Path.GetFullPath(path);
        var fullDirectory = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(fullDirectory, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    private static void TryDeleteFile(string? path)
    {
        try { if (path is not null && File.Exists(path)) File.Delete(path); }
        catch { }
    }

    private static void TryDeleteDirectory(string? path)
    {
        try
        {
            if (path is not null && Directory.Exists(path)
                && Path.GetFileName(path).EndsWith(".staging", StringComparison.Ordinal))
                Directory.Delete(path, recursive: true);
        }
        catch { }
    }

    [GeneratedRegex("^[a-z0-9_]+$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeSlugRegex();

    [GeneratedRegex("^[a-z0-9_]+\\.svg$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeSvgNameRegex();

    [GeneratedRegex("^[A-Za-z0-9_]+\\.svg$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeGenericSvgNameRegex();

    [GeneratedRegex("^(?:LICENSE|LICENCE|COPYING|NOTICE|DISCLAIMER)(?:\\.(?:MD|TXT))?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GenericNoticeNameRegex();

    [GeneratedRegex("^[0-9A-Fa-f]{6}$", RegexOptions.CultureInvariant)]
    private static partial Regex HexRegex();

    [GeneratedRegex("^(?:simple-icons|local-icons)-[0-9A-Za-z.-]+-[0-9a-f]{12}$", RegexOptions.CultureInvariant)]
    private static partial Regex SafePackIdRegex();

    [GeneratedRegex(@"(?:^|/)simple-icons-([0-9]+(?:\.[0-9]+){1,3}(?:-[0-9A-Za-z.-]+)?)/data/simple-icons\.json$", RegexOptions.CultureInvariant)]
    private static partial Regex VersionRegex();

    private sealed record CurrentPack(string PackId);
    private sealed record BrandDisplaySettings(bool ShowIssuerLogo);
    private sealed record AccountBrandSettings(int Version, List<AccountBrandOverride> Overrides);
    private sealed record AccountBrandOverride(Guid AccountId, string BrandId);
    private sealed record BrandIndex(string Version, List<IndexBrand> Brands);
    private sealed record IndexBrand(string Id, string DisplayName, string BackgroundColor, string IconFileName, string[] Aliases);
    private sealed record SourceBrand(
        string Id,
        string DisplayName,
        string BackgroundColor,
        string[] Aliases,
        string ArchiveEntryName);
    private sealed record ParsedMetadata(
        string Version,
        BrandIconPackFormat Format,
        List<SourceBrand> Brands);
    private sealed record CatalogSnapshot(
        string? PackDirectory,
        IReadOnlyDictionary<string, BrandDefinition> ById,
        IReadOnlyDictionary<string, BrandDefinition> ByAlias,
        IReadOnlyDictionary<string, BrandDefinition> ByNormalizedAlias,
        IReadOnlyList<BrandDefinition> Brands,
        BrandIconPackStatus Status)
    {
        internal static CatalogSnapshot Empty { get; } = new(
            null,
            new Dictionary<string, BrandDefinition>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, BrandDefinition>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, BrandDefinition>(StringComparer.Ordinal),
            [],
            new BrandIconPackStatus(false, null, 0));
    }
}
