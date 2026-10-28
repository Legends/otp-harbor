using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
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
    private const int MaximumArchiveBytes = 512 * 1024 * 1024;
    private const int MaximumEntries = 10_000;
    private const int MaximumSvgBytes = IconImportArchive.MaximumSvgBytes;
    private const int MaximumNoticeBytes = 128 * 1024;
    private const int MaximumOtpHarborNoticeBytes = 1024 * 1024;
    private const int MaximumAliasesPerBrand = 32;
    private const int MaximumInstalledIndexBytes = 256 * 1024 * 1024;
    private const int MaximumInstalledPacks = 32;
    private const int PackFormatVersion = 7;
    private const string StorageDirectoryName = "BrandIcons";
    private const string CurrentPackFileName = "current.json";
    private const string InstalledPacksFileName = "installed-packs.json";
    private const string DisplaySettingsFileName = "display-settings.json";
    private const string AccountBrandSettingsFileName = "account-brand-settings.json";
    private const string CustomIconIndexFileName = "custom-icon-index.json";
    private const string IndexFileName = "brand-index.json";
    private const string RuntimeArchiveFileName = "pack.otphicons";
    private const string RetiredPacksDirectoryPrefix = ".packs-reset-";
    private const string RetiredPacksDirectorySuffix = ".retired";

    private readonly string _storageRoot;
    private readonly string _packsRoot;
    private readonly string _currentPackPath;
    private readonly string _installedPacksPath;
    private readonly string _displaySettingsPath;
    private readonly string _accountBrandSettingsPath;
    private readonly string _customIconsRoot;
    private readonly string _customIconIndexPath;
    private readonly IPlatformFileSecurity _fileSecurity;
    private readonly ILogger<SimpleIconsBrandIconPackService> _logger;
    private readonly IIconPackImporterResolver _importerResolver;
    private readonly ICustomIconImporter _customIconImporter;
    private readonly Action<Action> _scheduleBackgroundWork;
    private IssuerAliasResolver _issuerAliases = IssuerAliasResolver.Empty;
    private readonly SemaphoreSlim _importLock = new(1, 1);
    private readonly object _initializationLock = new();
    private readonly ConcurrentDictionary<string, IReadOnlyList<BrandIconLayer>> _iconLayerCache =
        new(StringComparer.Ordinal);
    private CatalogSnapshot _catalog = CatalogSnapshot.Empty;
    private IReadOnlyDictionary<Guid, string> _accountBrandIds =
        new Dictionary<Guid, string>();
    private IReadOnlyDictionary<string, BrandDefinition> _customIcons =
        new Dictionary<string, BrandDefinition>(StringComparer.OrdinalIgnoreCase);
    private bool _showIssuerLogo = true;
    private int _initialized;

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
                    new OtpHarborIconPackImporter(),
                    new SimpleIconsImporter(),
                    new AegisIconPackImporter(),
                    new FilenameIndexedIconPackImporter()
                ],
                NullLogger<IconPackImporterResolver>.Instance),
            new CustomIconImporter())
    {
    }

    public SimpleIconsBrandIconPackService(
        IPlatformApplicationPaths applicationPaths,
        IPlatformFileSecurity fileSecurity,
        ILogger<SimpleIconsBrandIconPackService> logger,
        IIconPackImporterResolver importerResolver,
        ICustomIconImporter customIconImporter)
        : this(
            applicationPaths,
            fileSecurity,
            logger,
            importerResolver,
            customIconImporter,
            action => _ = Task.Run(action))
    {
    }

    internal SimpleIconsBrandIconPackService(
        IPlatformApplicationPaths applicationPaths,
        IPlatformFileSecurity fileSecurity,
        ILogger<SimpleIconsBrandIconPackService> logger,
        IIconPackImporterResolver importerResolver,
        ICustomIconImporter customIconImporter,
        Action<Action> scheduleBackgroundWork)
    {
        ArgumentNullException.ThrowIfNull(applicationPaths);
        _fileSecurity = fileSecurity ?? throw new ArgumentNullException(nameof(fileSecurity));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _importerResolver = importerResolver ?? throw new ArgumentNullException(nameof(importerResolver));
        _customIconImporter = customIconImporter ?? throw new ArgumentNullException(nameof(customIconImporter));
        _scheduleBackgroundWork = scheduleBackgroundWork
            ?? throw new ArgumentNullException(nameof(scheduleBackgroundWork));
        _storageRoot = Path.Combine(applicationPaths.ApplicationDataDirectory, StorageDirectoryName);
        _packsRoot = Path.Combine(_storageRoot, "packs");
        _currentPackPath = Path.Combine(_storageRoot, CurrentPackFileName);
        _installedPacksPath = Path.Combine(_storageRoot, InstalledPacksFileName);
        _displaySettingsPath = Path.Combine(_storageRoot, DisplaySettingsFileName);
        _accountBrandSettingsPath = Path.Combine(_storageRoot, AccountBrandSettingsFileName);
        _customIconsRoot = Path.Combine(_storageRoot, "custom");
        _customIconIndexPath = Path.Combine(_storageRoot, CustomIconIndexFileName);
    }

    public event EventHandler? CatalogChanged;

    public Task InitializeAsync(CancellationToken cancellationToken = default) =>
        Task.Run(EnsureInitialized, cancellationToken);

    public BrandIconPackStatus Status
    {
        get
        {
            EnsureInitialized();
            return Volatile.Read(ref _catalog).Status;
        }
    }

    public IReadOnlyList<BrandIconPackInstallation> InstalledPacks =>
        Status.InstalledPacks ?? [];

    public bool ShowIssuerLogo
    {
        get
        {
            EnsureInitialized();
            return Volatile.Read(ref _showIssuerLogo);
        }
    }

    public IReadOnlyList<BrandDefinition> AvailableBrands
    {
        get
        {
            EnsureInitialized();
            return Volatile.Read(ref _catalog).Brands;
        }
    }

    public string? GetAccountBrandId(Guid accountId)
    {
        EnsureInitialized();
        if (accountId == Guid.Empty) return null;
        var mappings = Volatile.Read(ref _accountBrandIds);
        return mappings.TryGetValue(accountId, out var brandId) ? brandId : null;
    }

    public BrandDefinition? Resolve(string? issuer, string? explicitBrandId = null)
    {
        EnsureInitialized();
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
        if (catalog.HasAuthoritativeAliasIndex)
        {
            var authoritativeKey = OtpHarborIssuerNormalizer.Normalize(trimmed);
            return catalog.ByOtpHarborAlias.TryGetValue(authoritativeKey, out var authoritative)
                ? authoritative
                : null;
        }
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
        return Resolve(issuer, explicitBrandId);
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
        if (!TryGetIconLayers(brandId, out var layers) || layers.Count == 0) return false;
        pathData = layers[0].PathData;
        return true;
    }

    public bool TryGetIconLayers(string brandId, out IReadOnlyList<BrandIconLayer> layers)
    {
        EnsureInitialized();
        layers = [];
        if (string.IsNullOrWhiteSpace(brandId)) return false;
        var catalog = Volatile.Read(ref _catalog);
        var customIcons = Volatile.Read(ref _customIcons);
        var isCustomIcon = customIcons.TryGetValue(brandId, out var brand);
        if (!isCustomIcon && !catalog.ById.TryGetValue(brandId, out brand)) return false;
        if (brand is null || string.IsNullOrWhiteSpace(brand.IconFileName)) return false;
        if (_iconLayerCache.TryGetValue(brand.Id, out layers!)) return true;

        try
        {
            var iconRoot = isCustomIcon
                ? _customIconsRoot
                : catalog.IconRoots.TryGetValue(brand.Id, out var installedRoot)
                    ? installedRoot
                    : null;
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
            var document = XDocument.Load(reader, LoadOptions.None);
            var root = document.Root;
            if (root is null || !root.Name.LocalName.Equals("svg", StringComparison.OrdinalIgnoreCase))
                return false;
            var viewport = TryParseSvgViewport(root, out var parsedViewport)
                ? parsedViewport
                : null;
            var parsedLayers = new List<BrandIconLayer>();
            if (!TryCollectSvgLayers(root, null, null, null, null, viewport, parsedLayers)) return false;

            if (parsedLayers.Count == 0) return false;
            layers = _iconLayerCache.GetOrAdd(brand.Id, parsedLayers.ToArray());
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or XmlException)
        {
            _logger.LogWarning("A locally installed brand icon could not be read.");
        }
        return false;
    }

    public bool TryGetIconTransform(string brandId, out BrandIconTransform? transform)
    {
        EnsureInitialized();
        transform = null;
        if (!TryGetIconLayers(brandId, out var layers) || layers.Count == 0) return false;
        transform = layers[0].Transform;
        return transform is not null;
    }

    public Task<Result<BrandIconPackImportResult>> ImportAsync(
        Stream zipStream,
        CancellationToken cancellationToken = default) =>
        ImportAsync(zipStream, null, cancellationToken);

    public async Task<Result<BrandIconPackImportResult>> ImportAsync(
        Stream zipStream,
        string? fileName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(zipStream);
        var result = await Task.Run(
            () => ImportCoreAsync(zipStream, fileName, cancellationToken),
            cancellationToken);
        if (result.IsSuccess) CatalogChanged?.Invoke(this, EventArgs.Empty);
        return result;
    }

    private async Task<Result<BrandIconPackImportResult>> ImportCoreAsync(
        Stream zipStream,
        string? fileName,
        CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken);
        await _importLock.WaitAsync(cancellationToken);
        var totalTimer = Stopwatch.StartNew();
        var copyAndHashElapsed = TimeSpan.Zero;
        var validationElapsed = TimeSpan.Zero;
        var installationTimer = new Stopwatch();
        string? stagingDirectory = null;
        string? temporaryArchive = null;
        string? unregisteredPackDirectory = null;
        try
        {
            Directory.CreateDirectory(_storageRoot);
            Directory.CreateDirectory(_packsRoot);
            _fileSecurity.RestrictDirectoryToCurrentUser(_storageRoot);
            _fileSecurity.RestrictDirectoryToCurrentUser(_packsRoot);

            temporaryArchive = Path.Combine(_storageRoot, $"import-{Guid.NewGuid():N}.tmp");
            var phaseTimer = Stopwatch.StartNew();
            var archiveHash = await CopyBoundedAndHashAsync(zipStream, temporaryArchive, cancellationToken);
            _fileSecurity.RestrictFileToCurrentUser(temporaryArchive);
            copyAndHashElapsed = phaseTimer.Elapsed;

            IconPackImportResult parsed;
            phaseTimer.Restart();
            await using (var archiveStream = new FileStream(
                             temporaryArchive,
                             FileMode.Open,
                             FileAccess.Read,
                             FileShare.Read,
                             81920,
                             useAsync: true))
            {
                var parsedResult = await _importerResolver.ImportAsync(
                    new IconPackSource { Stream = archiveStream, FileName = fileName },
                    cancellationToken);
                if (parsedResult.IsFailed)
                    return Result.Fail("The archive did not match a supported icon-pack format.");
                parsed = parsedResult.Value;
            }
            validationElapsed = phaseTimer.Elapsed;
            installationTimer.Start();
            var isOtpHarborPack = parsed.Format == BrandIconPackFormat.OtpHarbor;
            var isOtpHarborV2 = isOtpHarborPack && parsed.Metadata?.FormatVersion == 2;
            var isOtpHarborV3 = isOtpHarborPack && parsed.Metadata?.FormatVersion == 3;
            var isOtpHarborRuntimePack = isOtpHarborV2 || isOtpHarborV3;
            var packageVersion = parsed.Version;
            var packPrefix = SanitizeVersion(parsed.ProviderId).ToLowerInvariant();
            if (packPrefix.Length == 0)
                throw new InvalidDataException("The icon-pack provider id is invalid.");
            var packId = $"{packPrefix}-{SanitizeVersion(packageVersion)}-v{PackFormatVersion}-{archiveHash[..12].ToLowerInvariant()}";
            stagingDirectory = Path.Combine(_packsRoot, $".{packId}-{Guid.NewGuid():N}.staging");
            Directory.CreateDirectory(stagingDirectory);
            _fileSecurity.RestrictDirectoryToCurrentUser(stagingDirectory);
            var hasSvgIcons = parsed.Icons.Any(static icon => icon.SvgData is not null);
            var iconsDirectory = Path.Combine(stagingDirectory, "icons");
            if (hasSvgIcons)
            {
                Directory.CreateDirectory(iconsDirectory);
                _fileSecurity.RestrictDirectoryToCurrentUser(iconsDirectory);
            }

            var hasRuntimeIcons = parsed.Icons.Any(static icon => icon.RuntimeIcon is not null);
            var hasRuntimeAppearances = parsed.Icons.Any(static icon => icon.RuntimeAppearances is not null);
            if (hasRuntimeIcons != isOtpHarborV2
                || hasRuntimeAppearances != isOtpHarborV3)
                throw new InvalidDataException("The normalized icon pack runtime layout is invalid.");
            if (isOtpHarborRuntimePack)
            {
                var installedArchivePath = Path.Combine(stagingDirectory, RuntimeArchiveFileName);
                File.Move(temporaryArchive, installedArchivePath);
                temporaryArchive = null;
                _fileSecurity.RestrictFileToCurrentUser(installedArchivePath);
            }

            var imported = new List<IndexBrand>(parsed.Icons.Count);
            foreach (var sourceBrand in parsed.Icons.OrderBy(value => value.Id, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var hasValidSvg = sourceBrand.SvgData is { Length: > 0 } svgData
                    && svgData.Length <= MaximumSvgBytes;
                if (!(isOtpHarborPack
                        ? OtpHarborCanonicalIdRegex().IsMatch(sourceBrand.Id)
                        : SafeSlugRegex().IsMatch(sourceBrand.Id))
                    || isOtpHarborV2 && (sourceBrand.SvgData is not null || sourceBrand.RuntimeIcon is null)
                    || isOtpHarborV3 && (sourceBrand.SvgData is not null
                        || sourceBrand.RuntimeIcon is not null
                        || sourceBrand.RuntimeAppearances is null)
                    || !isOtpHarborRuntimePack
                        && (!hasValidSvg
                            || sourceBrand.RuntimeIcon is not null
                            || sourceBrand.RuntimeAppearances is not null)
                    || !IsSafeDisplayText(sourceBrand.Name, 256))
                    throw new InvalidDataException("The normalized icon pack contains invalid icon metadata.");
                string? iconFileName = null;
                if (sourceBrand.SvgData is { } svg)
                {
                    iconFileName = $"{sourceBrand.Id}.svg";
                    var destination = Path.Combine(iconsDirectory, iconFileName);
                    await File.WriteAllBytesAsync(destination, svg, cancellationToken);
                    _fileSecurity.RestrictFileToCurrentUser(destination);
                }
                var aliases = sourceBrand.Issuers
                    .Where(value => IsSafeDisplayText(value, 256))
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                var maximumAliases = isOtpHarborPack
                    ? int.MaxValue
                    : MaximumAliasesPerBrand;
                if (aliases.Length == 0 || aliases.Length > maximumAliases)
                    throw new InvalidDataException("The normalized icon pack contains invalid issuer aliases.");
                InstalledRuntimeIcon? installedRuntimeIcon = null;
                if (sourceBrand.RuntimeIcon is { } runtimeIcon)
                {
                    installedRuntimeIcon = new InstalledRuntimeIcon(
                        null,
                        runtimeIcon.Sha256,
                        runtimeIcon.Width,
                        runtimeIcon.Height,
                        runtimeIcon.MediaType,
                        runtimeIcon.RenderProfile,
                        runtimeIcon.ArchivePath);
                }
                InstalledRuntimeAppearances? installedRuntimeAppearances = null;
                if (sourceBrand.RuntimeAppearances is { } appearances)
                {
                    installedRuntimeAppearances = new InstalledRuntimeAppearances(
                        ToInstalledAppearance(appearances.LightTheme),
                        ToInstalledAppearance(appearances.DarkTheme));
                }
                imported.Add(new IndexBrand(
                    sourceBrand.Id,
                    sourceBrand.Name,
                    sourceBrand.BackgroundColor,
                    iconFileName,
                    aliases,
                    sourceBrand.SelectedSource,
                    sourceBrand.Sources,
                    installedRuntimeIcon,
                    installedRuntimeAppearances));
            }
            if (imported.Count == 0) return Result.Fail("The archive did not contain usable brand icon assets.");

            if (parsed.Notices.Count > 0 && !isOtpHarborRuntimePack)
            {
                var noticesDirectory = Path.Combine(stagingDirectory, "notices");
                Directory.CreateDirectory(noticesDirectory);
                _fileSecurity.RestrictDirectoryToCurrentUser(noticesDirectory);
                for (var indexValue = 0; indexValue < parsed.Notices.Count; indexValue++)
                {
                    var notice = parsed.Notices[indexValue];
                    var maximumNoticeBytes = isOtpHarborPack
                        ? MaximumOtpHarborNoticeBytes
                        : MaximumNoticeBytes;
                    if (notice.Data.Length is <= 0 || notice.Data.Length > maximumNoticeBytes)
                    {
                        if (isOtpHarborPack)
                            throw new InvalidDataException("The normalized icon pack contains invalid license data.");
                        continue;
                    }
                    string noticePath;
                    if (notice.RelativePath is { Length: > 0 } relativePath)
                    {
                        IconImportArchive.EnsureSafeRelativePath(relativePath);
                        if (!relativePath.StartsWith("licenses/", StringComparison.Ordinal))
                            throw new InvalidDataException("The normalized icon pack contains an invalid notice path.");
                        noticePath = Path.Combine(
                            stagingDirectory,
                            "provenance",
                            relativePath.Replace('/', Path.DirectorySeparatorChar));
                        var noticeDirectory = Path.GetDirectoryName(noticePath)
                            ?? throw new InvalidDataException("The normalized icon pack contains an invalid notice path.");
                        if (!IsDescendantOf(noticePath, stagingDirectory))
                            throw new InvalidDataException("The normalized icon pack contains an invalid notice path.");
                        Directory.CreateDirectory(noticeDirectory);
                        _fileSecurity.RestrictDirectoryToCurrentUser(noticeDirectory);
                    }
                    else
                    {
                        noticePath = Path.Combine(
                            noticesDirectory,
                            $"{indexValue + 1:D2}-{Path.GetFileName(notice.FileName)}");
                    }
                    await File.WriteAllBytesAsync(noticePath, notice.Data, cancellationToken);
                    _fileSecurity.RestrictFileToCurrentUser(noticePath);
                }
            }
            var index = new BrandIndex(
                packageVersion,
                imported,
                parsed.ProviderId,
                parsed.ProviderDisplayName,
                parsed.Format,
                parsed.Metadata is null
                    ? null
                    : parsed.Metadata with { ArchiveSha256 = archiveHash.ToLowerInvariant() });
            var indexPath = Path.Combine(stagingDirectory, IndexFileName);
            await WriteJsonAsync(indexPath, index, cancellationToken);
            _fileSecurity.RestrictFileToCurrentUser(indexPath);

            var finalDirectory = Path.Combine(_packsRoot, packId);
            if (!Directory.Exists(finalDirectory))
            {
                Directory.Move(stagingDirectory, finalDirectory);
                stagingDirectory = null;
                unregisteredPackDirectory = finalDirectory;
            }

            var previousRegistration = _catalog.Registrations.FirstOrDefault(value =>
                string.Equals(value.ProviderId, parsed.ProviderId, StringComparison.OrdinalIgnoreCase));
            var priority = previousRegistration?.Priority ?? DefaultPriority(parsed.Format);
            var registrations = _catalog.Registrations
                .Where(value => !string.Equals(
                    value.ProviderId,
                    parsed.ProviderId,
                    StringComparison.OrdinalIgnoreCase))
                .Append(new InstalledPackRegistration(packId, parsed.ProviderId, priority))
                .OrderByDescending(value => value.Priority)
                .ThenBy(value => value.ProviderId, StringComparer.Ordinal)
                .ToArray();

            // Validate the complete combined catalogue before making the new
            // registry durable. A rejected provider must not corrupt the last
            // known-good installed-provider set.
            var snapshot = LoadCombinedCatalog(registrations);
            if (!snapshot.Registrations.Any(value => string.Equals(
                    value.PackId,
                    packId,
                    StringComparison.Ordinal)))
                throw new InvalidDataException("The imported icon provider could not be loaded.");
            await PersistInstalledPacksAsync(registrations, cancellationToken);
            unregisteredPackDirectory = null;

            // Preserve the legacy pointer for downgrade compatibility. New versions
            // use installed-packs.json and combine all registered providers.
            try
            {
                var pointerPath = Path.Combine(_storageRoot, $"{CurrentPackFileName}.{Guid.NewGuid():N}.tmp");
                await WriteJsonAsync(pointerPath, new CurrentPack(packId), cancellationToken);
                _fileSecurity.RestrictFileToCurrentUser(pointerPath);
                File.Move(pointerPath, _currentPackPath, overwrite: true);
                _fileSecurity.RestrictFileToCurrentUser(_currentPackPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning("The legacy icon-pack compatibility pointer could not be updated.");
            }

            Volatile.Write(ref _catalog, snapshot);
            _iconLayerCache.Clear();
            if (previousRegistration is not null
                && !string.Equals(previousRegistration.PackId, packId, StringComparison.Ordinal))
            {
                TryDeleteInstalledPack(previousRegistration.PackId);
            }
            installationTimer.Stop();
            totalTimer.Stop();
            _logger.LogInformation(
                "Brand icon pack import completed in {TotalMilliseconds} ms "
                + "(copy and archive hash {CopyMilliseconds} ms, structural and asset hash validation "
                + "{ValidationMilliseconds} ms, installation and commit {InstallationMilliseconds} ms, "
                + "installed files {InstalledFileCount}).",
                Math.Round(totalTimer.Elapsed.TotalMilliseconds, 1),
                Math.Round(copyAndHashElapsed.TotalMilliseconds, 1),
                Math.Round(validationElapsed.TotalMilliseconds, 1),
                Math.Round(installationTimer.Elapsed.TotalMilliseconds, 1),
                Directory.EnumerateFiles(finalDirectory, "*", SearchOption.AllDirectories).Count());
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
            TryDeleteDirectory(unregisteredPackDirectory);
            _importLock.Release();
        }
    }

    private static InstalledRuntimeAppearance ToInstalledAppearance(
        ImportedRuntimeAppearance appearance) =>
        new(
            new InstalledRuntimeIcon(
                null,
                appearance.Asset.Sha256,
                appearance.Asset.Width,
                appearance.Asset.Height,
                appearance.Asset.MediaType,
                appearance.Asset.RenderProfile,
                appearance.Asset.ArchivePath),
            appearance.AssetRole,
            appearance.BackgroundMode,
            appearance.BackgroundColor,
            appearance.Selection,
            appearance.Source,
            appearance.AppearanceSelectionReason);

    public async Task<Result<BrandDefinition>> ImportCustomIconAsync(
        Guid accountId,
        Stream imageStream,
        string? fileName = null,
        CancellationToken cancellationToken = default)
    {
        if (accountId == Guid.Empty)
            return Result.Fail<BrandDefinition>("The account identifier is invalid.");
        ArgumentNullException.ThrowIfNull(imageStream);

        await InitializeAsync(cancellationToken);
        await _importLock.WaitAsync(cancellationToken);
        string? temporaryIconPath = null;
        string? temporaryIndexPath = null;
        string? temporarySettingsPath = null;
        try
        {
            var imported = await _customIconImporter.ImportAsync(
                new CustomIconSource
                {
                    Stream = imageStream,
                    FileName = fileName
                },
                cancellationToken);
            if (imported.IsFailed)
                return Result.Fail<BrandDefinition>(imported.Errors);

            var icon = imported.Value;
            var hasSvg = icon.SvgData is { Length: > 0 };
            var hasPng = icon.PngData is { Length: > 0 };
            if (!SafeSlugRegex().IsMatch(icon.Id)
                || hasSvg == hasPng
                || !IsSafeDisplayText(icon.Name, 256))
            {
                return Result.Fail<BrandDefinition>("The selected custom icon is invalid.");
            }
            var imageData = hasSvg ? icon.SvgData! : icon.PngData!;
            if (imageData.Length > MaximumSvgBytes)
                return Result.Fail<BrandDefinition>("The selected custom icon is invalid.");
            string? pngSha256 = null;
            if (hasPng)
            {
                pngSha256 = Convert.ToHexString(SHA256.HashData(imageData)).ToLowerInvariant();
                RuntimePngProfileValidator.ValidateCustomEncoded(imageData, pngSha256);
            }

            Directory.CreateDirectory(_storageRoot);
            Directory.CreateDirectory(_customIconsRoot);
            _fileSecurity.RestrictDirectoryToCurrentUser(_storageRoot);
            _fileSecurity.RestrictDirectoryToCurrentUser(_customIconsRoot);

            var iconFileName = $"{icon.Id}.{(hasPng ? "png" : "svg")}";
            var finalIconPath = Path.Combine(_customIconsRoot, iconFileName);
            temporaryIconPath = Path.Combine(
                _customIconsRoot,
                $".{icon.Id}-{Guid.NewGuid():N}.tmp");
            await File.WriteAllBytesAsync(temporaryIconPath, imageData, cancellationToken);
            _fileSecurity.RestrictFileToCurrentUser(temporaryIconPath);
            File.Move(temporaryIconPath, finalIconPath, overwrite: true);
            temporaryIconPath = null;
            _fileSecurity.RestrictFileToCurrentUser(finalIconPath);

            var definition = new BrandDefinition(
                icon.Id,
                icon.Name,
                icon.BackgroundColor,
                hasSvg ? iconFileName : null,
                icon.SourceFileName,
                hasPng
                    ? new BrandRuntimeIcon(
                        $"{icon.Id}:{pngSha256}",
                        finalIconPath,
                        pngSha256!,
                        RuntimePngValidator.Width,
                        RuntimePngValidator.Height,
                        BrandId: icon.Id)
                    : null);
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
                    2,
                    customIcons.Values
                        .OrderBy(value => value.Id, StringComparer.Ordinal)
                        .Select(value => new CustomIconEntry(
                            value.Id,
                            value.DisplayName,
                            value.BackgroundColor,
                            value.IconFileName
                                ?? Path.GetFileName(value.RuntimeIcon?.FilePath)
                                ?? throw new InvalidDataException(
                                    "The custom icon storage metadata is invalid."),
                            value.SourceFileName,
                            value.RuntimeIcon?.Sha256,
                            value.RuntimeIcon?.Width,
                            value.RuntimeIcon?.Height))
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
            _iconLayerCache.TryRemove(definition.Id, out _);
            CatalogChanged?.Invoke(this, EventArgs.Empty);
            return Result.Ok(definition);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "A local per-account custom icon could not be imported.");
            return Result.Fail<BrandDefinition>(
                "The selected custom icon is invalid or could not be stored safely.");
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
        await InitializeAsync(cancellationToken);
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

            var retiredPacksDirectory = RetireImportedPackActivation();
            _iconLayerCache.Clear();
            Volatile.Write(ref _catalog, CatalogSnapshot.Empty);
            QueueRetiredPackCleanup(retiredPacksDirectory);
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
        await InitializeAsync(cancellationToken);
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
        await InitializeAsync(cancellationToken);
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

    private string RetireImportedPackActivation()
    {
        Directory.CreateDirectory(_storageRoot);
        _fileSecurity.RestrictDirectoryToCurrentUser(_storageRoot);

        var resetId = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        var retiredDirectory = Path.Combine(
            _storageRoot,
            $"{RetiredPacksDirectoryPrefix}{resetId}{RetiredPacksDirectorySuffix}");
        if (!IsSafeRetiredPacksDirectory(retiredDirectory))
            throw new InvalidDataException("The retired brand-icon storage location is invalid.");

        var retiredCurrentPackPath = Path.Combine(retiredDirectory, $".{resetId}-{CurrentPackFileName}");
        var retiredInstalledPacksPath = Path.Combine(
            retiredDirectory,
            $".{resetId}-{InstalledPacksFileName}");
        var movedCurrentPack = false;
        var movedInstalledPacks = false;
        try
        {
            Directory.CreateDirectory(retiredDirectory);
            _fileSecurity.RestrictDirectoryToCurrentUser(retiredDirectory);

            if (File.Exists(_currentPackPath))
            {
                File.Move(_currentPackPath, retiredCurrentPackPath);
                movedCurrentPack = true;
            }
            if (File.Exists(_installedPacksPath))
            {
                File.Move(_installedPacksPath, retiredInstalledPacksPath);
                movedInstalledPacks = true;
            }

            Directory.CreateDirectory(_packsRoot);
            _fileSecurity.RestrictDirectoryToCurrentUser(_packsRoot);
            return retiredDirectory;
        }
        catch
        {
            if (movedInstalledPacks && File.Exists(retiredInstalledPacksPath))
                File.Move(retiredInstalledPacksPath, _installedPacksPath);
            if (movedCurrentPack && File.Exists(retiredCurrentPackPath))
                File.Move(retiredCurrentPackPath, _currentPackPath);
            if (Directory.Exists(retiredDirectory))
                Directory.Delete(retiredDirectory, recursive: false);
            throw;
        }
    }

    private void QueueRetiredPackCleanup(string retiredDirectory)
    {
        if (!IsSafeRetiredPacksDirectory(retiredDirectory)) return;
        _scheduleBackgroundWork(() => CleanupRetiredPackStorage(retiredDirectory));
    }

    private void QueueStaleRetiredPackCleanup()
    {
        _scheduleBackgroundWork(() =>
        {
            _importLock.Wait();
            try
            {
                DeleteUnregisteredPackDirectories();
                if (!Directory.Exists(_storageRoot)) return;
                foreach (var directory in Directory.EnumerateDirectories(
                             _storageRoot,
                             $"{RetiredPacksDirectoryPrefix}*{RetiredPacksDirectorySuffix}",
                             SearchOption.TopDirectoryOnly))
                {
                    DeleteRetiredPackDirectory(directory);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning("Retired local brand-icon directories could not be enumerated for cleanup.");
            }
            finally
            {
                _importLock.Release();
            }
        });
    }

    private void CleanupRetiredPackStorage(string retiredDirectory)
    {
        _importLock.Wait();
        try
        {
            DeleteUnregisteredPackDirectories();
            DeleteRetiredPackDirectory(retiredDirectory);
        }
        finally
        {
            _importLock.Release();
        }
    }

    private void DeleteUnregisteredPackDirectories()
    {
        if (!Directory.Exists(_packsRoot)) return;
        var registeredPackIds = Volatile.Read(ref _catalog).Registrations
            .Select(static registration => registration.PackId)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var directory in Directory.EnumerateDirectories(
                     _packsRoot,
                     "*",
                     SearchOption.TopDirectoryOnly))
        {
            if (!IsDescendantOf(directory, _packsRoot)
                || registeredPackIds.Contains(Path.GetFileName(directory)))
                continue;
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(
                    "An inactive local brand-icon directory could not be removed yet.");
            }
        }
    }

    private void DeleteRetiredPackDirectory(string retiredDirectory)
    {
        try
        {
            if (IsSafeRetiredPacksDirectory(retiredDirectory)
                && Directory.Exists(retiredDirectory))
            {
                Directory.Delete(retiredDirectory, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning("A retired local brand-icon directory could not be removed yet.");
        }
    }

    private bool IsSafeRetiredPacksDirectory(string path)
    {
        if (!IsDescendantOf(path, _storageRoot)) return false;
        var name = Path.GetFileName(path);
        return name.StartsWith(RetiredPacksDirectoryPrefix, StringComparison.Ordinal)
               && name.EndsWith(RetiredPacksDirectorySuffix, StringComparison.Ordinal)
               && Guid.TryParseExact(
                   name[RetiredPacksDirectoryPrefix.Length..
                       ^RetiredPacksDirectorySuffix.Length],
                   "N",
                   out _);
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
        catch (Exception ex) when (ex is IOException
                                   or UnauthorizedAccessException
                                   or JsonException
                                   or InvalidDataException)
        {
            _logger.LogWarning("The local brand-icon display preference could not be loaded.");
        }
    }

    private void EnsureInitialized()
    {
        if (Volatile.Read(ref _initialized) != 0) return;
        lock (_initializationLock)
        {
            if (_initialized != 0) return;
            _issuerAliases = LoadIssuerAliases(_logger);
            TryLoadDisplaySettings();
            TryLoadAccountBrandSettings();
            TryLoadCustomIcons();
            TryLoadInstalledCatalog();
            Volatile.Write(ref _initialized, 1);
            QueueStaleRetiredPackCleanup();
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
                || index.Version is not (1 or 2)
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
                    || !SafeCustomIconNameRegex().IsMatch(item.IconFileName)
                    || !IsSafeDisplayText(item.DisplayName, 256)
                    || item.SourceFileName is { Length: > 0 }
                        && !IsSafeDisplayText(item.SourceFileName, 256)
                    || string.IsNullOrWhiteSpace(item.BackgroundColor)
                    || !IconImportArchive.IsHexColor(item.BackgroundColor.TrimStart('#'))
                    )
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
                BrandDefinition definition;
                if (item.IconFileName.EndsWith(".png", StringComparison.Ordinal))
                {
                    if (index.Version != 2
                        || item.Sha256 is null
                        || !LowercaseSha256Regex().IsMatch(item.Sha256)
                        || item.Width != RuntimePngValidator.Width
                        || item.Height != RuntimePngValidator.Height)
                        return;
                    var png = File.ReadAllBytes(iconPath);
                    RuntimePngProfileValidator.ValidateCustomEncoded(png, item.Sha256);
                    definition = new BrandDefinition(
                        item.Id,
                        item.DisplayName,
                        item.BackgroundColor,
                        null,
                        item.SourceFileName,
                        new BrandRuntimeIcon(
                            $"{item.Id}:{item.Sha256}",
                            iconPath,
                            item.Sha256,
                            item.Width.Value,
                            item.Height.Value,
                            BrandId: item.Id));
                }
                else
                {
                    definition = new BrandDefinition(
                        item.Id,
                        item.DisplayName,
                        item.BackgroundColor,
                        item.IconFileName,
                        item.SourceFileName);
                }
                if (!icons.TryAdd(item.Id, definition)) return;
            }
            Volatile.Write(ref _customIcons, icons);
        }
        catch (Exception ex) when (ex is IOException
                                   or UnauthorizedAccessException
                                   or JsonException
                                   or InvalidDataException)
        {
            _logger.LogWarning("The local custom-icon index could not be loaded.");
        }
    }

    private void TryLoadInstalledCatalog()
    {
        try
        {
            var registrations = LoadInstalledPackRegistrations();
            if (registrations.Count == 0) return;
            Volatile.Write(ref _catalog, LoadCombinedCatalog(registrations));
        }
        catch (Exception ex) when (ex is IOException
                                   or UnauthorizedAccessException
                                   or JsonException
                                   or InvalidDataException)
        {
            _logger.LogWarning("The installed local brand icon index could not be loaded.");
        }
    }

    private IReadOnlyList<InstalledPackRegistration> LoadInstalledPackRegistrations()
    {
        if (File.Exists(_installedPacksPath))
        {
            var file = new FileInfo(_installedPacksPath);
            if (file.Length is <= 0 or > 1024 * 1024) return [];
            var registry = JsonSerializer.Deserialize<InstalledPackRegistry>(
                File.ReadAllText(_installedPacksPath));
            if (registry is null
                || registry.Version != 1
                || registry.Packs is null
                || registry.Packs.Count is 0 or > MaximumInstalledPacks) return [];

            var packIds = new HashSet<string>(StringComparer.Ordinal);
            var providerIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var registration in registry.Packs)
            {
                if (registration is null
                    || !SafePackIdRegex().IsMatch(registration.PackId)
                    || !SafeProviderIdRegex().IsMatch(registration.ProviderId)
                    || registration.Priority is < 0 or > 1_000
                    || !packIds.Add(registration.PackId)
                    || !providerIds.Add(registration.ProviderId)) return [];
            }
            return registry.Packs
                .OrderByDescending(value => value.Priority)
                .ThenBy(value => value.ProviderId, StringComparer.Ordinal)
                .ToArray();
        }

        // Version 4 and earlier stored one active pack in current.json. Treat it
        // as the first provider without rewriting user data during construction.
        if (!File.Exists(_currentPackPath)) return [];
        var pointer = JsonSerializer.Deserialize<CurrentPack>(File.ReadAllText(_currentPackPath));
        if (pointer is null || !SafePackIdRegex().IsMatch(pointer.PackId)) return [];
        var index = TryReadBrandIndex(pointer.PackId);
        return index is null
            ? []
            : [new InstalledPackRegistration(
                pointer.PackId,
                index.ProviderId,
                DefaultPriority(index.Format))];
    }

    private CatalogSnapshot LoadCombinedCatalog(
        IReadOnlyList<InstalledPackRegistration> registrations)
    {
        var providers = registrations
            .OrderByDescending(value => value.Priority)
            .ThenBy(value => value.ProviderId, StringComparer.Ordinal)
            .Select(TryLoadProviderSnapshot)
            .Where(value => value is not null)
            .Cast<ProviderSnapshot>()
            .ToArray();
        if (providers.Length == 0) return CatalogSnapshot.Empty;

        var byId = new Dictionary<string, BrandDefinition>(StringComparer.OrdinalIgnoreCase);
        var iconRoots = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var winnersByCanonicalId = new Dictionary<string, BrandDefinition>(StringComparer.Ordinal);
        var available = new List<BrandDefinition>();
        foreach (var provider in providers)
        {
            foreach (var definition in provider.Brands)
            {
                var aliases = provider.AliasesById.TryGetValue(definition.Id, out var values)
                    ? values
                    : [];
                var canonicalId = provider.Format == BrandIconPackFormat.OtpHarbor
                    ? definition.Id
                    : ResolveCanonicalServiceId(definition, aliases);
                if (byId.TryGetValue(definition.Id, out var idWinner))
                {
                    winnersByCanonicalId.TryAdd(canonicalId, idWinner);
                    continue;
                }
                if (winnersByCanonicalId.TryGetValue(canonicalId, out var winner))
                {
                    byId.TryAdd(definition.Id, winner);
                    continue;
                }

                winnersByCanonicalId.Add(canonicalId, definition);
                byId.TryAdd(definition.Id, definition);
                iconRoots.Add(definition.Id, provider.PackDirectory);
                available.Add(definition);
            }
        }

        var byAlias = new Dictionary<string, BrandDefinition>(StringComparer.OrdinalIgnoreCase);
        var byNormalizedAlias = new Dictionary<string, BrandDefinition>(StringComparer.Ordinal);
        var byOtpHarborAlias = new Dictionary<string, BrandDefinition>(StringComparer.Ordinal);
        foreach (var provider in providers)
        {
            foreach (var pair in provider.ByAlias)
            {
                if (byId.TryGetValue(pair.Value.Id, out var winner))
                    byAlias.TryAdd(pair.Key, winner);
            }
            foreach (var pair in provider.ByNormalizedAlias)
            {
                if (byId.TryGetValue(pair.Value.Id, out var winner))
                    byNormalizedAlias.TryAdd(pair.Key, winner);
            }
            foreach (var pair in provider.ByOtpHarborAlias)
            {
                if (byId.TryGetValue(pair.Value.Id, out var winner))
                    byOtpHarborAlias.TryAdd(pair.Key, winner);
            }
        }

        var installations = providers.Select(provider => new BrandIconPackInstallation(
            provider.Index.ProviderId,
            provider.ProviderDisplayName,
            provider.Index.Version,
            provider.ById.Count,
            provider.Format,
            provider.Registration.Priority)).ToArray();
        var status = installations.Length == 1
            ? new BrandIconPackStatus(
                true,
                installations[0].Version,
                available.Count,
                installations[0].Format,
                installations[0].ProviderDisplayName,
                installations)
            : new BrandIconPackStatus(
                true,
                null,
                available.Count,
                null,
                null,
                installations);
        return new CatalogSnapshot(
            byId,
            byAlias,
            byNormalizedAlias,
            byOtpHarborAlias,
            iconRoots,
            available.OrderBy(value => value.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(value => value.Id, StringComparer.Ordinal)
                .ToArray(),
            providers.Select(value => value.Registration).ToArray(),
            status,
            providers.Any(value => value.Format == BrandIconPackFormat.OtpHarbor));
    }

    private ProviderSnapshot? TryLoadProviderSnapshot(InstalledPackRegistration registration)
    {
        var index = TryReadBrandIndex(registration.PackId);
        if (index is null
            || !string.Equals(index.ProviderId, registration.ProviderId, StringComparison.OrdinalIgnoreCase))
            return null;
        var packDirectory = Path.Combine(_packsRoot, registration.PackId);
        return CreateProviderSnapshot(packDirectory, registration, index);
    }

    private BrandIndex? TryReadBrandIndex(string packId)
    {
        if (!SafePackIdRegex().IsMatch(packId)) return null;
        var packDirectory = Path.Combine(_packsRoot, packId);
        if (!IsDescendantOf(packDirectory, _packsRoot)) return null;
        var indexPath = Path.Combine(packDirectory, IndexFileName);
        if (!File.Exists(indexPath) || new FileInfo(indexPath).Length is <= 0 or > MaximumInstalledIndexBytes)
            return null;
        return JsonSerializer.Deserialize<BrandIndex>(File.ReadAllText(indexPath));
    }

    private static ProviderSnapshot CreateProviderSnapshot(
        string packDirectory,
        InstalledPackRegistration registration,
        BrandIndex index)
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
        var byOtpHarborAlias = new Dictionary<string, BrandDefinition>(StringComparer.Ordinal);
        var aliasesById = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        var ambiguousAliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ambiguousNormalizedAliases = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in index.Brands ?? [])
        {
            var isOtpHarborPack = format == BrandIconPackFormat.OtpHarbor;
            var isOtpHarborV2 = isOtpHarborPack && index.Metadata?.FormatVersion == 2;
            var isOtpHarborV3 = isOtpHarborPack && index.Metadata?.FormatVersion == 3;
            var isOtpHarborRuntimePack = isOtpHarborV2 || isOtpHarborV3;
            var validId = isOtpHarborPack
                ? OtpHarborCanonicalIdRegex().IsMatch(item.Id)
                : SafeSlugRegex().IsMatch(item.Id);
            var validFileName = isOtpHarborRuntimePack
                ? item.IconFileName is null
                : isOtpHarborPack
                    ? string.Equals(item.IconFileName, item.Id + ".svg", StringComparison.Ordinal)
                    : item.IconFileName is not null && SafeSvgNameRegex().IsMatch(item.IconFileName);
            var maximumAliases = isOtpHarborPack
                ? int.MaxValue
                : MaximumAliasesPerBrand;
            if (!validId
                || !validFileName
                || !IsSafeDisplayText(item.DisplayName, 256)
                || string.IsNullOrWhiteSpace(item.BackgroundColor)
                || !IconImportArchive.IsHexColor(item.BackgroundColor.TrimStart('#'))
                || item.Aliases is null
                || item.Aliases.Length is <= 0
                || item.Aliases.Length > maximumAliases
                || isOtpHarborPack && (item.SelectedSource is null
                    || item.Sources is null
                    || item.Sources.Count == 0))
            {
                if (isOtpHarborPack)
                    throw new InvalidDataException("The installed OTP Harbor icon index is invalid.");
                continue;
            }
            if (!isOtpHarborRuntimePack)
            {
                var iconPath = Path.Combine(packDirectory, "icons", item.IconFileName!);
                if (!IsDescendantOf(iconPath, packDirectory)
                    || !File.Exists(iconPath)
                    || new FileInfo(iconPath).Length is <= 0 or > MaximumSvgBytes)
                {
                    if (isOtpHarborPack)
                        throw new InvalidDataException("The installed OTP Harbor icon asset is missing or invalid.");
                    continue;
                }
            }
            BrandRuntimeIcon? runtimeIcon = null;
            BrandRuntimeAppearances? runtimeAppearances = null;
            if (isOtpHarborV2)
            {
                if (item.RuntimeAppearances is not null)
                    throw new InvalidDataException("The installed OTP Harbor runtime icon metadata is invalid.");
                var installedRuntime = item.RuntimeIcon;
                var expectedArchiveEntry = $"runtime/foreground-128/{item.Id}.png";
                var isArchiveBacked = installedRuntime?.ArchiveEntryPath is not null;
                if (installedRuntime is null
                    || isArchiveBacked == (installedRuntime.FileName is not null)
                    || !LowercaseSha256Regex().IsMatch(installedRuntime.Sha256)
                    || installedRuntime.Width != RuntimePngValidator.Width
                    || installedRuntime.Height != RuntimePngValidator.Height
                    || !string.Equals(installedRuntime.MediaType, "image/png", StringComparison.Ordinal)
                    || !string.Equals(
                        installedRuntime.RenderProfile,
                        "otp-harbor-foreground-v1",
                        StringComparison.Ordinal))
                    throw new InvalidDataException("The installed OTP Harbor runtime icon metadata is invalid.");
                if (isArchiveBacked)
                {
                    if (!string.Equals(
                            installedRuntime.ArchiveEntryPath,
                            expectedArchiveEntry,
                            StringComparison.Ordinal))
                        throw new InvalidDataException("The installed OTP Harbor runtime icon metadata is invalid.");
                    var archivePath = Path.Combine(packDirectory, RuntimeArchiveFileName);
                    if (!IsDescendantOf(archivePath, packDirectory)
                        || !File.Exists(archivePath)
                        || new FileInfo(archivePath).Length is <= 0 or > MaximumArchiveBytes)
                        throw new InvalidDataException("The installed OTP Harbor runtime archive is missing or invalid.");
                    runtimeIcon = new BrandRuntimeIcon(
                        $"{item.Id}:{installedRuntime.Sha256}",
                        null,
                        installedRuntime.Sha256,
                        installedRuntime.Width,
                        installedRuntime.Height,
                        archivePath,
                        installedRuntime.ArchiveEntryPath,
                        item.Id);
                }
                else
                {
                    if (!string.Equals(installedRuntime.FileName, item.Id + ".png", StringComparison.Ordinal))
                        throw new InvalidDataException("The installed OTP Harbor runtime icon metadata is invalid.");
                    var runtimePath = Path.Combine(
                        packDirectory,
                        "runtime",
                        "foreground-128",
                        installedRuntime.FileName!);
                    if (!IsDescendantOf(runtimePath, packDirectory)
                        || !File.Exists(runtimePath)
                        || new FileInfo(runtimePath).Length is <= 0 or > RuntimePngValidator.MaximumEncodedBytes)
                        throw new InvalidDataException("The installed OTP Harbor runtime icon asset is missing or invalid.");
                    runtimeIcon = new BrandRuntimeIcon(
                        $"{item.Id}:{installedRuntime.Sha256}",
                        runtimePath,
                        installedRuntime.Sha256,
                        installedRuntime.Width,
                        installedRuntime.Height,
                        BrandId: item.Id);
                }
            }
            else if (isOtpHarborV3)
            {
                if (item.RuntimeIcon is not null || item.RuntimeAppearances is null)
                    throw new InvalidDataException("The installed OTP Harbor themed appearance metadata is invalid.");
                var archivePath = Path.Combine(packDirectory, RuntimeArchiveFileName);
                if (!IsDescendantOf(archivePath, packDirectory)
                    || !File.Exists(archivePath)
                    || new FileInfo(archivePath).Length is <= 0 or > MaximumArchiveBytes)
                    throw new InvalidDataException("The installed OTP Harbor runtime archive is missing or invalid.");
                runtimeAppearances = new BrandRuntimeAppearances(
                    LoadInstalledAppearance(
                        item,
                        BrandIconTheme.Light,
                        item.RuntimeAppearances.LightTheme,
                        archivePath),
                    LoadInstalledAppearance(
                        item,
                        BrandIconTheme.Dark,
                        item.RuntimeAppearances.DarkTheme,
                        archivePath));
            }
            else if (item.RuntimeIcon is not null || item.RuntimeAppearances is not null)
            {
                throw new InvalidDataException("The installed icon index contains unexpected runtime icon metadata.");
            }
            var definition = new BrandDefinition(
                item.Id,
                item.DisplayName,
                item.BackgroundColor,
                isOtpHarborRuntimePack ? null : item.IconFileName,
                RuntimeIcon: runtimeIcon,
                RuntimeAppearances: runtimeAppearances);
            if (!byId.TryAdd(item.Id, definition))
            {
                if (isOtpHarborPack)
                    throw new InvalidDataException("The installed OTP Harbor icon index contains duplicate brands.");
                continue;
            }
            aliasesById[item.Id] = item.Aliases;
            if (isOtpHarborPack) continue;
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
        if (format == BrandIconPackFormat.OtpHarbor)
        {
            ValidateInstalledOtpHarborMetadata(index, byId, byOtpHarborAlias);
        }
        return new ProviderSnapshot(
            packDirectory,
            registration,
            index,
            byId,
            byAlias,
            byNormalizedAlias,
            byOtpHarborAlias,
            byId.Values.OrderBy(value => value.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(value => value.Id, StringComparer.Ordinal)
                .ToArray(),
            aliasesById,
            format,
            providerDisplayName);
    }

    private static BrandRuntimeAppearance LoadInstalledAppearance(
        IndexBrand brand,
        BrandIconTheme theme,
        InstalledRuntimeAppearance? appearance,
        string archivePath)
    {
        var asset = appearance?.Asset;
        var themeKey = theme == BrandIconTheme.Dark ? "dark" : "light";
        if (appearance is null
            || asset is null
            || asset.FileName is not null
            || asset.ArchiveEntryPath is null
            || !LowercaseSha256Regex().IsMatch(asset.Sha256)
            || asset.Width != RuntimePngValidator.Width
            || asset.Height != RuntimePngValidator.Height
            || !string.Equals(asset.MediaType, "image/png", StringComparison.Ordinal)
            || !string.Equals(
                asset.RenderProfile,
                "otp-harbor-themed-appearance-v1",
                StringComparison.Ordinal)
            || !string.Equals(
                asset.ArchiveEntryPath,
                $"runtime/appearance-128/{asset.Sha256}.png",
                StringComparison.Ordinal)
            || !Enum.IsDefined(appearance.AssetRole)
            || !Enum.IsDefined(appearance.BackgroundMode)
            || !Enum.IsDefined(appearance.Selection)
            || appearance.AssetRole == BrandIconAssetRole.Foreground
                && (appearance.BackgroundMode != BrandIconBackgroundMode.Solid
                    || appearance.BackgroundColor is null
                    || !UppercaseColorRegex().IsMatch(appearance.BackgroundColor))
            || appearance.AssetRole == BrandIconAssetRole.CompleteIcon
                && (appearance.BackgroundMode != BrandIconBackgroundMode.Embedded
                    || appearance.BackgroundColor is not null)
            || appearance.Source is null
            || !IsSafeDisplayText(appearance.Source.Provider, 128)
            || !IsSafeDisplayText(appearance.Source.SourceId, 256)
            || !IsSafeDisplayText(appearance.Source.AssetPath, 4_096)
            || brand.Sources is null
            || !brand.Sources.Any(source => string.Equals(
                source.Provider,
                appearance.Source.Provider,
                StringComparison.Ordinal))
            || !IsSafeDisplayText(appearance.AppearanceSelectionReason, 4_096))
            throw new InvalidDataException("The installed OTP Harbor themed appearance metadata is invalid.");

        var runtimeIcon = new BrandRuntimeIcon(
            $"{brand.Id}:{themeKey}:{asset.Sha256}",
            null,
            asset.Sha256,
            asset.Width,
            asset.Height,
            archivePath,
            asset.ArchiveEntryPath,
            brand.Id,
            theme);
        return new BrandRuntimeAppearance(
            runtimeIcon,
            appearance.AssetRole,
            appearance.BackgroundMode,
            appearance.BackgroundColor,
            appearance.Selection,
            appearance.Source,
            appearance.AppearanceSelectionReason);
    }

    private static void ValidateInstalledOtpHarborMetadata(
        BrandIndex index,
        IReadOnlyDictionary<string, BrandDefinition> brands,
        IDictionary<string, BrandDefinition> aliases)
    {
        var metadata = index.Metadata;
        if (metadata is null
            || metadata.FormatVersion is not (1 or 2 or 3)
            || !string.Equals(metadata.PackId, "otp-harbor-icons", StringComparison.Ordinal)
            || !IsSafeDisplayText(metadata.Name, 256)
            || metadata.ArchiveSha256 is null
            || !LowercaseSha256Regex().IsMatch(metadata.ArchiveSha256)
            || metadata.Sources is null
            || metadata.Sources.Count == 0
            || metadata.IssuerAliases is null
            || metadata.IssuerAliases.Count == 0)
            throw new InvalidDataException("The installed OTP Harbor icon provenance is invalid.");

        foreach (var alias in metadata.IssuerAliases)
        {
            if (string.IsNullOrWhiteSpace(alias.Key)
                || !string.Equals(
                    OtpHarborIssuerNormalizer.Normalize(alias.Key),
                    alias.Key,
                    StringComparison.Ordinal)
                || !brands.TryGetValue(alias.BrandId, out var definition)
                || !aliases.TryAdd(alias.Key, definition))
                throw new InvalidDataException("The installed OTP Harbor issuer index is invalid.");
        }

        var expectedAliases = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in index.Brands)
        {
            foreach (var alias in item.Aliases)
            {
                var key = OtpHarborIssuerNormalizer.Normalize(alias);
                if (key.Length == 0
                    || expectedAliases.TryGetValue(key, out var owner)
                    && !string.Equals(owner, item.Id, StringComparison.Ordinal))
                    throw new InvalidDataException("The installed OTP Harbor original aliases are ambiguous.");
                expectedAliases.TryAdd(key, item.Id);
            }
        }
        if (expectedAliases.Count != aliases.Count
            || expectedAliases.Any(pair => !aliases.TryGetValue(pair.Key, out var definition)
                || !string.Equals(definition.Id, pair.Value, StringComparison.Ordinal)))
            throw new InvalidDataException("The installed OTP Harbor issuer index is incomplete.");
    }

    private string ResolveCanonicalServiceId(
        BrandDefinition definition,
        IReadOnlyList<string> aliases)
    {
        foreach (var candidate in aliases.Prepend(definition.DisplayName).Append(definition.Id))
        {
            var normalized = IssuerAliasResolver.Normalize(candidate);
            if (_issuerAliases.TryResolve(normalized, out var knownId)) return knownId;
        }
        return IssuerAliasResolver.Normalize(definition.DisplayName);
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

    private static string? NormalizeSvgFill(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var fill = value.Trim();
        if (fill.Equals("none", StringComparison.OrdinalIgnoreCase)) return "none";
        if (fill.Equals("white", StringComparison.OrdinalIgnoreCase)) return "#FFFFFF";
        if (fill.Equals("black", StringComparison.OrdinalIgnoreCase)) return "#000000";
        if (fill.Length == 4
            && fill[0] == '#'
            && fill.Skip(1).All(Uri.IsHexDigit))
            return $"#{char.ToUpperInvariant(fill[1])}{char.ToUpperInvariant(fill[1])}"
                + $"{char.ToUpperInvariant(fill[2])}{char.ToUpperInvariant(fill[2])}"
                + $"{char.ToUpperInvariant(fill[3])}{char.ToUpperInvariant(fill[3])}";
        if (fill.Length == 7
            && fill[0] == '#'
            && IconImportArchive.IsHexColor(fill[1..])) return fill.ToUpperInvariant();
        return null;
    }

    private static bool TryCollectSvgLayers(
        XElement element,
        string? inheritedFill,
        string? inheritedStroke,
        string? inheritedStrokeWidth,
        BrandIconTransform? inheritedTransform,
        BrandIconViewport? viewport,
        ICollection<BrandIconLayer> layers)
    {
        var fill = ResolveSvgPaint(element, "fill", inheritedFill);
        var stroke = ResolveSvgPaint(element, "stroke", inheritedStroke);
        var strokeWidth = ResolveSvgProperty(element, "stroke-width", inheritedStrokeWidth);
        var transform = inheritedTransform;
        var transformText = element.Attributes().FirstOrDefault(attribute =>
            attribute.Name.LocalName.Equals("transform", StringComparison.OrdinalIgnoreCase))?.Value;
        if (!string.IsNullOrWhiteSpace(transformText))
        {
            if (!TryParseSvgTransform(transformText, out var localTransform)) return false;
            transform = inheritedTransform is null
                ? localTransform
                : MultiplySvgTransforms(localTransform, inheritedTransform);
        }

        if (element.Name.LocalName.Equals("path", StringComparison.OrdinalIgnoreCase))
        {
            var data = element.Attribute("d")?.Value;
            if (string.IsNullOrWhiteSpace(data) || data.Length > MaximumSvgBytes) return false;
            BrandIconStroke? parsedStroke = null;
            if (stroke is not null
                && !string.Equals(stroke, "none", StringComparison.OrdinalIgnoreCase))
            {
                var width = 1d;
                if (strokeWidth is not null && !TryParseSvgLength(strokeWidth, out width)) return false;
                parsedStroke = new BrandIconStroke(stroke, width);
            }
            if (!string.Equals(fill, "none", StringComparison.OrdinalIgnoreCase) || parsedStroke is not null)
                layers.Add(new BrandIconLayer(data, fill, transform, viewport, parsedStroke));
        }

        foreach (var child in element.Elements())
            if (!TryCollectSvgLayers(
                    child,
                    fill,
                    stroke,
                    strokeWidth,
                    transform,
                    viewport,
                    layers)) return false;
        return true;
    }

    private static string? ResolveSvgPaint(XElement element, string property, string? inheritedValue)
    {
        var value = ResolveSvgProperty(element, property, null);
        return value is null ? inheritedValue : NormalizeSvgFill(value);
    }

    private static string? ResolveSvgProperty(XElement element, string property, string? inheritedValue)
    {
        var style = element.Attribute("style")?.Value;
        if (!string.IsNullOrWhiteSpace(style))
        {
            foreach (var declaration in style.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var separator = declaration.IndexOf(':');
                if (separator <= 0
                    || !declaration[..separator].Trim().Equals(property, StringComparison.OrdinalIgnoreCase))
                    continue;
                return declaration[(separator + 1)..].Trim();
            }
        }

        var value = element.Attributes().FirstOrDefault(attribute =>
            attribute.Name.LocalName.Equals(property, StringComparison.OrdinalIgnoreCase))?.Value;
        return value ?? inheritedValue;
    }

    private static bool TryParseSvgViewport(XElement root, out BrandIconViewport viewport)
    {
        viewport = null!;
        var viewBox = root.Attributes().FirstOrDefault(attribute =>
            attribute.Name.LocalName.Equals("viewBox", StringComparison.OrdinalIgnoreCase))?.Value;
        if (!string.IsNullOrWhiteSpace(viewBox)
            && TryParseSvgNumbers(viewBox, 4, out var values)
            && values[2] > 0
            && values[3] > 0)
        {
            viewport = new BrandIconViewport(values[0], values[1], values[2], values[3]);
            return true;
        }

        var widthText = root.Attributes().FirstOrDefault(attribute =>
            attribute.Name.LocalName.Equals("width", StringComparison.OrdinalIgnoreCase))?.Value;
        var heightText = root.Attributes().FirstOrDefault(attribute =>
            attribute.Name.LocalName.Equals("height", StringComparison.OrdinalIgnoreCase))?.Value;
        if (!TryParseSvgLength(widthText, out var width)
            || !TryParseSvgLength(heightText, out var height)
            || width <= 0
            || height <= 0)
            return false;
        viewport = new BrandIconViewport(0, 0, width, height);
        return true;
    }

    private static bool TryParseSvgLength(string? value, out double length)
    {
        length = 0;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var match = SvgLengthRegex().Match(value);
        return match.Success
            && TryParseFiniteSvgNumber(match.Groups[1].Value, out length)
            && length > 0;
    }

    private static bool TryParseSvgTransform(
        string? value,
        out BrandIconTransform transform)
    {
        transform = null!;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var matches = SvgTransformFunctionRegex().Matches(value);
        if (matches.Count == 0) return false;
        var consumed = string.Concat(matches.Select(match => match.Value));
        if (!SvgTransformSeparatorRegex().Replace(value, string.Empty)
            .Equals(SvgTransformSeparatorRegex().Replace(consumed, string.Empty), StringComparison.Ordinal))
            return false;

        var combined = IdentitySvgTransform;
        foreach (Match match in matches)
        {
            var function = match.Groups[1].Value;
            if (!TryParseSvgTransformFunction(function, match.Groups[2].Value, out var current))
                return false;
            combined = MultiplySvgTransforms(current, combined);
        }
        transform = combined;
        return true;
    }

    private static bool TryParseSvgTransformFunction(
        string function,
        string arguments,
        out BrandIconTransform transform)
    {
        transform = null!;
        if (!TryParseSvgNumbers(arguments, null, out var values)) return false;
        switch (function.ToLowerInvariant())
        {
            case "matrix" when values.Length == 6:
                transform = new(values[0], values[1], values[2], values[3], values[4], values[5]);
                return true;
            case "translate" when values.Length is 1 or 2:
                transform = new(1, 0, 0, 1, values[0], values.Length == 2 ? values[1] : 0);
                return true;
            case "scale" when values.Length is 1 or 2:
                transform = new(values[0], 0, 0, values.Length == 2 ? values[1] : values[0], 0, 0);
                return true;
            case "rotate" when values.Length is 1 or 3:
            {
                var radians = values[0] * Math.PI / 180d;
                var rotation = new BrandIconTransform(
                    Math.Cos(radians), Math.Sin(radians),
                    -Math.Sin(radians), Math.Cos(radians),
                    0, 0);
                if (values.Length == 1)
                {
                    transform = rotation;
                    return true;
                }
                var toOrigin = new BrandIconTransform(1, 0, 0, 1, -values[1], -values[2]);
                var fromOrigin = new BrandIconTransform(1, 0, 0, 1, values[1], values[2]);
                transform = MultiplySvgTransforms(
                    MultiplySvgTransforms(toOrigin, rotation),
                    fromOrigin);
                return true;
            }
            case "skewx" when values.Length == 1:
                transform = new(1, 0, Math.Tan(values[0] * Math.PI / 180d), 1, 0, 0);
                return IsFiniteSvgTransform(transform);
            case "skewy" when values.Length == 1:
                transform = new(1, Math.Tan(values[0] * Math.PI / 180d), 0, 1, 0, 0);
                return IsFiniteSvgTransform(transform);
            default:
                return false;
        }
    }

    private static bool TryParseSvgNumbers(string value, int? expectedCount, out double[] values)
    {
        var matches = SvgNumberRegex().Matches(value);
        if (matches.Count == 0 || expectedCount is not null && matches.Count != expectedCount)
        {
            values = [];
            return false;
        }
        var remainder = SvgNumberRegex().Replace(value, string.Empty);
        if (remainder.Any(character => !char.IsWhiteSpace(character) && character != ','))
        {
            values = [];
            return false;
        }
        values = new double[matches.Count];
        for (var index = 0; index < matches.Count; index++)
            if (!TryParseFiniteSvgNumber(matches[index].Value, out values[index]))
            {
                values = [];
                return false;
            }
        return true;
    }

    private static bool TryParseFiniteSvgNumber(string value, out double number) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number)
        && double.IsFinite(number)
        && Math.Abs(number) <= 10_000_000;

    private static BrandIconTransform MultiplySvgTransforms(
        BrandIconTransform first,
        BrandIconTransform second) =>
        new(
            first.M11 * second.M11 + first.M12 * second.M21,
            first.M11 * second.M12 + first.M12 * second.M22,
            first.M21 * second.M11 + first.M22 * second.M21,
            first.M21 * second.M12 + first.M22 * second.M22,
            first.M31 * second.M11 + first.M32 * second.M21 + second.M31,
            first.M31 * second.M12 + first.M32 * second.M22 + second.M32);

    private static bool IsFiniteSvgTransform(BrandIconTransform value) =>
        double.IsFinite(value.M11)
        && double.IsFinite(value.M12)
        && double.IsFinite(value.M21)
        && double.IsFinite(value.M22)
        && double.IsFinite(value.M31)
        && double.IsFinite(value.M32);

    private static readonly BrandIconTransform IdentitySvgTransform = new(1, 0, 0, 1, 0, 0);

    private static async Task WriteJsonAsync<T>(string path, T value, CancellationToken token)
    {
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
        await JsonSerializer.SerializeAsync(stream, value, cancellationToken: token);
        await stream.FlushAsync(token);
    }

    private async Task PersistInstalledPacksAsync(
        IReadOnlyList<InstalledPackRegistration> registrations,
        CancellationToken cancellationToken)
    {
        if (registrations.Count is 0 or > MaximumInstalledPacks)
            throw new InvalidDataException("The installed icon-provider registry is invalid.");
        var temporaryPath = Path.Combine(
            _storageRoot,
            $"{InstalledPacksFileName}.{Guid.NewGuid():N}.tmp");
        try
        {
            await WriteJsonAsync(
                temporaryPath,
                new InstalledPackRegistry(1, registrations.ToList()),
                cancellationToken);
            _fileSecurity.RestrictFileToCurrentUser(temporaryPath);
            File.Move(temporaryPath, _installedPacksPath, overwrite: true);
            _fileSecurity.RestrictFileToCurrentUser(_installedPacksPath);
        }
        finally
        {
            TryDeleteFile(temporaryPath);
        }
    }

    private static int DefaultPriority(BrandIconPackFormat format) => format switch
    {
        BrandIconPackFormat.OtpHarbor => 200,
        BrandIconPackFormat.Aegis => 100,
        BrandIconPackFormat.SimpleIcons => 90,
        BrandIconPackFormat.FilenameIndexed => 70,
        BrandIconPackFormat.CustomSvg => 60,
        _ => 50
    };

    private void TryDeleteInstalledPack(string packId)
    {
        try
        {
            if (!SafePackIdRegex().IsMatch(packId)) return;
            var path = Path.Combine(_packsRoot, packId);
            if (IsDescendantOf(path, _packsRoot) && Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning("A superseded local icon-provider directory could not be removed.");
        }
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

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex OtpHarborCanonicalIdRegex();

    [GeneratedRegex("^[a-z0-9_]+\\.svg$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeSvgNameRegex();

    [GeneratedRegex("^[a-z0-9_]+\\.(?:svg|png)$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeCustomIconNameRegex();

    [GeneratedRegex("^[a-z0-9-]+-[0-9A-Za-z.-]+-v[0-9]+-[0-9a-f]{12}$", RegexOptions.CultureInvariant)]
    private static partial Regex SafePackIdRegex();

    [GeneratedRegex("^[a-z0-9-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeProviderIdRegex();

    [GeneratedRegex("^[a-f0-9]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex LowercaseSha256Regex();

    [GeneratedRegex("^#[0-9A-F]{6}$", RegexOptions.CultureInvariant)]
    private static partial Regex UppercaseColorRegex();

    [GeneratedRegex(@"([A-Za-z]+)\s*\(([^)]*)\)", RegexOptions.CultureInvariant)]
    private static partial Regex SvgTransformFunctionRegex();

    [GeneratedRegex(@"[\s,]+", RegexOptions.CultureInvariant)]
    private static partial Regex SvgTransformSeparatorRegex();

    [GeneratedRegex(@"[+-]?(?:\d+(?:\.\d*)?|\.\d+)(?:[Ee][+-]?\d+)?", RegexOptions.CultureInvariant)]
    private static partial Regex SvgNumberRegex();

    [GeneratedRegex(@"^\s*([+-]?(?:\d+(?:\.\d*)?|\.\d+)(?:[Ee][+-]?\d+)?)\s*(?:px)?\s*$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex SvgLengthRegex();

    private sealed record CurrentPack(string PackId);
    private sealed record InstalledPackRegistry(int Version, List<InstalledPackRegistration> Packs);
    private sealed record InstalledPackRegistration(string PackId, string ProviderId, int Priority);
    private sealed record BrandDisplaySettings(bool ShowIssuerLogo);
    private sealed record AccountBrandSettings(int Version, List<AccountBrandOverride> Overrides);
    private sealed record AccountBrandOverride(Guid AccountId, string BrandId);
    private sealed record CustomIconIndex(int Version, List<CustomIconEntry> Icons);
    private sealed record CustomIconEntry(
        string Id,
        string DisplayName,
        string BackgroundColor,
        string IconFileName,
        string? SourceFileName = null,
        string? Sha256 = null,
        int? Width = null,
        int? Height = null);
    private sealed record BrandIndex(
        string Version,
        List<IndexBrand> Brands,
        string ProviderId = "simple-icons",
        string ProviderDisplayName = "Simple Icons",
        BrandIconPackFormat Format = BrandIconPackFormat.SimpleIcons,
        IconPackMetadata? Metadata = null);
    private sealed record IndexBrand(
        string Id,
        string DisplayName,
        string BackgroundColor,
        string? IconFileName,
        string[] Aliases,
        IconPackSourceReference? SelectedSource = null,
        IReadOnlyList<IconPackSourceReference>? Sources = null,
        InstalledRuntimeIcon? RuntimeIcon = null,
        InstalledRuntimeAppearances? RuntimeAppearances = null);
    private sealed record InstalledRuntimeIcon(
        string? FileName,
        string Sha256,
        int Width,
        int Height,
        string MediaType,
        string RenderProfile,
        string? ArchiveEntryPath = null);
    private sealed record InstalledRuntimeAppearance(
        InstalledRuntimeIcon Asset,
        BrandIconAssetRole AssetRole,
        BrandIconBackgroundMode BackgroundMode,
        string? BackgroundColor,
        BrandIconAppearanceSelection Selection,
        BrandIconAppearanceSource Source,
        string AppearanceSelectionReason);
    private sealed record InstalledRuntimeAppearances(
        InstalledRuntimeAppearance LightTheme,
        InstalledRuntimeAppearance DarkTheme);
    private sealed record ProviderSnapshot(
        string PackDirectory,
        InstalledPackRegistration Registration,
        BrandIndex Index,
        IReadOnlyDictionary<string, BrandDefinition> ById,
        IReadOnlyDictionary<string, BrandDefinition> ByAlias,
        IReadOnlyDictionary<string, BrandDefinition> ByNormalizedAlias,
        IReadOnlyDictionary<string, BrandDefinition> ByOtpHarborAlias,
        IReadOnlyList<BrandDefinition> Brands,
        IReadOnlyDictionary<string, IReadOnlyList<string>> AliasesById,
        BrandIconPackFormat Format,
        string ProviderDisplayName);
    private sealed record CatalogSnapshot(
        IReadOnlyDictionary<string, BrandDefinition> ById,
        IReadOnlyDictionary<string, BrandDefinition> ByAlias,
        IReadOnlyDictionary<string, BrandDefinition> ByNormalizedAlias,
        IReadOnlyDictionary<string, BrandDefinition> ByOtpHarborAlias,
        IReadOnlyDictionary<string, string> IconRoots,
        IReadOnlyList<BrandDefinition> Brands,
        IReadOnlyList<InstalledPackRegistration> Registrations,
        BrandIconPackStatus Status,
        bool HasAuthoritativeAliasIndex)
    {
        internal static CatalogSnapshot Empty { get; } = new(
            new Dictionary<string, BrandDefinition>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, BrandDefinition>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, BrandDefinition>(StringComparer.Ordinal),
            new Dictionary<string, BrandDefinition>(StringComparer.Ordinal),
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            [],
            [],
            new BrandIconPackStatus(false, null, 0),
            false);
    }
}
