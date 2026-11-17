using Android.Content;

namespace TOTP.Avalonia.Android;

internal sealed class AndroidAppShareService(AndroidActivityProvider activityProvider)
{
    public Task<bool> ShareAsync(string title, string text)
    {
        var activity = activityProvider.GetCurrent();
        if (activity is null) return Task.FromResult(false);
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        activity.RunOnUiThread(() =>
        {
            try
            {
                using var send = new Intent(Intent.ActionSend);
                send.SetType("text/plain");
                send.PutExtra(Intent.ExtraSubject, title);
                send.PutExtra(Intent.ExtraText, text);
                using var chooser = Intent.CreateChooser(send, title);
                activity.StartActivity(chooser!);
                completion.TrySetResult(true);
            }
            catch (ActivityNotFoundException) { completion.TrySetResult(false); }
            catch (Exception exception) { completion.TrySetException(exception); }
        });
        return completion.Task;
    }
}
