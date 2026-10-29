#if DEBUG
using Android.Content;
using Android.Util;
using Avalonia.Threading;

namespace TOTP.Avalonia.Android;

[BroadcastReceiver(
    Name = "io.github.legends.otpharbor.debug.DebugAccountDataReceiver",
    Enabled = true,
    Exported = true)]
[IntentFilter([ShowOnboardingAction, SetThemeAction, AddAction, DeleteAction, ImportAction, DeleteAllAction])]
public sealed class DebugAccountDataReceiver : BroadcastReceiver
{
    internal const string ShowOnboardingAction =
        "io.github.legends.otpharbor.debug.action.SHOW_ONBOARDING";
    internal const string SetThemeAction =
        "io.github.legends.otpharbor.debug.action.SET_THEME";
    internal const string AddAction =
        "io.github.legends.otpharbor.debug.action.BULK_ADD_SYNTHETIC_ACCOUNTS";
    internal const string DeleteAction =
        "io.github.legends.otpharbor.debug.action.BULK_DELETE_SYNTHETIC_ACCOUNTS";
    internal const string ImportAction =
        "io.github.legends.otpharbor.debug.action.IMPORT_ACCOUNTS";
    internal const string DeleteAllAction =
        "io.github.legends.otpharbor.debug.action.DELETE_ALL_ACCOUNTS";

    public override void OnReceive(Context? context, Intent? intent)
    {
        var pending = GoAsync();
        var application = context?.ApplicationContext as OtpHarborApplication;
        var action = intent?.Action;
        var count = Math.Clamp(intent?.GetIntExtra("count", 600) ?? 600, 1, 5000);
        var importPath = intent?.GetStringExtra("path");
        var theme = intent?.GetStringExtra("theme");
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                var succeeded = action switch
                {
                    ShowOnboardingAction when application is not null =>
                        await application.ShowOnboardingPreviewAsync(),
                    SetThemeAction when application is not null =>
                        await application.SetDebugThemeAsync(theme),
                    AddAction when application is not null =>
                        await application.AddDebugSyntheticAccountsAsync(count),
                    DeleteAction when application is not null =>
                        await application.DeleteDebugSyntheticAccountsAsync(),
                    ImportAction when application is not null && context is not null =>
                        await application.ImportDebugAccountsAsync(context, importPath),
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
