using System.Globalization;
using System.Xml.Linq;
using Avalonia.Platform;

namespace TOTP.Installer.Localization;

internal sealed class InstallerText
{
    private readonly IReadOnlyDictionary<string, string> _values;

    private InstallerText(IReadOnlyDictionary<string, string> values, string licenseBody)
    {
        _values = values;
        LicenseBody = licenseBody;
    }

    internal static InstallerText LoadCurrent()
    {
        var culture = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName switch
        {
            "de" => "de-de",
            "es" => "es-es",
            "fr" => "fr-fr",
            _ => "en-us"
        };
        var uri = new Uri($"avares://TOTP.Installer/Localization/Setup.{culture}.wxl");
        using var stream = AssetLoader.Open(uri);
        var document = XDocument.Load(stream);
        var values = document.Root?.Elements()
            .Where(element => element.Name.LocalName == "String")
            .ToDictionary(
                element => (string?)element.Attribute("Id") ?? string.Empty,
                element => (string?)element.Attribute("Value") ?? string.Empty,
                StringComparer.Ordinal)
            ?? throw new InvalidDataException("The installer localization resource is empty.");
        var licenseUri = new Uri($"avares://TOTP.Installer/Localization/License.{culture}.txt");
        using var licenseStream = AssetLoader.Open(licenseUri);
        using var reader = new StreamReader(licenseStream);
        return new InstallerText(values, reader.ReadToEnd());
    }

    private string Get(string key) =>
        _values.TryGetValue(key, out var value) ? value : throw new InvalidDataException($"Missing installer localization key: {key}");

    public string Caption => Get("Caption");
    public string Tagline => Get("Tagline");
    public string Principles => Get("Principles");
    public string Kicker => Get("Kicker");
    public string Loading => Get("Loading");
    public string InstallHeader => Get("InstallHeader");
    public string InstallMessage => Get("InstallMessage");
    public string ReadLicense => Get("ReadLicense");
    public string AcceptLicense => Get("AcceptLicense");
    public string Location => Get("Location");
    public string Install => Get("Install");
    public string Close => Get("Close");
    public string Minimize => Get("Minimize");
    public string LicenseHeader => Get("LicenseHeader");
    public string LicenseBody { get; }
    public string Back => Get("Back");
    public string Browse => Get("Browse");
    public string ProgressHeader => Get("ProgressHeader");
    public string ProgressMessage => Get("ProgressMessage");
    public string Cancel => Get("Cancel");
    public string ModifyHeader => Get("ModifyHeader");
    public string ModifyMessage => Get("ModifyMessage");
    public string Repair => Get("Repair");
    public string Uninstall => Get("Uninstall");
    public string SuccessHeader => Get("SuccessHeader");
    public string SuccessMessage => Get("SuccessMessage");
    public string Launch => Get("Launch");
    public string FailureHeader => Get("FailureHeader");
    public string FailureMessage => Get("FailureMessage");
    public string ConfirmCancelMessage => Get("ConfirmCancelMessage");
}
