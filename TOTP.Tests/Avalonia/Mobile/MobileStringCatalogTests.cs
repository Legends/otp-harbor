using System.Globalization;
using TOTP.Avalonia.Mobile.Localization;

namespace TOTP.Tests.Avalonia.Mobile;

public sealed class MobileStringCatalogTests
{
    [Theory]
    [InlineData("en")]
    [InlineData("de")]
    [InlineData("fr")]
    [InlineData("es")]
    public void GetMissingKeys_ForSupportedCulture_ReturnsNone(string cultureName)
    {
        var catalog = new MobileStringCatalog(CultureInfo.GetCultureInfo(cultureName));

        var missing = catalog.GetMissingKeys(CultureInfo.GetCultureInfo(cultureName));

        Assert.Empty(missing);
    }

    [Fact]
    public void Get_WithGermanCulture_ReturnsCompleteGermanMessage()
    {
        var catalog = new MobileStringCatalog(CultureInfo.GetCultureInfo("de"));

        var message = catalog.Get(MobileStringKeys.UnlockDescription);

        Assert.Contains("Masterpasswort", message, StringComparison.Ordinal);
        Assert.DoesNotContain("encrypted local vault", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Get_ImportExportActions_UsesClearSeparateEnglishLabels()
    {
        var catalog = new MobileStringCatalog(CultureInfo.GetCultureInfo("en"));

        Assert.Equal("Import", catalog.Get(MobileStringKeys.ImportSection));
        Assert.Equal(
            "Import from Google Authenticator",
            catalog.Get(MobileStringKeys.ImportGoogleQr));
        Assert.Equal("Import OTP Harbor backup", catalog.Get(MobileStringKeys.ImportBackup));
        Assert.Equal("Export", catalog.Get(MobileStringKeys.ExportSection));
        Assert.Equal("Export encrypted backup", catalog.Get(MobileStringKeys.ExportBackup));
    }

    [Fact]
    public void Get_BiometricRecoveryMessage_UsesOnlyActiveGermanLocale()
    {
        var catalog = new MobileStringCatalog(CultureInfo.GetCultureInfo("de"));

        var message = catalog.Get(MobileStringKeys.BiometricRecoveryRequired);

        Assert.Contains("Masterpasswort", message, StringComparison.Ordinal);
        Assert.Contains("biometrischen Zugriff", message, StringComparison.Ordinal);
        Assert.DoesNotContain("recovery", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ApplyCulture_ChangesTheActiveLocale()
    {
        var catalog = new MobileStringCatalog(CultureInfo.GetCultureInfo("en"));

        catalog.ApplyCulture("de");

        Assert.Equal("Löschen", catalog.Get(MobileStringKeys.Delete));
        Assert.Equal("Einstellungen", catalog.Get(MobileStringKeys.Settings));
    }

    [Theory]
    [InlineData("fr-FR", "Paramètres", "Utilisez votre mot de passe principal pour déverrouiller le coffre et rétablir l’accès biométrique.")]
    [InlineData("es-ES", "Configuración", "Use su contraseña maestra para desbloquear y restaurar el acceso biométrico.")]
    public void ApplyCulture_ForAdditionalLanguage_UsesOnlyTheSelectedLocale(
        string cultureName,
        string expectedSettings,
        string expectedRecoveryMessage)
    {
        var catalog = new MobileStringCatalog(CultureInfo.GetCultureInfo("en"));

        catalog.ApplyCulture(CultureInfo.GetCultureInfo(cultureName).TwoLetterISOLanguageName);

        Assert.Equal(expectedSettings, catalog.Get(MobileStringKeys.Settings));
        Assert.Equal(expectedRecoveryMessage, catalog.Get(MobileStringKeys.BiometricRecoveryRequired));
    }
}
