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

    [Theory]
    [InlineData(
        "en",
        "Create a master password to encrypt your local vault. OTP Harbor cannot reset it if you forget it. After setup, create an encrypted backup and store its password separately.",
        "No accounts yet. Add one manually, scan an account or Google Authenticator export QR code, or restore an encrypted backup from Settings.")]
    [InlineData(
        "de",
        "Erstellen Sie ein Masterpasswort, um Ihren lokalen Tresor zu verschlüsseln. OTP Harbor kann es nicht zurücksetzen, wenn Sie es vergessen. Erstellen Sie anschließend eine verschlüsselte Sicherung und bewahren Sie deren Passwort getrennt auf.",
        "Noch keine Konten vorhanden. Fügen Sie eines manuell hinzu, scannen Sie den QR-Code eines Kontos oder eines Google-Authenticator-Exports oder stellen Sie in den Einstellungen eine verschlüsselte Sicherung wieder her.")]
    [InlineData(
        "fr",
        "Créez un mot de passe principal pour chiffrer votre coffre-fort local. OTP Harbor ne peut pas le réinitialiser si vous l’oubliez. Créez ensuite une sauvegarde chiffrée et conservez son mot de passe séparément.",
        "Aucun compte pour le moment. Ajoutez-en un manuellement, scannez le QR code d’un compte ou d’une exportation Google Authenticator, ou restaurez une sauvegarde chiffrée depuis les paramètres.")]
    [InlineData(
        "es",
        "Cree una contraseña maestra para cifrar su almacén local. OTP Harbor no puede restablecerla si la olvida. Después, cree una copia de seguridad cifrada y guarde su contraseña por separado.",
        "Todavía no hay cuentas. Añada una manualmente, escanee el QR de una cuenta o exportación de Google Authenticator, o restaure una copia de seguridad cifrada desde Configuración.")]
    public void Get_OnboardingExplainsPasswordRecoveryAndFirstAccountOptions(
        string cultureName,
        string expectedSetupDescription,
        string expectedEmptyState)
    {
        var catalog = new MobileStringCatalog(CultureInfo.GetCultureInfo(cultureName));

        Assert.Equal(expectedSetupDescription, catalog.Get(MobileStringKeys.SetupDescription));
        Assert.Equal(expectedEmptyState, catalog.Get(MobileStringKeys.NoAccounts));
    }

    [Theory]
    [InlineData("en", "Showing {0} of {1} accounts")]
    [InlineData("de", "{0} von {1} Konten angezeigt")]
    [InlineData("fr", "{0} comptes affichés sur {1}")]
    [InlineData("es", "Mostrando {0} de {1} cuentas")]
    public void Get_SearchResultSummaryUsesCompleteSelectedLocale(
        string cultureName,
        string expected)
    {
        var catalog = new MobileStringCatalog(CultureInfo.GetCultureInfo(cultureName));

        Assert.Equal(expected, catalog.Get(MobileStringKeys.SearchResultsFormat));
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
