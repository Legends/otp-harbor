using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace TOTP.Updater;

internal sealed class UpdateInstallerViewModel : INotifyPropertyChanged
{
    private readonly UpdateInstallerService _installerService;
    private readonly RelayCommand _closeCommand;

    private string _titleText = UpdaterText.InstallingUpdate;
    private string _statusText = UpdaterText.PreparingUpdater;
    private string _detailText = string.Empty;
    private string _progressText = string.Empty;
    private bool _isIndeterminate = true;
    private int _progressValue;
    private bool _isCloseEnabled;

    public UpdateInstallerViewModel(UpdateInstallerService installerService)
    {
        _installerService = installerService;
        _closeCommand = new RelayCommand(() => RequestClose?.Invoke(this, EventArgs.Empty), () => IsCloseEnabled);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? RequestClose;

    public string TitleText
    {
        get => _titleText;
        private set => SetProperty(ref _titleText, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string DetailText
    {
        get => _detailText;
        private set => SetProperty(ref _detailText, value);
    }

    public string ProgressText
    {
        get => _progressText;
        private set => SetProperty(ref _progressText, value);
    }

    public bool IsIndeterminate
    {
        get => _isIndeterminate;
        private set => SetProperty(ref _isIndeterminate, value);
    }

    public int ProgressValue
    {
        get => _progressValue;
        private set => SetProperty(ref _progressValue, value);
    }

    public bool IsCloseEnabled
    {
        get => _isCloseEnabled;
        private set
        {
            if (SetProperty(ref _isCloseEnabled, value))
            {
                _closeCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public ICommand CloseCommand => _closeCommand;

    public async Task RunInstallAsync()
    {
        try
        {
            var progress = new Progress<InstallerProgressState>(ApplyState);
            await _installerService.RunAsync(progress);
            RequestClose?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _installerService.LogFailure(ex);
            ApplyState(new InstallerProgressState
            {
                Title = UpdaterText.UpdateFailedTitle,
                Status = UpdaterText.UpdateFailedStatus,
                Detail = UpdaterText.UpdateFailedDetail,
                ProgressText = string.Empty,
                IsIndeterminate = false,
                ProgressValue = 0,
                IsCloseEnabled = true
            });
        }
    }

    private void ApplyState(InstallerProgressState state)
    {
        TitleText = state.Title;
        StatusText = state.Status;
        DetailText = state.Detail;
        ProgressText = state.ProgressText;
        IsIndeterminate = state.IsIndeterminate;
        ProgressValue = state.ProgressValue;
        IsCloseEnabled = state.IsCloseEnabled;
    }

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}
