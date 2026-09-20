using Avalonia.Controls;
using TOTP.Avalonia.Desktop.Localization;

namespace TOTP.Tests.Avalonia.Localization;

public sealed class AvaloniaLocalizationServiceTests
{
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
    [InlineData("en", "OTP Harbor includes no third-party logos and is not affiliated with or endorsed by Simple Icons, icon-pack providers, or depicted brands. Import only a ZIP you obtained independently and are authorized to use. Imported artwork stays in local app data; no issuer or account information is uploaded.")]
    [InlineData("de", "OTP Harbor enthält keine Logos Dritter und ist weder mit Simple Icons, Anbietern von Symbolpaketen noch mit dargestellten Marken verbunden oder von ihnen unterstützt. Importieren Sie nur eine unabhängig bezogene ZIP-Datei, zu deren Nutzung Sie berechtigt sind. Importierte Grafiken verbleiben in den lokalen App-Daten; Anbieter- oder Kontoinformationen werden nicht hochgeladen.")]
    [InlineData("fr", "OTP Harbor n’inclut aucun logo tiers et n’est ni affilié ni approuvé par Simple Icons, les fournisseurs de packs d’icônes ou les marques représentées. Importez uniquement une archive ZIP obtenue indépendamment et que vous êtes autorisé à utiliser. Les images importées restent dans les données locales de l’application ; aucune information d’émetteur ou de compte n’est transmise.")]
    [InlineData("es", "OTP Harbor no incluye logotipos de terceros ni está afiliado o respaldado por Simple Icons, los proveedores de paquetes de iconos o las marcas representadas. Importa únicamente un archivo ZIP obtenido de forma independiente y para cuyo uso tengas autorización. Las imágenes importadas permanecen en los datos locales de la aplicación; no se transmite información de emisores o cuentas.")]
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
