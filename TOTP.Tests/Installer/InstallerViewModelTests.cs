using TOTP.Installer.Engine;
using TOTP.Installer.Presentation;
using WixToolset.BootstrapperApplicationApi;

namespace TOTP.Tests.Installer;

public sealed class InstallerViewModelTests
{
    [Fact]
    public void InstallFlow_UsesCustomFolder_ReportsProgress_AndLaunchesAsASeparateStep()
    {
        var engine = new FakeInstallerEngine();
        var launched = new List<string>();
        var shutdownResults = new List<int>();
        var sut = Create(engine, launched.Add, shutdownResults.Add);

        sut.Start();
        engine.CompleteDetection(RegistrationType.None, 0);

        Assert.True(engine.DetectCalled);
        Assert.True(sut.IsWelcome);
        Assert.Equal(@"C:\Program Files\OTP Harbor\", sut.InstallFolder);
        Assert.False(sut.InstallCommand.CanExecute(null));

        sut.InstallFolder = @"D:\Apps\OTP Harbor";
        sut.LicenseAccepted = true;
        sut.InstallCommand.Execute(null);

        Assert.Equal(LaunchAction.Install, engine.PlannedAction);
        Assert.Equal(@"D:\Apps\OTP Harbor\", engine.Variables["InstallFolder"]);
        Assert.True(sut.IsApplying);

        engine.CompletePlan(0);
        engine.ReportProgress(62);
        engine.CompleteApply(0);

        Assert.Equal(new IntPtr(42), engine.AppliedParentWindow);
        Assert.Equal(62, sut.Progress);
        Assert.True(sut.IsSuccess);

        sut.LaunchCommand.Execute(null);

        Assert.Equal(@"D:\Apps\OTP Harbor\TOTP.UI.Avalonia.Desktop.exe", Assert.Single(launched));
        Assert.Equal(0, Assert.Single(shutdownResults));
    }

    [Fact]
    public void InstalledBundle_OffersMaintenance_AndRepairUsesBurnPlan()
    {
        var engine = new FakeInstallerEngine();
        var sut = Create(engine);

        sut.Start();
        engine.CompleteDetection(RegistrationType.Full, 0);
        sut.RepairCommand.Execute(null);

        Assert.True(sut.IsApplying);
        Assert.Equal(LaunchAction.Repair, engine.PlannedAction);
    }

    [Fact]
    public void CloseDuringApply_RequestsTransactionalCancellationWithoutClosingEarly()
    {
        var engine = new FakeInstallerEngine();
        var shutdownResults = new List<int>();
        var sut = Create(engine, shutdown: shutdownResults.Add);
        engine.CompleteDetection(RegistrationType.None, 0);
        sut.LicenseAccepted = true;
        sut.InstallCommand.Execute(null);

        sut.RequestClose();

        Assert.True(engine.CancelRequested);
        Assert.Empty(shutdownResults);
    }

    [Fact]
    public void DetectionFailure_ShowsRecoverableFailureState()
    {
        var engine = new FakeInstallerEngine();
        var sut = Create(engine);

        engine.CompleteDetection(RegistrationType.None, unchecked((int)0x80004005));

        Assert.True(sut.IsFailure);
        Assert.True(sut.CloseCommand.CanExecute(null));
    }

    [Fact]
    public void LicenseNotice_StaysInsideInstaller_AndReturnsToWelcome()
    {
        var engine = new FakeInstallerEngine();
        var launched = new List<string>();
        var sut = Create(engine, launched.Add);
        engine.CompleteDetection(RegistrationType.None, 0);

        sut.OpenLicenseCommand.Execute(null);

        Assert.True(sut.IsLicense);
        Assert.Empty(launched);

        sut.BackCommand.Execute(null);

        Assert.True(sut.IsWelcome);
    }

    [Fact]
    public void InvalidInstallFolder_CannotStartInstallation()
    {
        var engine = new FakeInstallerEngine();
        var sut = Create(engine);
        engine.CompleteDetection(RegistrationType.None, 0);
        sut.LicenseAccepted = true;
        sut.InstallFolder = "relative-folder";

        Assert.False(sut.InstallCommand.CanExecute(null));
    }

    [Fact]
    public void SuccessfulUninstall_DoesNotOfferToLaunchRemovedApplication()
    {
        var engine = new FakeInstallerEngine();
        var sut = Create(engine);
        engine.CompleteDetection(RegistrationType.Full, 0);

        sut.UninstallCommand.Execute(null);
        engine.CompletePlan(0);
        engine.CompleteApply(0);

        Assert.True(sut.IsSuccess);
        Assert.False(sut.CanLaunch);
        Assert.False(sut.LaunchCommand.CanExecute(null));
    }

    private static InstallerViewModel Create(
        FakeInstallerEngine engine,
        Action<string>? launch = null,
        Action<int>? shutdown = null) =>
        new(
            engine,
            null!,
            () => new IntPtr(42),
            launch ?? (_ => { }),
            action => action(),
            shutdown ?? (_ => { }));

    private sealed class FakeInstallerEngine : IInstallerEngine
    {
        public event Action<RegistrationType>? DetectionStarted;
        public event Action<string, RelationType>? RelatedBundleDetected;
        public event Action<int>? DetectionCompleted;
        public event Action<int>? PlanCompleted;
        public event Action<int>? ProgressChanged;
        public event Action<int>? ApplyCompleted;

        public Display Display { get; set; } = Display.Full;
        public LaunchAction CommandAction { get; set; } = LaunchAction.Install;
        public string BundleVersion { get; set; } = "2.0.0-rc20";
        public Dictionary<string, string> Variables { get; } = new(StringComparer.Ordinal)
        {
            ["InstallFolder"] = @"[ProgramFiles64Folder]OTP Harbor\"
        };
        public bool DetectCalled { get; private set; }
        public bool CancelRequested { get; private set; }
        public LaunchAction? PlannedAction { get; private set; }
        public IntPtr? AppliedParentWindow { get; private set; }

        public bool ContainsVariable(string name) => Variables.ContainsKey(name);
        public string GetVariableString(string name) => Variables[name];
        public string FormatString(string value) => value.Replace("[ProgramFiles64Folder]", @"C:\Program Files\", StringComparison.Ordinal);
        public int CompareVersions(string first, string second) => string.Compare(first, second, StringComparison.Ordinal);
        public void SetVariableString(string name, string value) => Variables[name] = value;
        public void Detect() => DetectCalled = true;
        public void Plan(LaunchAction action) => PlannedAction = action;
        public void Apply(IntPtr parentWindow) => AppliedParentWindow = parentWindow;
        public void RequestCancel() => CancelRequested = true;

        internal void CompleteDetection(RegistrationType type, int status)
        {
            DetectionStarted?.Invoke(type);
            DetectionCompleted?.Invoke(status);
        }

        internal void CompletePlan(int status) => PlanCompleted?.Invoke(status);
        internal void ReportProgress(int percentage) => ProgressChanged?.Invoke(percentage);
        internal void CompleteApply(int status) => ApplyCompleted?.Invoke(status);
        internal void ReportRelatedBundle(string version, RelationType relationType) => RelatedBundleDetected?.Invoke(version, relationType);
    }
}
