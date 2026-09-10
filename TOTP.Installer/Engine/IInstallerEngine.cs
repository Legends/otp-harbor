using WixToolset.BootstrapperApplicationApi;

namespace TOTP.Installer.Engine;

internal interface IInstallerEngine
{
    event Action<RegistrationType>? DetectionStarted;
    event Action<string, RelationType>? RelatedBundleDetected;
    event Action<int>? DetectionCompleted;
    event Action<int>? PlanCompleted;
    event Action<int>? ProgressChanged;
    event Action<int>? ApplyCompleted;

    Display Display { get; }
    LaunchAction CommandAction { get; }
    string BundleVersion { get; }
    bool ContainsVariable(string name);
    string GetVariableString(string name);
    string FormatString(string value);
    int CompareVersions(string first, string second);
    void SetVariableString(string name, string value);
    void Detect();
    void Plan(LaunchAction action);
    void Apply(IntPtr parentWindow);
    void RequestCancel();
}
