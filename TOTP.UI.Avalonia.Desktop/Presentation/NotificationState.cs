using System.ComponentModel;
using System.Runtime.CompilerServices;
using TOTP.Core.Services.Interfaces;

namespace TOTP.Avalonia.Desktop.Presentation;

public sealed class NotificationState : INotifyPropertyChanged, IDisposable
{
    private readonly TimeSpan _defaultTransientDuration;
    private CancellationTokenSource? _lifetime;
    private string _text = string.Empty;
    private NotificationSeverity _severity = NotificationSeverity.Information;
    private bool _disposed;

    public NotificationState(TimeSpan? defaultTransientDuration = null)
    {
        _defaultTransientDuration = defaultTransientDuration
            ?? TransientNotificationDefaults.Duration;
        if (_defaultTransientDuration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(defaultTransientDuration));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler<NotificationShownEventArgs>? Shown;

    public string Text
    {
        get => _text;
        private set
        {
            if (!SetField(ref _text, value)) return;
            OnPropertyChanged(nameof(HasMessage));
        }
    }

    public NotificationSeverity Severity
    {
        get => _severity;
        private set => SetField(ref _severity, value);
    }

    public bool HasMessage => !string.IsNullOrWhiteSpace(Text);

    public void ShowPersistent(string text, NotificationSeverity severity)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        CancelLifetime();
        Severity = severity;
        Text = text ?? string.Empty;
        if (HasMessage)
            Shown?.Invoke(this, new NotificationShownEventArgs(Text, Severity));
    }

    public void ShowForSeverity(string text, NotificationSeverity severity)
        => ShowTransient(text, severity);

    public void ShowTransient(
        string text,
        NotificationSeverity severity,
        TimeSpan? duration = null)
    {
        var lifetimeDuration = duration ?? _defaultTransientDuration;
        if (lifetimeDuration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(duration));

        ShowPersistent(text, severity);
        if (!HasMessage) return;

        var lifetime = new CancellationTokenSource();
        _lifetime = lifetime;
        _ = ClearAfterDelayAsync(text, lifetimeDuration, lifetime);
    }

    public void Clear()
    {
        if (_disposed) return;
        CancelLifetime();
        Text = string.Empty;
        Severity = NotificationSeverity.Information;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CancelLifetime();
    }

    private async Task ClearAfterDelayAsync(
        string expectedText,
        TimeSpan duration,
        CancellationTokenSource lifetime)
    {
        try
        {
            await Task.Delay(duration, lifetime.Token);
            if (ReferenceEquals(_lifetime, lifetime)
                && string.Equals(Text, expectedText, StringComparison.Ordinal))
            {
                Text = string.Empty;
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
        finally
        {
            if (ReferenceEquals(_lifetime, lifetime))
                _lifetime = null;
            lifetime.Dispose();
        }
    }

    private void CancelLifetime()
    {
        var lifetime = _lifetime;
        _lifetime = null;
        lifetime?.Cancel();
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed record NotificationShownEventArgs(
    string Text,
    NotificationSeverity Severity);
