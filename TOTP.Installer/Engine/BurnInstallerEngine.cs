using WixToolset.BootstrapperApplicationApi;

namespace TOTP.Installer.Engine;

internal sealed class BurnInstallerEngine : IInstallerEngine
{
    private readonly InstallerBootstrapper _bootstrapper;
    private bool _cancelRequested;

    internal BurnInstallerEngine(InstallerBootstrapper bootstrapper)
    {
        _bootstrapper = bootstrapper;
        bootstrapper.DetectBegin += (_, args) => DetectionStarted?.Invoke(args.RegistrationType);
        bootstrapper.DetectRelatedBundle += (_, args) => RelatedBundleDetected?.Invoke(args.Version, args.RelationType);
        bootstrapper.DetectComplete += (_, args) => DetectionCompleted?.Invoke(args.Status);
        bootstrapper.PlanComplete += (_, args) => PlanCompleted?.Invoke(args.Status);
        bootstrapper.Progress += (_, args) =>
        {
            args.Cancel = _cancelRequested;
            ProgressChanged?.Invoke(args.OverallPercentage);
        };
        bootstrapper.ExecuteProgress += (_, args) =>
        {
            args.Cancel = _cancelRequested;
            ProgressChanged?.Invoke(args.OverallPercentage);
        };
        bootstrapper.ApplyComplete += (_, args) => ApplyCompleted?.Invoke(args.Status);
    }

    public event Action<RegistrationType>? DetectionStarted;
    public event Action<string, RelationType>? RelatedBundleDetected;
    public event Action<int>? DetectionCompleted;
    public event Action<int>? PlanCompleted;
    public event Action<int>? ProgressChanged;
    public event Action<int>? ApplyCompleted;

    public Display Display => _bootstrapper.CommandInstance.Display;
    public LaunchAction CommandAction => _bootstrapper.CommandInstance.Action;
    public string BundleVersion => _bootstrapper.EngineInstance.GetVariableVersion("WixBundleVersion");
    public bool ContainsVariable(string name) => _bootstrapper.EngineInstance.ContainsVariable(name);
    public string GetVariableString(string name) => _bootstrapper.EngineInstance.GetVariableString(name);
    public string FormatString(string value) => _bootstrapper.EngineInstance.FormatString(value);
    public int CompareVersions(string first, string second) => _bootstrapper.EngineInstance.CompareVersions(first, second);
    public void SetVariableString(string name, string value) => _bootstrapper.EngineInstance.SetVariableString(name, value, false);
    public void Detect() => _bootstrapper.EngineInstance.Detect();
    public void Plan(LaunchAction action) => _bootstrapper.EngineInstance.Plan(action);
    public void Apply(IntPtr parentWindow) => _bootstrapper.EngineInstance.Apply(parentWindow);
    public void RequestCancel() => _cancelRequested = true;
}
