using System.ComponentModel;
using System.Windows.Input;
using System.Globalization;
using TOTP.Core.Services.Models;

namespace TOTP.Avalonia.Shared.Import;

public enum AuthenticatorImportSource
{
    TwoFas, Aegis, Google, BitwardenAuthenticator, BitwardenVault, Ente, Proton, KeePassXC, Microsoft, Other
}

public sealed class AuthenticatorImportViewModel : INotifyPropertyChanged
{
    private readonly Func<string, string> _localize;
    private SourceOption? _selectedSource;
    private TaskCompletionSource<bool>? _fileSummaryCompletion;
    private AccountImportReadSummary? _fileSummary;
    private string _fileName = string.Empty;
    private TaskCompletionSource<AccountImportFormat?>? _formatSelectionCompletion;
    private FormatOption? _selectedFormat;

    public AuthenticatorImportViewModel(Func<string, string> localize, ICommand fileCommand, ICommand qrCommand)
    {
        _localize = localize;
        FileCommand = fileCommand;
        QrCommand = qrCommand;
        Sources = Enum.GetValues<AuthenticatorImportSource>().Select(source => new SourceOption(this, source)).ToArray();
        ReviewFileCommand = new FileActionCommand(() => _fileSummaryCompletion?.TrySetResult(true));
        ClearFileCommand = new FileActionCommand(CancelPendingFile);
        UseFormatCommand = new FileActionCommand(() => _formatSelectionCompletion?.TrySetResult(SelectedFormat?.Format), () => SelectedFormat is not null);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public IReadOnlyList<SourceOption> Sources { get; }
    public ICommand FileCommand { get; }
    public ICommand QrCommand { get; }
    public ICommand ReviewFileCommand { get; }
    public ICommand ClearFileCommand { get; }
    public ICommand UseFormatCommand { get; }
    public bool HasFormatChoice => _formatSelectionCompletion is not null;
    public IReadOnlyList<FormatOption> FormatOptions { get; private set; } = [];
    public FormatOption? SelectedFormat
    {
        get => _selectedFormat;
        set { if (value is not null && !FormatOptions.Contains(value)) return; _selectedFormat = value; ((FileActionCommand)UseFormatCommand).Refresh(); NotifyChanged(); }
    }
    public string AmbiguousFormatText => Get(FileImportStringKeys.AmbiguousFormat);
    public string ChooseFormatText => Get(FileImportStringKeys.ChooseFormat);
    public string UseFormatText => Get(FileImportStringKeys.UseFormat);
    public SourceOption? SelectedSource
    {
        get => _selectedSource;
        set
        {
            if (value is not null && !Sources.Contains(value) || value == _selectedSource) return;
            _selectedSource = value;
            NotifyChanged();
        }
    }

    public string Title => Get(MigrationStringKeys.Title);
    public string Description => Get(MigrationStringKeys.Description);
    public string ChooseSourceText => Get(MigrationStringKeys.ChooseSource);
    public string InstructionsTitle => Get(MigrationStringKeys.InstructionsTitle);
    public bool HasGuidance => SelectedSource is not null;
    public string Instructions => SelectedSource is null ? string.Empty : Get(MigrationStringKeys.Guide(SelectedSource.Source));
    public string SupportNotice => Get(SelectedSource?.Source is AuthenticatorImportSource.Microsoft or AuthenticatorImportSource.KeePassXC ? MigrationStringKeys.GuideOnly
        : SelectedSource?.Source == AuthenticatorImportSource.Google ? MigrationStringKeys.QrOnly : MigrationStringKeys.PlaintextOnly);
    public string SelectExportText => Get(MigrationStringKeys.SelectExport);
    public string FileText => Get(MigrationStringKeys.ChooseFile);
    public string QrText => Get(MigrationStringKeys.ChooseQr);
    public string ReviewText => Get(MigrationStringKeys.ReviewNext);
    public string LocalProcessingText => Get(MigrationStringKeys.LocalProcessing);
    public bool CanImportFile => true;
    public bool CanImportQr => true;
    public bool HasImportMethod => CanImportFile || CanImportQr;
    public string FileTitle => Get(FileImportStringKeys.FileTitle);
    public string AutoDetectionText => Get(FileImportStringKeys.AutoDetection);
    public string SupportedFormatsText => Get(FileImportStringKeys.SupportedFormats);
    public string HelpTitle => Get(FileImportStringKeys.HelpTitle);
    public string HelpDescription => Get(FileImportStringKeys.HelpDescription);
    public string OptionalSourceText => Get(FileImportStringKeys.OptionalSource);
    public bool HasSelectedFile => _fileSummaryCompletion is not null;
    public string SelectedFileName => _fileName;
    public string DetectedFormatText => string.Format(CultureInfo.CurrentCulture, Get(FileImportStringKeys.DetectedFormat), GetFormatName(_fileSummary?.Format ?? AccountImportFormat.Unknown));
    public string GetFormatName(AccountImportFormat format) => Get(FileImportStringKeys.Format(format));
    public int TotalEntries => (_fileSummary?.SupportedCount ?? 0) + AttentionEntries;
    public int ValidEntries => _fileSummary?.SupportedCount ?? 0;
    public int AttentionEntries => (_fileSummary?.InvalidCount ?? 0) + (_fileSummary?.UnsupportedCount ?? 0) + (_fileSummary?.NonOtpCount ?? 0);
    public string TotalEntriesText => Get(FileImportStringKeys.TotalEntries);
    public string ValidEntriesText => Get(FileImportStringKeys.ValidEntries);
    public string AttentionEntriesText => Get(FileImportStringKeys.AttentionEntries);
    public string ReviewEntriesText => string.Format(CultureInfo.CurrentCulture, Get(FileImportStringKeys.ReviewEntries), TotalEntries);
    public string ClearFileText => Get(FileImportStringKeys.ClearFile);

    public void CancelPendingFile()
    {
        _fileSummaryCompletion?.TrySetResult(false);
        _formatSelectionCompletion?.TrySetResult(null);
    }
    public async Task<AccountImportFormat?> ChooseDetectedFormatAsync(IReadOnlyList<AccountImportFormat> candidates,
        string fileName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CancelPendingFile();
        var completion = new TaskCompletionSource<AccountImportFormat?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _formatSelectionCompletion = completion;
        _fileName = ImportReviewViewModel.Safe(fileName);
        FormatOptions = candidates.Select(format => new FormatOption(this, format)).ToArray();
        SelectedFormat = null;
        using var cancellation = cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
        try { return await completion.Task; }
        finally
        {
            if (ReferenceEquals(_formatSelectionCompletion, completion))
            {
                _formatSelectionCompletion = null;
                FormatOptions = [];
                _fileName = string.Empty;
                SelectedFormat = null;
            }
        }
    }
    public async Task<bool> WaitForFileReviewAsync(AccountImportReadSummary summary, string fileName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CancelPendingFile();
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _fileSummaryCompletion = completion;
        _fileSummary = summary;
        _fileName = ImportReviewViewModel.Safe(fileName);
        NotifyChanged();
        using var cancellation = cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
        try { return await completion.Task; }
        finally
        {
            if (ReferenceEquals(_fileSummaryCompletion, completion))
            {
                _fileSummaryCompletion = null;
                _fileSummary = null;
                _fileName = string.Empty;
                NotifyChanged();
            }
        }
    }

    public void RefreshLanguage()
    {
        foreach (var source in Sources) source.RefreshLanguage();
        foreach (var option in FormatOptions) option.RefreshLanguage();
        NotifyChanged();
    }

    private string Get(string key) => _localize(key);
    private void NotifyChanged() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));

    public sealed class SourceOption(AuthenticatorImportViewModel owner, AuthenticatorImportSource source) : INotifyPropertyChanged
    {
        public AuthenticatorImportSource Source { get; } = source;
        public string Name => owner.Get(MigrationStringKeys.Source(Source));
        public event PropertyChangedEventHandler? PropertyChanged;
        internal void RefreshLanguage() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
    }

    public sealed class FormatOption(AuthenticatorImportViewModel owner, AccountImportFormat format) : INotifyPropertyChanged
    {
        public AccountImportFormat Format { get; } = format;
        public string Name => owner.GetFormatName(Format);
        public event PropertyChangedEventHandler? PropertyChanged;
        internal void RefreshLanguage() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
    }

    private sealed class FileActionCommand(Action execute, Func<bool>? canExecute = null) : ICommand
    {
        public event EventHandler? CanExecuteChanged;
        public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;
        public void Execute(object? parameter) { if (CanExecute(parameter)) execute(); }
        public void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
