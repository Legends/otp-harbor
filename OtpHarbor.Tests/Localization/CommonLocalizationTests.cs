using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using TOTP.Avalonia.Desktop.Localization;
using TOTP.Avalonia.Mobile.Localization;
using TOTP.Localization;

namespace TOTP.Tests.Localization;

public sealed class CommonLocalizationTests
{
    [Theory]
    [InlineData("en-US", "en", "Cancel")]
    [InlineData("de-DE", "de", "Abbrechen")]
    [InlineData("de-AT", "de", "Abbrechen")]
    [InlineData("fr-CA", "fr", "Annuler")]
    [InlineData("es-MX", "es", "Cancelar")]
    [InlineData("it-IT", "en", "Cancel")]
    [InlineData("", "en", "Cancel")]
    [InlineData(null, "en", "Cancel")]
    [InlineData("not/a/culture", "en", "Cancel")]
    public void SharedCultureResolution_HandlesRegionalUnsupportedAndInvalidNames(string? name, string expectedCulture, string expectedText)
    {
        var culture = LocalizationCulture.Resolve(name);
        Assert.Equal(expectedCulture, culture.Name);
        Assert.Equal(expectedText, new LocalizationCatalog().Get(CommonStringKeys.Cancel, culture));
        var mobile = new MobileStringCatalog();
        mobile.ApplyCulture(name);
        Assert.Equal(culture, mobile.Culture);
        Assert.Equal(expectedText, mobile.Get(MobileStringKeys.Cancel));
    }

