using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using FluentResults;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TOTP.Core.Icons;
using TOTP.Core.Services.Interfaces;
using TOTP.Core.Services.Models;
using TOTP.Infrastructure.Branding;
using TOTP.Infrastructure.Icons;

namespace TOTP.Infrastructure.Services;

public sealed partial class SimpleIconsBrandIconPackService : IBrandIconPackService
{
    private const int MaximumArchiveBytes = 25 * 1024 * 1024;
    private const int MaximumEntries = 10_000;
    private const int MaximumSvgBytes = 64 * 1024;
    private const int MaximumNoticeBytes = 128 * 1024;
    private const int MaximumAliasesPerBrand = 32;
    private const int PackFormatVersion = 4;
    private const string StorageDirectoryName = "BrandIcons";
    private const string CurrentPackFileName = "current.json";
    private const string DisplaySettingsFileName = "display-settings.json";
    private const string AccountBrandSettingsFileName = "account-brand-settings.json";
    private const string CustomIconIndexFileName = "custom-icon-index.json";
    private const string IndexFileName = "brand-index.json";

    private readonly string _storageRoot;
    private readonly string _packsRoot;
    private readonly string _currentPackPath;
    private readonly string _displaySettingsPath;
    private readonly string _accountBrandSettingsPath;
    private readonly string _customIconsRoot;
    private readonly string _customIconIndexPath;
    private readonly IPlatformFileSecurity _fileSecurity;
    private readonly ILogger<SimpleIconsBrandIconPackService> _logger;
    private readonly IIconPackImporterResolver _importerResolver;
    private readonly ICustomIconImporter _customIconImporter;
    private readonly IssuerAliasResolver _issuerAliases;
    private readonly SemaphoreSlim _importLock = new(1, 1);
    private readonly ConcurrentDictionary<string, string> _pathDataCache =
        new(StringComparer.Ordinal);
    private CatalogSnapshot _catalog = CatalogSnapshot.Empty;
    private IReadOnlyDictionary<Guid, string> _accountBrandIds =
        new Dictionary<Guid, string>();
    private IReadOnlyDictionary<string, BrandDefinition> _customIcons =
        new Dictionary<string, BrandDefinition>(StringComparer.OrdinalIgnoreCase);
    private bool _showIssuerLogo = true;

    public SimpleIconsBrandIconPackService(
        IPlatformApplicationPaths applicationPaths,
        IPlatformFileSecurity fileSecurity,
        ILogger<SimpleIconsBrandIconPackService> logger)
        : this(
            applicationPaths,
            fileSecurity,
            logger,
            new IconPackImporterResolver(
                [
                    new SimpleIconsImporter(),
                    new AegisIconPackImporter(),
                    new FilenameIndexedIconPackImporter()
                ],
                NullLogger<IconPackImporterResolver>.Instance),
            new SvgIconImporter())
    {
    }

