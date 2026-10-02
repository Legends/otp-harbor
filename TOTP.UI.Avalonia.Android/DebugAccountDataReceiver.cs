#if DEBUG
using Android.Content;
using Android.Util;
using Avalonia.Threading;

namespace TOTP.Avalonia.Android;

[BroadcastReceiver(
    Name = "io.github.legends.otpharbor.debug.DebugAccountDataReceiver",
    Enabled = true,
    Exported = true)]
[IntentFilter([AddAction, DeleteAction, DeleteAllAction])]
public sealed class DebugAccountDataReceiver : BroadcastReceiver
{
    internal const string AddAction =
        "io.github.legends.otpharbor.debug.action.BULK_ADD_SYNTHETIC_ACCOUNTS";
    internal const string DeleteAction =
        "io.github.legends.otpharbor.debug.action.BULK_DELETE_SYNTHETIC_ACCOUNTS";
    internal const string DeleteAllAction =
        "io.github.legends.otpharbor.debug.action.DELETE_ALL_ACCOUNTS";

    public override void OnReceive(Context? context, Intent? intent)
    {
        var pending = GoAsync();
        var application = context?.ApplicationContext as OtpHarborApplication;
        var action = intent?.Action;
        var count = Math.Clamp(intent?.GetIntExtra("count", 600) ?? 600, 1, 5000);
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                var succeeded = action switch
                {
                    AddAction when application is not null =>
                        await application.AddDebugSyntheticAccountsAsync(count),
                    DeleteAction when application is not null =>
                        await application.DeleteDebugSyntheticAccountsAsync(),
                    DeleteAllAction when application is not null =>
                        await application.DeleteAllAccountsAsync(),
                    _ => false
                };
                Log.Info("OtpHarborDebug", $"Debug account data action completed: {succeeded}.");
            }
            catch (Exception exception)
            {
                Log.Error(
                    "OtpHarborDebug",
                    $"Debug account data action failed ({exception.GetType().Name}).");
            }
            finally
            {
                pending?.Finish();
            }
        });
    }
}
#endif