    [Theory]
    [InlineData("en")]
    [InlineData("de-DE")]
    [InlineData("fr-CA")]
    [InlineData("es-MX")]
    public void EveryCatalogAndTypedKey_HasCompleteExactLocaleResources(string locale)
    {
        var culture = CultureInfo.GetCultureInfo(locale);
        var catalogs = new[]
        {
            (LocalizationScope.Common, CommonStringKeys.All, typeof(CommonStringKeys)),
            (LocalizationScope.Desktop, AvaloniaStringKeys.All, typeof(AvaloniaStringKeys)),
            (LocalizationScope.Mobile, MobileStringKeys.All, typeof(MobileStringKeys)),
            (LocalizationScope.Updater, UpdaterStringKeys.All, typeof(UpdaterStringKeys))
        };
        foreach (var (scope, keys, type) in catalogs)
        {
            Assert.Equal(keys.Count, keys.Distinct(StringComparer.Ordinal).Count());
            var catalog = new LocalizationCatalog(scope);
            Assert.Empty(catalog.GetMissingKeys(keys, culture));
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Static).Where(value => value.IsLiteral && value.FieldType == typeof(string)))
                Assert.Contains((string)field.GetRawConstantValue()!, keys);
            foreach (var key in keys)
            {
                var english = catalog.Get(key, LocalizationCulture.English);
                var localized = catalog.Get(key, culture);
                var isFormat = Regex.IsMatch(english, @"(?<!\{)\{\d+[,}:]");
                Assert.Equal(isFormat, Regex.IsMatch(localized, @"(?<!\{)\{\d+[,}:]"));
                if (isFormat)
                    Assert.Equal(CompositeFormat.Parse(english).MinimumArgumentCount, CompositeFormat.Parse(localized).MinimumArgumentCount);
            }
        }
    }

    [Theory]
    [InlineData(LocalizationScope.Common)]
    [InlineData(LocalizationScope.Desktop)]
    [InlineData(LocalizationScope.Mobile)]
    [InlineData(LocalizationScope.Updater)]
    public void MissingKeys_ReturnTheKeyAndRemainVisibleToCompletenessChecks(LocalizationScope scope)
    {
        var catalog = new LocalizationCatalog(scope);
        Assert.Equal("MissingSyntheticKey", catalog.Get("MissingSyntheticKey", CultureInfo.GetCultureInfo("de")));
        Assert.Equal(["MissingSyntheticKey"], catalog.GetMissingKeys(["MissingSyntheticKey"], CultureInfo.GetCultureInfo("fr")));
        Assert.Throws<ArgumentException>(() => catalog.Get(" ", LocalizationCulture.English));
        Assert.Throws<ArgumentNullException>(() => catalog.Get(CommonStringKeys.Cancel, null!));
    }

    [Fact]
    public void ContextSpecificWordingAndPlaceholderContracts_ArePreserved()
    {
        var desktop = new LocalizationCatalog(LocalizationScope.Desktop);
        var mobile = new LocalizationCatalog(LocalizationScope.Mobile);
        Assert.Equal("Manual backup", desktop.Get(AvaloniaStringKeys.BackupSection, LocalizationCulture.English));
        Assert.Equal("Backup", mobile.Get(MobileStringKeys.BackupSection, LocalizationCulture.English));
        Assert.Equal(2, CompositeFormat.Parse(desktop.Get(AvaloniaStringKeys.DeleteAccountPrompt, LocalizationCulture.English)).MinimumArgumentCount);
        Assert.Equal(1, CompositeFormat.Parse(mobile.Get(MobileStringKeys.DeleteAccountPrompt, LocalizationCulture.English)).MinimumArgumentCount);
        Assert.Equal(AvaloniaStringKeys.BackupSection, new LocalizationCatalog().Get(AvaloniaStringKeys.BackupSection, LocalizationCulture.English));
        var german = CultureInfo.GetCultureInfo("de");
        // Existing resource encodings produced different runtime line endings; migration retains both.
        Assert.Contains("\r\n", desktop.Get("ImportReviewExistingParameters", german));
        Assert.DoesNotContain('\r', mobile.Get("ImportReviewExistingParameters", german));
    }

    [Fact]
    public void DomainAwareSharedUiKeyLists_RemainCoveredByBothPresentationCatalogs()
    {
        var sharedKeys = TOTP.Avalonia.Shared.Storage.StorageLocationStringKeys.All
            .Concat(TOTP.Avalonia.Shared.Import.MigrationStringKeys.All)
            .Concat(TOTP.Avalonia.Shared.Import.ReviewStringKeys.AllKeys)
            .Concat(TOTP.Avalonia.Shared.Import.FileImportStringKeys.All)
            .Distinct(StringComparer.Ordinal).ToArray();
        foreach (var (scope, keys) in new[] { (LocalizationScope.Desktop, AvaloniaStringKeys.All), (LocalizationScope.Mobile, MobileStringKeys.All) })
        {
            Assert.All(sharedKeys, key => Assert.Contains(key, keys));
            foreach (var culture in LocalizationCulture.Supported)
                Assert.Empty(new LocalizationCatalog(scope).GetMissingKeys(sharedKeys, culture));
        }
    }

    [Fact]
    public void LocalizationAssembly_IsTheOnlyResourceOwnerAndHasNoUiOrDomainDependency()
    {
        var assembly = typeof(LocalizationCatalog).Assembly;
        Assert.Equal(assembly, typeof(AvaloniaStringKeys).Assembly);
        Assert.Equal(assembly, typeof(MobileStringKeys).Assembly);
        Assert.Contains(typeof(AvaloniaStringKeys), typeof(AvaloniaStringCatalog).Assembly.GetForwardedTypes());
        Assert.Contains(typeof(MobileStringKeys), typeof(MobileStringCatalog).Assembly.GetForwardedTypes());
        Assert.DoesNotContain(assembly.GetReferencedAssemblies(), name =>
            name.Name!.StartsWith("Avalonia", StringComparison.Ordinal) || name.Name.StartsWith("TOTP.", StringComparison.Ordinal));
        Assert.Equal(4, assembly.GetManifestResourceNames().Length);
        foreach (var locale in new[] { "de", "fr", "es" })
        {
            var satellite = assembly.GetSatelliteAssembly(CultureInfo.GetCultureInfo(locale));
            Assert.Equal(4, satellite.GetManifestResourceNames().Length);
        }
        Assert.DoesNotContain(typeof(AvaloniaStringCatalog).Assembly.GetManifestResourceNames(), name => name.EndsWith("Localization.Strings.resources", StringComparison.Ordinal));
        Assert.DoesNotContain(typeof(MobileStringCatalog).Assembly.GetManifestResourceNames(), name => name.EndsWith("Localization.Strings.resources", StringComparison.Ordinal));
        Assert.Throws<ArgumentOutOfRangeException>(() => new LocalizationCatalog((LocalizationScope)(-1)));
    }

    [Fact]
    public async Task CommonLookup_IsSafeForConcurrentCulturesWithoutChangingGlobalCulture()
    {
        var catalog = new LocalizationCatalog();
        var values = new[] { ("en", "Cancel"), ("de", "Abbrechen"), ("fr", "Annuler"), ("es", "Cancelar") };
        await Task.WhenAll(Enumerable.Range(0, 100).Select(index => Task.Run(() =>
        {
            var (locale, expected) = values[index % values.Length];
            Assert.Equal(expected, catalog.Get(CommonStringKeys.Cancel, CultureInfo.GetCultureInfo(locale)));
        })));
    }
}
