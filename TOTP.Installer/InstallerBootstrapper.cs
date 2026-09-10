using Avalonia;
using WixToolset.BootstrapperApplicationApi;

namespace TOTP.Installer;

internal sealed class InstallerBootstrapper : BootstrapperApplication
{
    private int _result;

    internal IEngine EngineInstance => engine;
    internal IBootstrapperCommand CommandInstance { get; private set; } = null!;

    protected override void OnCreate(CreateEventArgs args)
    {
        base.OnCreate(args);
        CommandInstance = args.Command;
    }

    protected override void Run()
    {
        InstallerRuntime.Initialize(this);
        try
        {
            AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .StartWithClassicDesktopLifetime([]);
        }
        catch (Exception exception)
        {
            EngineInstance.Log(LogLevel.Error, $"Custom setup UI failed: {exception.GetType().FullName}");
            _result = unchecked((int)0x80004005);
        }
        finally
        {
            EngineInstance.Quit(_result);
        }
    }

    internal void SetResult(int result) => _result = result;
}
