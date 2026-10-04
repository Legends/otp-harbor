using Avalonia.Controls;
using TOTP.Avalonia.Desktop.Localization;

namespace TOTP.Tests.Avalonia.Localization;

public sealed class AvaloniaLocalizationServiceTests
{
    [Fact]
    public void Catalog_DesktopSettingsUsesCompactEnglishTabNames()
    {
        var sut = new AvaloniaStringCatalog();
        var culture = System.Globalization.CultureInfo.GetCultureInfo("en");

        Assert.Equal("Back & Restore", sut.Get(AvaloniaStringKeys.Backups, culture));
        Assert.Equal("Imports", sut.Get(AvaloniaStringKeys.ImportExport, culture));
    }

    [Fact]
    public void ApplyCulture_UpdatesExistingDynamicResourceHostInPlace()
    {
        var resources = new ResourceDictionary();
        var sut = new AvaloniaLocalizationService(resources, new AvaloniaStringCatalog());
        var cultureChanged = 0;
        sut.CultureChanged += (_, _) => cultureChanged++;

        sut.ApplyCulture("de-DE");

        Assert.Equal("de", sut.CurrentLanguage.CultureName);
        Assert.Equal("Erneut versuchen", resources[AvaloniaStringKeys.Retry]);
        Assert.Equal("Master-Passwort", resources[AvaloniaStringKeys.MasterPassword]);
        Assert.Equal("Anzeigen", resources[AvaloniaStringKeys.RevealSecret]);
        Assert.Equal("Vorhandene Konten überspringen", resources[AvaloniaStringKeys.ImportSkipExisting]);
        Assert.Equal(1, cultureChanged);
    }

    [Theory]
    [InlineData("it-IT")]
    [InlineData("not-a-culture")]
    [InlineData("")]
    public void ApplyCulture_WhenUnsupported_FallsBackToEnglish(string cultureName)
    {
        var resources = new ResourceDictionary();
        var sut = new AvaloniaLocalizationService(resources, new AvaloniaStringCatalog());

        sut.ApplyCulture(cultureName);

        Assert.Equal("en", sut.CurrentLanguage.CultureName);
        Assert.Equal("Retry", resources[AvaloniaStringKeys.Retry]);
    }

    [Fact]
    public void Catalog_HasEveryDeclaredKeyInEverySupportedLanguage()
    {
        var sut = new AvaloniaStringCatalog();

        Assert.Empty(sut.GetMissingKeys(System.Globalization.CultureInfo.GetCultureInfo("en")));
        Assert.Empty(sut.GetMissingKeys(System.Globalization.CultureInfo.GetCultureInfo("de")));
        Assert.Empty(sut.GetMissingKeys(System.Globalization.CultureInfo.GetCultureInfo("fr")));
        Assert.Empty(sut.GetMissingKeys(System.Globalization.CultureInfo.GetCultureInfo("es")));
    }

    [Theory]
    [InlineData("en", "OTP Harbor")]
    [InlineData("de", "OTP Harbor")]
    [InlineData("fr", "OTP Harbor")]
    [InlineData("es", "OTP Harbor")]
    public void Catalog_AppTitleUsesStableProductName(string cultureName, string expected)
    {
        var sut = new AvaloniaStringCatalog();

        Assert.Equal(
                expected,
                sut.Get(AvaloniaStringKeys.AppTitle, System.Globalization.CultureInfo.GetCultureInfo(cultureName)));
    }

    [Theory]
    [InlineData("en", "Auto Lock", "Unlock using Biometric ID")]
    [InlineData("de", "Automatische Sperre", "Mit biometrischer ID entsperren")]
    [InlineData("fr", "Verrouillage automatique", "Déverrouiller avec l’identification biométrique")]
    [InlineData("es", "Bloqueo automático", "Desbloquear con identificación biométrica")]
    public void Catalog_SecuritySettingsUseLocalizedUserFacingNames(
        string cultureName,
        string expectedAutoLock,
        string expectedBiometricUnlock)
    {
        var sut = new AvaloniaStringCatalog();
        var culture = System.Globalization.CultureInfo.GetCultureInfo(cultureName);

        Assert.Equal(expectedAutoLock, sut.Get(AvaloniaStringKeys.IdleTimeout, culture));
        Assert.Equal(expectedBiometricUnlock, sut.Get(AvaloniaStringKeys.QuickUnlock, culture));
    }

