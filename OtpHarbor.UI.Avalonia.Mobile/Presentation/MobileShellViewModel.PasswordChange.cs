using System.Windows.Input;
using TOTP.Avalonia.Mobile.Localization;

namespace TOTP.Avalonia.Mobile.Presentation;

public sealed partial class MobileShellViewModel
{
    private readonly MobileAsyncCommand _beginMasterPasswordChangeCommand;
    private readonly MobileAsyncCommand _cancelMasterPasswordChangeCommand;
    private bool _isMasterPasswordChangeExpanded;

    public ICommand BeginMasterPasswordChangeCommand => _beginMasterPasswordChangeCommand;
    public ICommand CancelMasterPasswordChangeCommand => _cancelMasterPasswordChangeCommand;
    public string ChangeText => Get(MobileStringKeys.Change);
    public bool IsMasterPasswordChangeExpanded
    {
        get => _isMasterPasswordChangeExpanded;
        private set
        {
            if (!SetField(ref _isMasterPasswordChangeExpanded, value)) return;
            _beginMasterPasswordChangeCommand?.NotifyCanExecuteChanged();
            _cancelMasterPasswordChangeCommand?.NotifyCanExecuteChanged();
            _changeMasterPasswordCommand?.NotifyCanExecuteChanged();
        }
    }

    public Task BeginMasterPasswordChangeAsync()
    {
        if (!IsSecuritySettingsVisible || IsBusy || IsMasterPasswordChangeExpanded) return Task.CompletedTask;
        ClearMasterPasswordChangeInputs();
        ClearNotification();
        IsMasterPasswordChangeExpanded = true;
        return Task.CompletedTask;
    }

    public Task CancelMasterPasswordChangeAsync()
    {
        if (IsBusy) return Task.CompletedTask;
        CloseMasterPasswordChangeEditor();
        ClearNotification();
        return Task.CompletedTask;
    }

    private void CloseMasterPasswordChangeEditor()
    {
        ClearMasterPasswordChangeInputs();
        IsMasterPasswordChangeExpanded = false;
    }
}
