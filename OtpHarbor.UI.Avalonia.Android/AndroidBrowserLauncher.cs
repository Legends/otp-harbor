using Android.Content;

namespace TOTP.Avalonia.Android;

internal sealed class AndroidBrowserLauncher(AndroidActivityProvider activityProvider)
{
    public Task<bool> OpenAsync(Uri uri)
    {
        var activity = activityProvider.GetCurrent();
        if (activity is null || !uri.IsAbsoluteUri || uri.Scheme != Uri.UriSchemeHttps)
            return Task.FromResult(false);

        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        activity.RunOnUiThread(() =>
        {
            try
            {
                using var intent = new Intent(Intent.ActionView, global::Android.Net.Uri.Parse(uri.AbsoluteUri));
                intent.AddCategory(Intent.CategoryBrowsable);
                // Direct launching does not require Android package-visibility queries.
                activity.StartActivity(intent);
                completion.TrySetResult(true);
            }
            catch (ActivityNotFoundException)
            {
                completion.TrySetResult(false);
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        });
        return completion.Task;
    }
}
