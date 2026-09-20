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
