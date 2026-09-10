using System.ComponentModel;
using System.Runtime.CompilerServices;
using TOTP.Installer.Engine;
using TOTP.Installer.Localization;
using WixToolset.BootstrapperApplicationApi;

namespace TOTP.Installer.Presentation;

internal sealed class InstallerViewModel : INotifyPropertyChanged
{
    private readonly IInstallerEngine _engine;
    private readonly Func<IntPtr> _windowHandle;
    private readonly Action<string> _launchFile;
    private readonly Action<Action> _dispatch;
    private readonly Action<int> _shutdown;
    private readonly DelegateCommand _browseCommand;
    private InstallerStage _stage = InstallerStage.Detecting;
    private string _installFolder;
    private bool _licenseAccepted;
    private bool _newerBundleDetected;
    private RegistrationType _registrationType;
    private LaunchAction _plannedAction = LaunchAction.Unknown;
    private int _progress;
    private int _result;

    internal InstallerViewModel(
        IInstallerEngine engine,
        InstallerText text,
        Func<IntPtr> windowHandle,
        Action<string> launchFile,
        Action<Action> dispatch,
        Action<int> shutdown)
    {
        _engine = engine;
        Text = text;
        _windowHandle = windowHandle;
        _launchFile = launchFile;
        _dispatch = dispatch;
        _shutdown = shutdown;
        _installFolder = GetInstallFolder();

        _browseCommand = new DelegateCommand(() => BrowseRequested?.Invoke(this, EventArgs.Empty), () => IsWelcome);
        InstallCommand = new DelegateCommand(() => Plan(LaunchAction.Install), () => IsWelcome && LicenseAccepted && IsValidInstallFolder());
        RepairCommand = new DelegateCommand(() => Plan(LaunchAction.Repair), () => IsMaintenance);
        UninstallCommand = new DelegateCommand(() => Plan(LaunchAction.Uninstall), () => IsMaintenance);
        CancelCommand = new DelegateCommand(Cancel, () => IsApplying);
        CloseCommand = new DelegateCommand(Close, () => !IsApplying);
        LaunchCommand = new DelegateCommand(Launch, () => CanLaunch);
        OpenLicenseCommand = new DelegateCommand(() => SetStage(InstallerStage.License), () => IsWelcome);
        BackCommand = new DelegateCommand(() => SetStage(InstallerStage.Welcome), () => IsLicense);

        engine.DetectionStarted += OnDetectionStarted;
        engine.RelatedBundleDetected += OnRelatedBundleDetected;
        engine.DetectionCompleted += OnDetectionCompleted;
        engine.PlanCompleted += OnPlanCompleted;
        engine.ProgressChanged += OnProgressChanged;
        engine.ApplyCompleted += OnApplyCompleted;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    internal event EventHandler? BrowseRequested;

    public InstallerText Text { get; }
    public DelegateCommand InstallCommand { get; }
    public DelegateCommand RepairCommand { get; }
    public DelegateCommand UninstallCommand { get; }
    public DelegateCommand CancelCommand { get; }
    public DelegateCommand CloseCommand { get; }
    public DelegateCommand LaunchCommand { get; }
    public DelegateCommand OpenLicenseCommand { get; }
    public DelegateCommand BackCommand { get; }
    public DelegateCommand BrowseCommand => _browseCommand;

    public string InstallFolder
    {
        get => _installFolder;
        set
        {
            if (Set(ref _installFolder, value)) InstallCommand.RaiseCanExecuteChanged();
        }
    }

    public bool LicenseAccepted
    {
        get => _licenseAccepted;
        set
        {
            if (Set(ref _licenseAccepted, value)) InstallCommand.RaiseCanExecuteChanged();
        }
    }

    public int Progress
    {
        get => _progress;
        private set => Set(ref _progress, value);
    }

    public bool IsDetecting => _stage == InstallerStage.Detecting;
    public bool IsWelcome => _stage == InstallerStage.Welcome;
    public bool IsLicense => _stage == InstallerStage.License;
    public bool IsMaintenance => _stage == InstallerStage.Maintenance;
    public bool IsApplying => _stage == InstallerStage.Applying;
    public bool IsSuccess => _stage == InstallerStage.Success;
    public bool IsFailure => _stage == InstallerStage.Failure;
    public bool CanLaunch => IsSuccess && _plannedAction is LaunchAction.Install or LaunchAction.Repair;

    internal void Start() => _engine.Detect();

    internal void RequestClose()
    {
        if (IsApplying) Cancel();
        else Close();
    }

    private void OnDetectionStarted(RegistrationType registrationType)
    {
        _registrationType = registrationType;
        _newerBundleDetected = false;
        SetStage(InstallerStage.Detecting);
    }

    private void OnRelatedBundleDetected(string version, RelationType relationType)
    {
        if (relationType == RelationType.Upgrade &&
            _engine.CompareVersions(_engine.BundleVersion, version) < 0)
        {
            _newerBundleDetected = true;
        }
    }

    private void OnDetectionCompleted(int status)
    {
        RunOnUi(() =>
        {
            if (status < 0 || _newerBundleDetected)
            {
                _result = status < 0 ? status : unchecked((int)0x80070666);
                SetStage(InstallerStage.Failure);
                return;
            }

            var installed = _registrationType == RegistrationType.Full;
            InstallFolder = GetInstallFolder();
            SetStage(installed ? InstallerStage.Maintenance : InstallerStage.Welcome);
            if (_engine.Display != Display.Full)
            {
                Plan(_engine.CommandAction);
            }
        });
    }

    private void Plan(LaunchAction action)
    {
        _plannedAction = action;
        if (action == LaunchAction.Install)
        {
            var fullPath = Path.GetFullPath(InstallFolder.Trim());
            _engine.SetVariableString("InstallFolder", EnsureTrailingSeparator(fullPath));
        }

        Progress = 0;
        SetStage(InstallerStage.Applying);
        _engine.Plan(action);
    }

    private void OnPlanCompleted(int status)
    {
        if (status >= 0)
        {
            _engine.Apply(_windowHandle());
            return;
        }

        _result = status;
        RunOnUi(() => SetStage(InstallerStage.Failure));
    }

    private void OnProgressChanged(int percentage)
    {
        RunOnUi(() => Progress = percentage);
    }

    private void OnApplyCompleted(int status)
    {
        _result = status;
        RunOnUi(() =>
        {
            SetStage(status >= 0 ? InstallerStage.Success : InstallerStage.Failure);
            if (_engine.Display != Display.Full) _shutdown(_result);
        });
    }

    private void Cancel()
    {
        _engine.RequestCancel();
        CancelCommand.RaiseCanExecuteChanged();
    }

    private void Close() => _shutdown(_result == 0 ? 0 : _result);

    private void Launch()
    {
        var executable = Path.Combine(GetInstallFolder(), "TOTP.UI.Avalonia.Desktop.exe");
        try
        {
            _launchFile(executable);
            _shutdown(0);
        }
        catch
        {
            _result = unchecked((int)0x80004005);
            SetStage(InstallerStage.Failure);
        }
    }

    private string GetInstallFolder() =>
        _engine.ContainsVariable("InstallFolder")
            ? _engine.FormatString(_engine.GetVariableString("InstallFolder"))
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "OTP Harbor");