    public SimpleIconsBrandIconPackService(
        IPlatformApplicationPaths applicationPaths,
        IPlatformFileSecurity fileSecurity,
        ILogger<SimpleIconsBrandIconPackService> logger,
        IIconPackImporterResolver importerResolver,
        ICustomIconImporter customIconImporter)
    {
        ArgumentNullException.ThrowIfNull(applicationPaths);
        _fileSecurity = fileSecurity ?? throw new ArgumentNullException(nameof(fileSecurity));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _importerResolver = importerResolver ?? throw new ArgumentNullException(nameof(importerResolver));
        _customIconImporter = customIconImporter ?? throw new ArgumentNullException(nameof(customIconImporter));
        _issuerAliases = LoadIssuerAliases(_logger);
        _storageRoot = Path.Combine(applicationPaths.ApplicationDataDirectory, StorageDirectoryName);
        _packsRoot = Path.Combine(_storageRoot, "packs");
        _currentPackPath = Path.Combine(_storageRoot, CurrentPackFileName);
        _displaySettingsPath = Path.Combine(_storageRoot, DisplaySettingsFileName);
        _accountBrandSettingsPath = Path.Combine(_storageRoot, AccountBrandSettingsFileName);
        _customIconsRoot = Path.Combine(_storageRoot, "custom");
        _customIconIndexPath = Path.Combine(_storageRoot, CustomIconIndexFileName);
        TryLoadDisplaySettings();
        TryLoadAccountBrandSettings();
        TryLoadCustomIcons();
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
        if (!string.IsNullOrWhiteSpace(explicitBrandId)
            && Volatile.Read(ref _customIcons).TryGetValue(
                explicitBrandId.Trim(),
                out var customIcon))
        {
            return customIcon;
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
        var customIcons = Volatile.Read(ref _customIcons);
        var isCustomIcon = customIcons.TryGetValue(brandId, out var brand);
        if (!isCustomIcon && !catalog.ById.TryGetValue(brandId, out brand)) return false;
        if (brand is null) return false;
        if (_pathDataCache.TryGetValue(brand.Id, out pathData!)) return true;

        try
        {
            var iconRoot = isCustomIcon ? _customIconsRoot : catalog.PackDirectory;
            if (iconRoot is null) return false;
            var iconPath = isCustomIcon
                ? Path.Combine(iconRoot, brand.IconFileName)
                : Path.Combine(iconRoot, "icons", brand.IconFileName);
            if (!IsDescendantOf(iconPath, iconRoot)) return false;
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

            await using var archiveStream = new FileStream(
                temporaryArchive,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                81920,
                useAsync: true);
            var parsedResult = await _importerResolver.ImportAsync(
                new IconPackSource { Stream = archiveStream },
                cancellationToken);
            if (parsedResult.IsFailed)
                return Result.Fail("The archive did not match a supported icon-pack format.");
            var parsed = parsedResult.Value;
            var packageVersion = parsed.Version;
            var packPrefix = SanitizeVersion(parsed.ProviderId).ToLowerInvariant();
            if (packPrefix.Length == 0)
                throw new InvalidDataException("The icon-pack provider id is invalid.");
            var packId = $"{packPrefix}-{SanitizeVersion(packageVersion)}-v{PackFormatVersion}-{archiveHash[..12].ToLowerInvariant()}";
            stagingDirectory = Path.Combine(_packsRoot, $".{packId}-{Guid.NewGuid():N}.staging");
            Directory.CreateDirectory(stagingDirectory);
            _fileSecurity.RestrictDirectoryToCurrentUser(stagingDirectory);
            var iconsDirectory = Path.Combine(stagingDirectory, "icons");
            Directory.CreateDirectory(iconsDirectory);
            _fileSecurity.RestrictDirectoryToCurrentUser(iconsDirectory);

            var imported = new List<IndexBrand>(parsed.Icons.Count);
            foreach (var sourceBrand in parsed.Icons.OrderBy(value => value.Id, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!SafeSlugRegex().IsMatch(sourceBrand.Id)
                    || sourceBrand.SvgData.Length is <= 0 or > MaximumSvgBytes
                    || !IsSafeDisplayText(sourceBrand.Name, 256))
                    throw new InvalidDataException("The normalized icon pack contains invalid icon metadata.");
                var destination = Path.Combine(iconsDirectory, $"{sourceBrand.Id}.svg");
                await File.WriteAllBytesAsync(destination, sourceBrand.SvgData, cancellationToken);
                _fileSecurity.RestrictFileToCurrentUser(destination);
                imported.Add(new IndexBrand(
                    sourceBrand.Id,
                    sourceBrand.Name,
                    sourceBrand.BackgroundColor,
                    $"{sourceBrand.Id}.svg",
                    sourceBrand.Issuers
                        .Where(value => IsSafeDisplayText(value, 128))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Take(MaximumAliasesPerBrand)
                        .ToArray()));
            }
            if (imported.Count == 0) return Result.Fail("The archive did not contain usable brand SVG files.");

            if (parsed.Notices.Count > 0)
            {
                var noticesDirectory = Path.Combine(stagingDirectory, "notices");
                Directory.CreateDirectory(noticesDirectory);
                _fileSecurity.RestrictDirectoryToCurrentUser(noticesDirectory);
                for (var indexValue = 0; indexValue < parsed.Notices.Count; indexValue++)
                {
                    var notice = parsed.Notices[indexValue];
                    if (notice.Data.Length is <= 0 or > MaximumNoticeBytes) continue;
                    var noticePath = Path.Combine(
                        noticesDirectory,
                        $"{indexValue + 1:D2}-{Path.GetFileName(notice.FileName)}");
                    await File.WriteAllBytesAsync(noticePath, notice.Data, cancellationToken);
                    _fileSecurity.RestrictFileToCurrentUser(noticePath);
                }
            }
            var index = new BrandIndex(
                packageVersion,
                imported,
                parsed.ProviderId,
                parsed.ProviderDisplayName,
                parsed.Format);
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
                parsed.Format,
                parsed.ProviderId,
                parsed.ProviderDisplayName));
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

    public async Task<Result<BrandDefinition>> ImportCustomIconAsync(
        Guid accountId,
        Stream svgStream,
        string? fileName = null,
        CancellationToken cancellationToken = default)
    {
        if (accountId == Guid.Empty)
            return Result.Fail<BrandDefinition>("The account identifier is invalid.");
        ArgumentNullException.ThrowIfNull(svgStream);

        await _importLock.WaitAsync(cancellationToken);
        string? temporaryIconPath = null;
        string? temporaryIndexPath = null;
        string? temporarySettingsPath = null;
        try
        {
            var imported = await _customIconImporter.ImportAsync(
                new CustomIconSource
                {
                    Stream = svgStream,
                    FileName = fileName
                },
                cancellationToken);
            if (imported.IsFailed)
                return Result.Fail<BrandDefinition>(imported.Errors);

            var icon = imported.Value;
            if (!SafeSlugRegex().IsMatch(icon.Id)
                || icon.SvgData.Length is <= 0 or > MaximumSvgBytes
                || !IsSafeDisplayText(icon.Name, 256))
            {
                return Result.Fail<BrandDefinition>("The selected custom SVG is invalid.");
            }

            Directory.CreateDirectory(_storageRoot);
            Directory.CreateDirectory(_customIconsRoot);
            _fileSecurity.RestrictDirectoryToCurrentUser(_storageRoot);
            _fileSecurity.RestrictDirectoryToCurrentUser(_customIconsRoot);

            var iconFileName = $"{icon.Id}.svg";
            var finalIconPath = Path.Combine(_customIconsRoot, iconFileName);
            temporaryIconPath = Path.Combine(
                _customIconsRoot,
                $".{icon.Id}-{Guid.NewGuid():N}.tmp");
            await File.WriteAllBytesAsync(temporaryIconPath, icon.SvgData, cancellationToken);
            _fileSecurity.RestrictFileToCurrentUser(temporaryIconPath);
            File.Move(temporaryIconPath, finalIconPath, overwrite: true);
            temporaryIconPath = null;
            _fileSecurity.RestrictFileToCurrentUser(finalIconPath);

            var definition = new BrandDefinition(
                icon.Id,
                icon.Name,
                icon.BackgroundColor,
                iconFileName);
            var customIcons = new Dictionary<string, BrandDefinition>(
                Volatile.Read(ref _customIcons),
                StringComparer.OrdinalIgnoreCase)
            {
                [definition.Id] = definition
            };
            temporaryIndexPath = Path.Combine(
                _storageRoot,
                $"{CustomIconIndexFileName}.{Guid.NewGuid():N}.tmp");
            await WriteJsonAsync(
                temporaryIndexPath,
                new CustomIconIndex(
                    1,
                    customIcons.Values
                        .OrderBy(value => value.Id, StringComparer.Ordinal)
                        .Select(value => new CustomIconEntry(
                            value.Id,
                            value.DisplayName,
                            value.BackgroundColor,
                            value.IconFileName))
                        .ToList()),
                cancellationToken);
            _fileSecurity.RestrictFileToCurrentUser(temporaryIndexPath);
            File.Move(temporaryIndexPath, _customIconIndexPath, overwrite: true);
            temporaryIndexPath = null;
            _fileSecurity.RestrictFileToCurrentUser(_customIconIndexPath);

            var mappings = new Dictionary<Guid, string>(Volatile.Read(ref _accountBrandIds))
            {
                [accountId] = definition.Id
            };
            Directory.CreateDirectory(_storageRoot);
            _fileSecurity.RestrictDirectoryToCurrentUser(_storageRoot);
            temporarySettingsPath = Path.Combine(
                _storageRoot,
                $"{AccountBrandSettingsFileName}.{Guid.NewGuid():N}.tmp");
            var settings = new AccountBrandSettings(
                1,
                mappings.OrderBy(pair => pair.Key)
                    .Select(pair => new AccountBrandOverride(pair.Key, pair.Value))
                    .ToList());
            await WriteJsonAsync(temporarySettingsPath, settings, cancellationToken);
            _fileSecurity.RestrictFileToCurrentUser(temporarySettingsPath);
            File.Move(temporarySettingsPath, _accountBrandSettingsPath, overwrite: true);
            temporarySettingsPath = null;
            _fileSecurity.RestrictFileToCurrentUser(_accountBrandSettingsPath);

            Volatile.Write(ref _customIcons, customIcons);
            Volatile.Write(ref _accountBrandIds, mappings);
            _pathDataCache.TryRemove(definition.Id, out _);
            CatalogChanged?.Invoke(this, EventArgs.Empty);
            return Result.Ok(definition);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "A local per-account custom SVG could not be imported.");
            return Result.Fail<BrandDefinition>(
                "The selected custom SVG is invalid or could not be stored safely.");
        }
        finally
        {
            TryDeleteFile(temporaryIconPath);
            TryDeleteFile(temporaryIndexPath);
            TryDeleteFile(temporarySettingsPath);
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
                && !Volatile.Read(ref _catalog).ById.ContainsKey(normalizedBrandId)
                && !Volatile.Read(ref _customIcons).ContainsKey(normalizedBrandId))
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

    private void TryLoadCustomIcons()
    {
        try
        {
            if (!File.Exists(_customIconIndexPath)) return;
            var file = new FileInfo(_customIconIndexPath);
            if (file.Length is <= 0 or > 1024 * 1024) return;
            var index = JsonSerializer.Deserialize<CustomIconIndex>(
                File.ReadAllText(_customIconIndexPath));
            if (index is null
                || index.Version != 1
                || index.Icons is null
                || index.Icons.Count > MaximumEntries) return;

            var icons = new Dictionary<string, BrandDefinition>(
                StringComparer.OrdinalIgnoreCase);
            foreach (var item in index.Icons)
            {
                if (item is null
                    || string.IsNullOrWhiteSpace(item.Id)
                    || !SafeSlugRegex().IsMatch(item.Id)
                    || !item.Id.StartsWith("custom_", StringComparison.Ordinal)
                    || string.IsNullOrWhiteSpace(item.IconFileName)
                    || !SafeSvgNameRegex().IsMatch(item.IconFileName)
                    || !IsSafeDisplayText(item.DisplayName, 256)
                    || string.IsNullOrWhiteSpace(item.BackgroundColor)
                    || !IconImportArchive.IsHexColor(item.BackgroundColor.TrimStart('#'))
                    || !icons.TryAdd(
                        item.Id,
                        new BrandDefinition(
                            item.Id,
                            item.DisplayName,
                            item.BackgroundColor,
                            item.IconFileName)))
                {
                    return;
                }

                var iconPath = Path.Combine(_customIconsRoot, item.IconFileName);
                if (!IsDescendantOf(iconPath, _customIconsRoot)
                    || !File.Exists(iconPath)
                    || new FileInfo(iconPath).Length is <= 0 or > MaximumSvgBytes)
                {
                    return;
                }
            }
            Volatile.Write(ref _customIcons, icons);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _logger.LogWarning("The local custom-icon index could not be loaded.");
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
        var format = string.Equals(index.Version, "filename-indexed", StringComparison.OrdinalIgnoreCase)
            && index.Format == BrandIconPackFormat.SimpleIcons
            ? BrandIconPackFormat.FilenameIndexed
            : index.Format;
        var providerDisplayName = format == BrandIconPackFormat.FilenameIndexed
            && string.Equals(index.ProviderDisplayName, "Simple Icons", StringComparison.Ordinal)
                ? "Filename-indexed SVG pack"
                : index.ProviderDisplayName;
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
            new BrandIconPackStatus(
                true,
                index.Version,
                byId.Count,
                format,
                providerDisplayName));
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

    private static bool IsSafeDisplayText(string? value, int maximumLength) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length <= maximumLength
        && !value.Any(char.IsControl);

    private static async Task WriteJsonAsync<T>(string path, T value, CancellationToken token)
    {
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
        await JsonSerializer.SerializeAsync(stream, value, cancellationToken: token);
        await stream.FlushAsync(token);
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

    [GeneratedRegex("^[a-z0-9-]+-[0-9A-Za-z.-]+-v[0-9]+-[0-9a-f]{12}$", RegexOptions.CultureInvariant)]
    private static partial Regex SafePackIdRegex();

    private sealed record CurrentPack(string PackId);
    private sealed record BrandDisplaySettings(bool ShowIssuerLogo);
    private sealed record AccountBrandSettings(int Version, List<AccountBrandOverride> Overrides);
    private sealed record AccountBrandOverride(Guid AccountId, string BrandId);
    private sealed record CustomIconIndex(int Version, List<CustomIconEntry> Icons);
    private sealed record CustomIconEntry(
        string Id,
        string DisplayName,
        string BackgroundColor,
        string IconFileName);
    private sealed record BrandIndex(
        string Version,
        List<IndexBrand> Brands,
        string ProviderId = "simple-icons",
        string ProviderDisplayName = "Simple Icons",
        BrandIconPackFormat Format = BrandIconPackFormat.SimpleIcons);
    private sealed record IndexBrand(string Id, string DisplayName, string BackgroundColor, string IconFileName, string[] Aliases);
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
