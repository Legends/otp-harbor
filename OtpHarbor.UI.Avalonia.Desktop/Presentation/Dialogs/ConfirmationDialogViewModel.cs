using System.Windows.Input;
using TOTP.Core.Services.Interfaces;

namespace TOTP.Avalonia.Desktop.Presentation.Dialogs;

public sealed class ConfirmationDialogViewModel
{
    public ConfirmationDialogViewModel(ConfirmationDialogRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Title))
            throw new ArgumentException("A dialog title is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.Message))
            throw new ArgumentException("A dialog message is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.ConfirmText))
            throw new ArgumentException("Confirm button text is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.CancelText))
            throw new ArgumentException("Cancel button text is required.", nameof(request));

        Title = request.Title;
        Message = request.Message;
        Severity = request.Severity;
        ConfirmText = request.ConfirmText;
        CancelText = request.CancelText;
        ShowCancel = request.ShowCancel;
        IsDestructive = request.IsDestructive;
        ConfirmCommand = new AsyncCommand(ConfirmAsync, static () => true);
        CancelCommand = new AsyncCommand(CancelAsync, static () => true);
    }

    public event Action<bool>? CloseRequested;

    public string Title { get; }
    public string Message { get; }
    public NotificationSeverity Severity { get; }
    public string ConfirmText { get; }
    public string CancelText { get; }
    public bool ShowCancel { get; }
    public bool IsDestructive { get; }
    public ICommand ConfirmCommand { get; }
    public ICommand CancelCommand { get; }

    private Task ConfirmAsync()
    {
        CloseRequested?.Invoke(true);
        return Task.CompletedTask;
    }

    private Task CancelAsync()
    {
        CloseRequested?.Invoke(false);
        return Task.CompletedTask;
    }
}
