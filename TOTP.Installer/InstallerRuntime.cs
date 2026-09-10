namespace TOTP.Installer;

internal static class InstallerRuntime
{
    private static InstallerBootstrapper? _bootstrapper;

    internal static InstallerBootstrapper Bootstrapper =>
        _bootstrapper ?? throw new InvalidOperationException("The installer engine is not initialized.");

    internal static void Initialize(InstallerBootstrapper bootstrapper) =>
        _bootstrapper = bootstrapper ?? throw new ArgumentNullException(nameof(bootstrapper));
}
