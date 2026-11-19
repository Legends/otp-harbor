using System.Globalization;
using TOTP.Localization;

namespace TOTP.Updater;

public static class UpdaterText
{
    private static readonly LocalizationCatalog Resources = new(LocalizationScope.Updater);

    public static string WindowTitle => Get(UpdaterStringKeys.WindowTitle);
    public static string HeaderTitle => Get(UpdaterStringKeys.HeaderTitle);
    public static string HeaderDescription => Get(UpdaterStringKeys.HeaderDescription);
    public static string CurrentStep => Get(UpdaterStringKeys.CurrentStep);
    public static string OverallProgress => Get(UpdaterStringKeys.OverallProgress);
    public static string Close => Get(UpdaterStringKeys.Close);

    public static string InstallingUpdate => Get(UpdaterStringKeys.InstallingUpdate);
    public static string PreparingUpdater => Get(UpdaterStringKeys.PreparingUpdater);
    public static string ClosingApp => Get(UpdaterStringKeys.ClosingApp);
    public static string WaitingForAppClose => Get(UpdaterStringKeys.WaitingForAppClose);
    public static string StagingPackage => Get(UpdaterStringKeys.StagingPackage);
    public static string InstallingFiles => Get(UpdaterStringKeys.InstallingFiles);
    public static string RelaunchingApp => Get(UpdaterStringKeys.RelaunchingApp);
    public static string FinalizingCopiedFiles => Get(UpdaterStringKeys.FinalizingCopiedFiles);
    public static string Complete100 => Get(UpdaterStringKeys.Complete100);
    public static string UpdateFailedTitle => Get(UpdaterStringKeys.UpdateFailedTitle);
    public static string UpdateFailedStatus => Get(UpdaterStringKeys.UpdateFailedStatus);
    public static string UpdateFailedDetail => Get(UpdaterStringKeys.UpdateFailedDetail);
    public static string StartupFailedTitle => WindowTitle;
    public static string StartupFailedMessage => Get(UpdaterStringKeys.StartupFailedMessage);

    public static string FilesQueued(int count) => Format(UpdaterStringKeys.FilesQueued, count);
    public static string ItemsQueued(int count) => Format(UpdaterStringKeys.ItemsQueued, count);
    public static string FileCountProgress(int copiedFiles, int totalFiles) =>
        Format(UpdaterStringKeys.FileCountProgress, copiedFiles, totalFiles);
    public static string PercentComplete(int percentage) => Format(UpdaterStringKeys.PercentComplete, percentage);

    private static string Get(string key) =>
        Resources.Get(key, CultureInfo.CurrentUICulture);

    private static string Format(string key, params object[] arguments) =>
        string.Format(CultureInfo.CurrentUICulture, Get(key), arguments);
}
