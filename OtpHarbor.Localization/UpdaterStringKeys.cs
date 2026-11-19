namespace TOTP.Localization;

public static class UpdaterStringKeys
{
    public const string WindowTitle = nameof(WindowTitle);
    public const string HeaderTitle = nameof(HeaderTitle);
    public const string HeaderDescription = nameof(HeaderDescription);
    public const string CurrentStep = nameof(CurrentStep);
    public const string OverallProgress = nameof(OverallProgress);
    public const string Close = CommonStringKeys.Close;
    public const string InstallingUpdate = nameof(InstallingUpdate);
    public const string PreparingUpdater = nameof(PreparingUpdater);
    public const string ClosingApp = nameof(ClosingApp);
    public const string WaitingForAppClose = nameof(WaitingForAppClose);
    public const string StagingPackage = nameof(StagingPackage);
    public const string InstallingFiles = nameof(InstallingFiles);
    public const string RelaunchingApp = nameof(RelaunchingApp);
    public const string FinalizingCopiedFiles = nameof(FinalizingCopiedFiles);
    public const string Complete100 = nameof(Complete100);
    public const string UpdateFailedTitle = nameof(UpdateFailedTitle);
    public const string UpdateFailedStatus = nameof(UpdateFailedStatus);
    public const string UpdateFailedDetail = nameof(UpdateFailedDetail);
    public const string StartupFailedMessage = nameof(StartupFailedMessage);
    public const string FilesQueued = nameof(FilesQueued);
    public const string ItemsQueued = nameof(ItemsQueued);
    public const string FileCountProgress = nameof(FileCountProgress);
    public const string PercentComplete = nameof(PercentComplete);

    public static IReadOnlyList<string> All { get; } =
    [
        WindowTitle, HeaderTitle, HeaderDescription, CurrentStep, OverallProgress, Close,
        InstallingUpdate, PreparingUpdater, ClosingApp, WaitingForAppClose, StagingPackage,
        InstallingFiles, RelaunchingApp, FinalizingCopiedFiles, Complete100, UpdateFailedTitle,
        UpdateFailedStatus, UpdateFailedDetail, StartupFailedMessage, FilesQueued, ItemsQueued,
        FileCountProgress, PercentComplete
    ];
}
