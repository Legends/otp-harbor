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
        Assert.Equal(
            "Import accounts from other formats",
            catalog.Get(MobileStringKeys.ImportAccountFile));
        Assert.Equal("Backup & Restore", catalog.Get(MobileStringKeys.BackupTitle));
        Assert.Equal("Restore", catalog.Get(MobileStringKeys.ImportBackup));
        Assert.Equal("Export", catalog.Get(MobileStringKeys.ExportSection));
        Assert.Equal("Export", catalog.Get(MobileStringKeys.ExportBackup));
        Assert.Equal("Confirm account import", catalog.Get(MobileStringKeys.ImportConfirmationTitle));
        Assert.Equal("Replace", catalog.Get(MobileStringKeys.Override));
    }

    [Theory]
    [InlineData("en")]
    [InlineData("de")]
    [InlineData("fr")]
    [InlineData("es")]
    public void Get_OtherFormatImportHelpNamesEverySupportedFormat(string cultureName)
    {
        var catalog = new MobileStringCatalog(CultureInfo.GetCultureInfo(cultureName));

        var formats = string.Join('\n',
            catalog.Get(MobileStringKeys.ImportFormatAegis),
            catalog.Get(MobileStringKeys.ImportFormatTwoFas),
            catalog.Get(MobileStringKeys.ImportFormatOtpAuth));

        Assert.Contains("Aegis", formats, StringComparison.Ordinal);
        Assert.Contains("2FAS", formats, StringComparison.Ordinal);
        Assert.Contains(".json", formats, StringComparison.Ordinal);
        Assert.Contains(".txt", formats, StringComparison.Ordinal);
        Assert.Contains(".2fas", formats, StringComparison.Ordinal);
        Assert.Contains("otpauth://", formats, StringComparison.Ordinal);
        Assert.DoesNotContain("OTP Harbor", formats, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(".csv", formats, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("\"entries\"", catalog.Get(MobileStringKeys.FaqImportFormatsAegisExample));
        Assert.Contains("\"services\"", catalog.Get(MobileStringKeys.FaqImportFormatsTwoFasExample));
        Assert.StartsWith("otpauth://", catalog.Get(MobileStringKeys.FaqImportFormatsOtpAuthExample));
        Assert.False(string.IsNullOrWhiteSpace(catalog.Get(MobileStringKeys.FaqImportFormatsAegisDescription)));
        Assert.False(string.IsNullOrWhiteSpace(catalog.Get(MobileStringKeys.FaqImportFormatsTwoFasDescription)));
        Assert.False(string.IsNullOrWhiteSpace(catalog.Get(MobileStringKeys.FaqImportFormatsOtpAuthDescription)));
        Assert.Contains("OTP Harbor", catalog.Get(MobileStringKeys.IconPackBuilderLink));
        Assert.Contains("Simple Icons", catalog.Get(MobileStringKeys.FaqSimpleIconsOfficialLink));
        Assert.Contains("Aegis", catalog.Get(MobileStringKeys.FaqAegisIconPackDocsLink));
        Assert.Contains("Dashboard Icons", catalog.Get(MobileStringKeys.FaqDashboardIconsLegalLink));
        Assert.Contains("OTP Harbor", catalog.Get(MobileStringKeys.FaqImportIconPacksDisclaimer));
    }

    [Theory]
    [InlineData("en", "Third-party icon packs", "does not imply affiliation")]
    [InlineData("de", "Symbolpakete und benutzerdefinierte Symbole", "bedeutet weder eine Verbindung")]
    [InlineData("fr", "packs d’icônes tiers", "n’implique aucune affiliation")]
    [InlineData("es", "paquetes de iconos de terceros", "no implica afiliación")]
    public void Get_BrandIconDescriptionIncludesLocalizedOwnershipNotice(
        string cultureName,
        string expectedOpening,
        string expectedDisclaimer)
    {
        var catalog = new MobileStringCatalog(CultureInfo.GetCultureInfo(cultureName));

        var description = catalog.Get(MobileStringKeys.BrandIconsDescription);

        Assert.Contains(expectedOpening, description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(expectedDisclaimer, description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("OTP Harbor", description, StringComparison.Ordinal);
        Assert.Contains(".otphicons", description, StringComparison.Ordinal);
        Assert.Contains(
            "otp-harbor-icons.otphicons",
            catalog.Get(MobileStringKeys.FaqImportIconPacksAnswer),
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(
        "en",
        "Create a master password to encrypt your local vault. OTP Harbor cannot reset it if you forget it. After setup, create an encrypted backup and store its password separately.",
        "No accounts yet. Add one manually, scan an account or Google Authenticator export QR code, or restore an encrypted backup from Settings.",
        "Set up your first account")]
    [InlineData(
        "de",
        "Erstellen Sie ein Masterpasswort, um Ihren lokalen Tresor zu verschlüsseln. OTP Harbor kann es nicht zurücksetzen, wenn Sie es vergessen. Erstellen Sie anschließend eine verschlüsselte Sicherung und bewahren Sie deren Passwort getrennt auf.",
        "Noch keine Konten vorhanden. Fügen Sie eines manuell hinzu, scannen Sie den QR-Code eines Kontos oder eines Google-Authenticator-Exports oder stellen Sie in den Einstellungen eine verschlüsselte Sicherung wieder her.",
        "Erstes Konto einrichten")]
    [InlineData(
        "fr",
        "Créez un mot de passe principal pour chiffrer votre coffre-fort local. OTP Harbor ne peut pas le réinitialiser si vous l’oubliez. Créez ensuite une sauvegarde chiffrée et conservez son mot de passe séparément.",
        "Aucun compte pour le moment. Ajoutez-en un manuellement, scannez le QR code d’un compte ou d’une exportation Google Authenticator, ou restaurez une sauvegarde chiffrée depuis les paramètres.",
        "Configurez votre premier compte")]
    [InlineData(
        "es",
        "Cree una contraseña maestra para cifrar su almacén local. OTP Harbor no puede restablecerla si la olvida. Después, cree una copia de seguridad cifrada y guarde su contraseña por separado.",
        "Todavía no hay cuentas. Añada una manualmente, escanee el QR de una cuenta o exportación de Google Authenticator, o restaure una copia de seguridad cifrada desde Configuración.",
        "Configure su primera cuenta")]
    public void Get_OnboardingExplainsPasswordRecoveryAndFirstAccountOptions(
        string cultureName,
        string expectedSetupDescription,
        string expectedEmptyState,
        string expectedFirstAccountHeading)
    {
        var catalog = new MobileStringCatalog(CultureInfo.GetCultureInfo(cultureName));

        Assert.Equal(expectedSetupDescription, catalog.Get(MobileStringKeys.SetupDescription));
        Assert.Equal(expectedEmptyState, catalog.Get(MobileStringKeys.NoAccounts));
        Assert.Equal(expectedFirstAccountHeading, catalog.Get(MobileStringKeys.SetUpFirstAccount));
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

    [Theory]
    [InlineData("en", "Copied")]
    [InlineData("de", "Kopiert")]
    [InlineData("fr", "Copié")]
    [InlineData("es", "Copiado")]
    public void Get_CodeCopiedUsesConciseSelectedLocale(string cultureName, string expected)
    {
        var catalog = new MobileStringCatalog(CultureInfo.GetCultureInfo(cultureName));

        Assert.Equal(expected, catalog.Get(MobileStringKeys.CodeCopied));
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
