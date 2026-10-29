using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace TOTP.Avalonia.Desktop.Startup;

public sealed class AvaloniaBackgroundServiceCoordinator(
    IReadOnlyList<IHostedService> services,
    ILogger<AvaloniaBackgroundServiceCoordinator> logger)
{
    public void Start()
    {
        RunOutsideUiContext(async () =>
        {
            foreach (var service in services)
                await service.StartAsync(CancellationToken.None).ConfigureAwait(false);
        });
    }

    public void Stop()
    {
        RunOutsideUiContext(async () =>
        {
            for (var index = services.Count - 1; index >= 0; index--)
            {
                try
                {
                    await services[index].StopAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    logger.LogWarning(
                        "A desktop background service could not be stopped cleanly. Service type: {ServiceType}; exception type: {ExceptionType}.",
                        services[index].GetType().FullName,
                        exception.GetType().FullName);
                }
            }
        });
    }

    private static void RunOutsideUiContext(Func<Task> operation) =>
        Task.Run(operation).GetAwaiter().GetResult();
}