    [Theory]
    [InlineData("en", "Export accounts")]
    [InlineData("de", "Konten exportieren")]
    [InlineData("fr", "Exporter les comptes")]
    [InlineData("es", "Exportar cuentas")]
    public void Catalog_ExportSectionUsesAccountFocusedHeading(string cultureName, string expected)
    {
        var sut = new AvaloniaStringCatalog();

        Assert.Equal(
            expected,
            sut.Get(AvaloniaStringKeys.EncryptedBackup, System.Globalization.CultureInfo.GetCultureInfo(cultureName)));
    }

    [Theory]
    [InlineData("en")]
    [InlineData("de")]
    [InlineData("fr")]
    [InlineData("es")]
    public void Catalog_OtherFormatImportHelpNamesEverySupportedFormat(string cultureName)
    {
        var sut = new AvaloniaStringCatalog();

        var culture = System.Globalization.CultureInfo.GetCultureInfo(cultureName);
        var help = string.Join('\n',
            sut.Get(AvaloniaStringKeys.OtherFormatsImportHelp, culture),
            sut.Get(AvaloniaStringKeys.OtherFormatAegis, culture),
            sut.Get(AvaloniaStringKeys.OtherFormatTwoFas, culture),
            sut.Get(AvaloniaStringKeys.OtherFormatOtpAuth, culture));

        Assert.Contains("Aegis", help, StringComparison.Ordinal);
        Assert.Contains("2FAS", help, StringComparison.Ordinal);
        Assert.Contains(".json", help, StringComparison.Ordinal);
        Assert.Contains(".txt", help, StringComparison.Ordinal);
        Assert.Contains(".2fas", help, StringComparison.Ordinal);
        Assert.Contains("otpauth://", help, StringComparison.Ordinal);
        Assert.Contains("1.", help, StringComparison.Ordinal);
        Assert.Contains("3.", help, StringComparison.Ordinal);
        Assert.DoesNotContain("OTP Harbor", help, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(".csv", help, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("\"entries\"", sut.Get(AvaloniaStringKeys.FaqImportFormatsAegisExample, culture));
        Assert.Contains("\"services\"", sut.Get(AvaloniaStringKeys.FaqImportFormatsTwoFasExample, culture));
        Assert.StartsWith("otpauth://", sut.Get(AvaloniaStringKeys.FaqImportFormatsOtpAuthExample, culture));
        Assert.False(string.IsNullOrWhiteSpace(sut.Get(AvaloniaStringKeys.FaqImportFormatsAegisDescription, culture)));
        Assert.False(string.IsNullOrWhiteSpace(sut.Get(AvaloniaStringKeys.FaqImportFormatsTwoFasDescription, culture)));
        Assert.False(string.IsNullOrWhiteSpace(sut.Get(AvaloniaStringKeys.FaqImportFormatsOtpAuthDescription, culture)));
        Assert.Contains("Simple Icons", sut.Get(AvaloniaStringKeys.FaqSimpleIconsOfficialLink, culture));
        Assert.Contains("Aegis", sut.Get(AvaloniaStringKeys.FaqAegisIconPackDocsLink, culture));
        Assert.Contains("OTP Harbor", sut.Get(AvaloniaStringKeys.FaqImportIconPacksDisclaimer, culture));
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
        "Crea una contraseña maestra para cifrar tu caja fuerte local. OTP Harbor no puede restablecerla si la olvidas. Después, crea una copia de seguridad cifrada y guarda su contraseña por separado.",
        "Todavía no hay cuentas. Añade una manualmente, escanea el QR de una cuenta o exportación de Google Authenticator, o restaura una copia de seguridad cifrada desde Ajustes.")]
    public void Catalog_OnboardingExplainsPasswordRecoveryAndFirstAccountOptions(
        string cultureName,
        string expectedSetupHelp,
        string expectedEmptyState)
    {
        var sut = new AvaloniaStringCatalog();
        var culture = System.Globalization.CultureInfo.GetCultureInfo(cultureName);

        Assert.Equal(expectedSetupHelp, sut.Get(AvaloniaStringKeys.PasswordSetupHelp, culture));
        Assert.Equal(expectedEmptyState, sut.Get(AvaloniaStringKeys.NoEntriesYet, culture));
    }

    [Theory]
    [InlineData("en", "Showing {0} of {1} accounts")]
    [InlineData("de", "{0} von {1} Konten angezeigt")]
    [InlineData("fr", "{0} comptes affichés sur {1}")]
    [InlineData("es", "Mostrando {0} de {1} cuentas")]
    public void Catalog_SearchResultSummaryUsesCompleteSelectedLocale(
        string cultureName,
        string expected)
    {
        var sut = new AvaloniaStringCatalog();

        Assert.Equal(
            expected,
            sut.Get(
                AvaloniaStringKeys.SearchResultsFormat,
                System.Globalization.CultureInfo.GetCultureInfo(cultureName)));
    }

    [Theory]
    [InlineData("en", "Issuer ↑", "Issuer ↓", "Account ↑", "Account ↓")]
    [InlineData("de", "Anbieter ↑", "Anbieter ↓", "Konto ↑", "Konto ↓")]
    [InlineData("fr", "Émetteur ↑", "Émetteur ↓", "Compte ↑", "Compte ↓")]
    [InlineData("es", "Emisor ↑", "Emisor ↓", "Cuenta ↑", "Cuenta ↓")]
    public void Catalog_AccountSortOptionsUseCompleteSelectedLocale(
        string cultureName,
        string expectedIssuerSort,
        string expectedIssuerDescendingSort,
        string expectedAccountSort,
        string expectedAccountDescendingSort)
    {
        var sut = new AvaloniaStringCatalog();
        var culture = System.Globalization.CultureInfo.GetCultureInfo(cultureName);

        Assert.Equal(expectedIssuerSort, sut.Get(AvaloniaStringKeys.SortByIssuer, culture));
        Assert.Equal(
            expectedIssuerDescendingSort,
            sut.Get(AvaloniaStringKeys.SortByIssuerDescending, culture));
        Assert.Equal(expectedAccountSort, sut.Get(AvaloniaStringKeys.SortByAccountName, culture));
        Assert.Equal(
            expectedAccountDescendingSort,
            sut.Get(AvaloniaStringKeys.SortByAccountNameDescending, culture));
    }

    [Theory]
    [InlineData("en", "Clear search")]
    [InlineData("de", "Suche löschen")]
    [InlineData("fr", "Effacer la recherche")]
    [InlineData("es", "Borrar búsqueda")]
    public void Catalog_ClearSearchUsesSelectedLocale(string cultureName, string expected)
    {
        var sut = new AvaloniaStringCatalog();

        Assert.Equal(
            expected,
            sut.Get(
                AvaloniaStringKeys.ClearSearch,
                System.Globalization.CultureInfo.GetCultureInfo(cultureName)));
    }

    [Theory]
    [InlineData("en", "Third-party icon packs and custom icons are imported and stored locally by the user. OTP Harbor does not provide or distribute these assets.\n\nAll trademarks, logos, copyrights, licenses, and usage guidelines remain the responsibility of their respective owners and apply independently of OTP Harbor.\n\nCompatibility with an icon pack does not imply affiliation with, sponsorship by, or endorsement by its provider or any trademark owner.")]
    [InlineData("de", "Symbolpakete und benutzerdefinierte Symbole von Drittanbietern werden vom Benutzer importiert und lokal gespeichert. OTP Harbor stellt diese Inhalte weder bereit noch verbreitet sie.\n\nAlle Marken, Logos, Urheberrechte, Lizenzen und Nutzungsrichtlinien verbleiben in der Verantwortung ihrer jeweiligen Eigentümer und gelten unabhängig von OTP Harbor.\n\nDie Kompatibilität mit einem Symbolpaket bedeutet weder eine Verbindung oder Förderung noch eine Empfehlung durch dessen Anbieter oder einen Markeninhaber.")]
    [InlineData("fr", "Les packs d’icônes tiers et les icônes personnalisées sont importés et stockés localement par l’utilisateur. OTP Harbor ne fournit ni ne distribue ces éléments.\n\nToutes les marques, tous les logos, droits d’auteur, licences et directives d’utilisation restent sous la responsabilité de leurs propriétaires respectifs et s’appliquent indépendamment d’OTP Harbor.\n\nLa compatibilité avec un pack d’icônes n’implique aucune affiliation, aucun parrainage ni aucune approbation de son fournisseur ou d’un détenteur de marque.")]
    [InlineData("es", "Los paquetes de iconos de terceros y los iconos personalizados son importados y almacenados localmente por el usuario. OTP Harbor no proporciona ni distribuye estos recursos.\n\nTodas las marcas, logotipos, derechos de autor, licencias y directrices de uso siguen siendo responsabilidad de sus respectivos propietarios y se aplican independientemente de OTP Harbor.\n\nLa compatibilidad con un paquete de iconos no implica afiliación, patrocinio ni respaldo por parte de su proveedor ni de ningún titular de marca.")]
    public void Catalog_BrandIconImportNoticeUsesCompleteSelectedLocale(
        string cultureName,
        string expected)
    {
        var sut = new AvaloniaStringCatalog();

        Assert.Equal(
            expected,
            sut.Get(
                AvaloniaStringKeys.BrandIconsHelp,
                System.Globalization.CultureInfo.GetCultureInfo(cultureName)));
    }

    [Theory]
    [InlineData(
        "en",
        "Add account (Ctrl+A)",
        "Search issuer or account (Ctrl+F)",
        "Edit account (Ctrl+E)",
        "Delete account (Ctrl+D or Delete)",
        "Copy with timed clear (Ctrl+C)",
        "Lock (Ctrl+L)")]
    [InlineData(
        "de",
        "Konto hinzufügen (Strg+A)",
        "Aussteller oder Konto suchen (Strg+F)",
        "Konto bearbeiten (Strg+E)",
        "Konto löschen (Strg+D oder Entf)",
        "Kopieren und zeitgesteuert löschen (Strg+C)",
        "Sperren (Strg+L)")]
    [InlineData(
        "fr",
        "Ajouter un compte (Ctrl+A)",
        "Rechercher un émetteur ou un compte (Ctrl+F)",
        "Modifier le compte (Ctrl+E)",
        "Supprimer le compte (Ctrl+D ou Suppr)",
        "Copier avec effacement différé (Ctrl+C)",
        "Verrouiller (Ctrl+L)")]
    [InlineData(
        "es",
        "Añadir cuenta (Ctrl+A)",
        "Buscar emisor o cuenta (Ctrl+F)",
        "Editar cuenta (Ctrl+E)",
        "Eliminar cuenta (Ctrl+D o Supr)",
        "Copiar con borrado programado (Ctrl+C)",
        "Bloquear (Ctrl+L)")]
    public void Catalog_ShortcutHintsUseCompleteSelectedLocale(
        string cultureName,
        string expectedAdd,
        string expectedSearch,
        string expectedEdit,
        string expectedDelete,
        string expectedCopy,
        string expectedLock)
    {
        var sut = new AvaloniaStringCatalog();
        var culture = System.Globalization.CultureInfo.GetCultureInfo(cultureName);

        Assert.Equal(expectedAdd, sut.Get(AvaloniaStringKeys.AddAccountShortcut, culture));
        Assert.Equal(expectedSearch, sut.Get(AvaloniaStringKeys.SearchAccountsShortcut, culture));
        Assert.Equal(expectedEdit, sut.Get(AvaloniaStringKeys.EditAccountShortcut, culture));
        Assert.Equal(expectedDelete, sut.Get(AvaloniaStringKeys.DeleteAccountShortcut, culture));
        Assert.Equal(expectedCopy, sut.Get(AvaloniaStringKeys.CopyTimedClearShortcut, culture));
        Assert.Equal(expectedLock, sut.Get(AvaloniaStringKeys.LockShortcut, culture));
    }

    [Theory]
    [InlineData("fr-FR", "fr", "Réessayer", "Mot de passe principal")]
    [InlineData("es-ES", "es", "Reintentar", "Contraseña maestra")]
    public void ApplyCulture_ForAdditionalLanguage_UsesCompleteLocale(
        string requestedCulture,
        string expectedCulture,
        string expectedRetry,
        string expectedPassword)
    {
        var resources = new ResourceDictionary();
        var sut = new AvaloniaLocalizationService(resources, new AvaloniaStringCatalog());

        sut.ApplyCulture(requestedCulture);

        Assert.Equal(expectedCulture, sut.CurrentLanguage.CultureName);
        Assert.Equal(expectedRetry, resources[AvaloniaStringKeys.Retry]);
        Assert.Equal(expectedPassword, resources[AvaloniaStringKeys.MasterPassword]);
        Assert.Equal(4, sut.SupportedLanguages.Count);
        Assert.Equal(
            ["English", "Deutsch", "Français", "Español"],
            sut.SupportedLanguages.Select(value => value.DisplayName));
    }
}
