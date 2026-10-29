using System.Globalization;
using System.Resources;

namespace TOTP.Updater;

public static class UpdaterText
{
    private static readonly ResourceManager Resources = new(
        "TOTP.Updater.Localization.UpdaterStrings",
        typeof(UpdaterText).Assembly);

    public static string WindowTitle => Get(nameof(WindowTitle));
    public static string HeaderTitle => Get(nameof(HeaderTitle));
    public static string HeaderDescription => Get(nameof(HeaderDescription));
    public static string CurrentStep => Get(nameof(CurrentStep));
    public static string OverallProgress => Get(nameof(OverallProgress));
    public static string Close => Get(nameof(Close));

    public static string InstallingUpdate => Get(nameof(InstallingUpdate));
    public static string PreparingUpdater => Get(nameof(PreparingUpdater));
    public static string ClosingApp => Get(nameof(ClosingApp));
    public static string WaitingForAppClose => Get(nameof(WaitingForAppClose));
    public static string StagingPackage => Get(nameof(StagingPackage));
    public static string InstallingFiles => Get(nameof(InstallingFiles));
    public static string RelaunchingApp => Get(nameof(RelaunchingApp));
    public static string FinalizingCopiedFiles => Get(nameof(FinalizingCopiedFiles));
    public static string Complete100 => Get(nameof(Complete100));
    public static string UpdateFailedTitle => Get(nameof(UpdateFailedTitle));
    public static string UpdateFailedStatus => Get(nameof(UpdateFailedStatus));
    public static string UpdateFailedDetail => Get(nameof(UpdateFailedDetail));
    public static string StartupFailedTitle => WindowTitle;
    public static string StartupFailedMessage => Get(nameof(StartupFailedMessage));

    public static string FilesQueued(int count) => Format(nameof(FilesQueued), count);
    public static string ItemsQueued(int count) => Format(nameof(ItemsQueued), count);
    public static string FileCountProgress(int copiedFiles, int totalFiles) =>
        Format(nameof(FileCountProgress), copiedFiles, totalFiles);
    public static string PercentComplete(int percentage) => Format(nameof(PercentComplete), percentage);

    private static string Get(string key) =>
        Resources.GetString(key, CultureInfo.CurrentUICulture)
        ?? Resources.GetString(key, CultureInfo.GetCultureInfo("en"))
        ?? key;

    private static string Format(string key, params object[] arguments) =>
        string.Format(CultureInfo.CurrentUICulture, Get(key), arguments);
}
