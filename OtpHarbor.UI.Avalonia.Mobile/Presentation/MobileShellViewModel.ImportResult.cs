using TOTP.Avalonia.Mobile.Localization;

namespace TOTP.Avalonia.Mobile.Presentation;

public sealed partial class MobileShellViewModel
{
    public bool IsFileImportResult => _fileImportResult is not null;
    public string FileImportResultSubtitleText => Get(MobileStringKeys.FileImportResultSubtitle);
    public string ImportAccountsAddedText => Get(MobileStringKeys.ImportAccountsAdded);
    public string ImportAccountsOverwrittenText => Get(MobileStringKeys.ImportAccountsOverwritten);
    public string ImportAccountsUnchangedText => Get(MobileStringKeys.ImportAccountsUnchanged);
    public string ImportResultDismissText => IsFileImportResult ? Get(MobileStringKeys.ImportResultDone) : CloseText;
    public string ImportAccountsAddedCount => (_fileImportResult?.Added ?? 0).ToString("N0", _strings.Culture);
    public string ImportAccountsOverwrittenCount => (_fileImportResult?.Replaced ?? 0).ToString("N0", _strings.Culture);
    public string ImportAccountsUnchangedCount => (_fileImportResult?.Skipped ?? 0).ToString("N0", _strings.Culture);

    private void NotifyImportResultChanged()
    {
        OnPropertyChanged(nameof(IsFileImportResult));
        OnPropertyChanged(nameof(QrImportResultTitle));
        OnPropertyChanged(nameof(ImportResultDismissText));
        OnPropertyChanged(nameof(ImportAccountsAddedCount));
        OnPropertyChanged(nameof(ImportAccountsOverwrittenCount));
        OnPropertyChanged(nameof(ImportAccountsUnchangedCount));
    }

    private void ClearImportResult()
    {
        IsQrImportResultVisible = false;
        QrImportResultText = string.Empty;
        _fileImportResult = null;
        NotifyImportResultChanged();
    }
}
