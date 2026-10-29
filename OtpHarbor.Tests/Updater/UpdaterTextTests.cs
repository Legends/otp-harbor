using System.Globalization;
using TOTP.Tests.Common;

namespace TOTP.Tests.Updater;

[Collection(NonParallelCollectionDefinition.NonParallel)]
public sealed class UpdaterTextTests
{
    [Fact]
    public void GermanCulture_UsesCompleteLocalizedUpdaterMessages()
    {
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de-DE");

            Assert.Equal(
                "Das signierte Update-Paket wird angewendet und die App neu gestartet.",
                TOTP.Updater.UpdaterText.HeaderDescription);
            Assert.Equal("3 Datei(en) in Warteschlange", TOTP.Updater.UpdaterText.FilesQueued(3));
            Assert.Equal("Schließen", TOTP.Updater.UpdaterText.Close);
            Assert.Equal(
                "Prüfen Sie das Updater-Protokoll auf Diagnosedetails und versuchen Sie es erneut.",
                TOTP.Updater.UpdaterText.UpdateFailedDetail);
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }

    [Theory]
    [InlineData(
        "fr-FR",
        "Application du paquet de mise à jour signé et redémarrage de l’application.",
        "3 fichier(s) en attente",
        "Fermer")]
    [InlineData(
        "es-ES",
        "Aplicando el paquete de actualización firmado y reiniciando la aplicación.",
        "3 archivo(s) en espera",
        "Cerrar")]
    public void AdditionalCulture_UsesCompleteLocalizedUpdaterMessages(
        string cultureName,
        string expectedDescription,
        string expectedFilesQueued,
        string expectedClose)
    {
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(cultureName);

            Assert.Equal(expectedDescription, TOTP.Updater.UpdaterText.HeaderDescription);
            Assert.Equal(expectedFilesQueued, TOTP.Updater.UpdaterText.FilesQueued(3));
            Assert.Equal(expectedClose, TOTP.Updater.UpdaterText.Close);
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }
}
