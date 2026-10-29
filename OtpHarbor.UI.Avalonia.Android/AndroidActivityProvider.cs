namespace TOTP.Avalonia.Android;

internal sealed class AndroidActivityProvider
{
    private readonly object _sync = new();
    private WeakReference<MainActivity>? _current;

    public void Attach(MainActivity activity)
    {
        ArgumentNullException.ThrowIfNull(activity);
        lock (_sync) _current = new WeakReference<MainActivity>(activity);
    }

    public void Detach(MainActivity activity)
    {
        ArgumentNullException.ThrowIfNull(activity);
        lock (_sync)
        {
            if (_current?.TryGetTarget(out var current) == true
                && ReferenceEquals(current, activity))
            {
                _current = null;
            }
        }
    }

    public MainActivity? GetCurrent()
    {
        lock (_sync)
        {
            if (_current is null
                || !_current.TryGetTarget(out var activity)
                || activity.IsFinishing
                || activity.IsDestroyed)
            {
                return null;
            }

            return activity;
        }
    }
}
