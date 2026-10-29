using WixToolset.BootstrapperApplicationApi;

namespace TOTP.Installer;

internal static class Program
{
    private static int Main()
    {
        ManagedBootstrapperApplication.Run(new InstallerBootstrapper());
        return 0;
    }
}