    private void SetStage(InstallerStage stage)
    {
        RunOnUi(() =>
        {
            _stage = stage;
            OnPropertyChanged(nameof(IsDetecting));
            OnPropertyChanged(nameof(IsWelcome));
            OnPropertyChanged(nameof(IsLicense));
            OnPropertyChanged(nameof(IsMaintenance));
            OnPropertyChanged(nameof(IsApplying));
            OnPropertyChanged(nameof(IsSuccess));
            OnPropertyChanged(nameof(IsFailure));
            OnPropertyChanged(nameof(CanLaunch));
            _browseCommand.RaiseCanExecuteChanged();
            InstallCommand.RaiseCanExecuteChanged();
            RepairCommand.RaiseCanExecuteChanged();
            UninstallCommand.RaiseCanExecuteChanged();
            CancelCommand.RaiseCanExecuteChanged();
            CloseCommand.RaiseCanExecuteChanged();
            LaunchCommand.RaiseCanExecuteChanged();
            OpenLicenseCommand.RaiseCanExecuteChanged();
            BackCommand.RaiseCanExecuteChanged();
        });
    }

    private static string EnsureTrailingSeparator(string path) =>
        path.EndsWith(Path.DirectorySeparatorChar) ? path : path + Path.DirectorySeparatorChar;

    private bool IsValidInstallFolder()
    {
        if (string.IsNullOrWhiteSpace(InstallFolder) || !Path.IsPathFullyQualified(InstallFolder)) return false;
        try
        {
            return !string.IsNullOrWhiteSpace(Path.GetFullPath(InstallFolder));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private void RunOnUi(Action action) => _dispatch(action);

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
