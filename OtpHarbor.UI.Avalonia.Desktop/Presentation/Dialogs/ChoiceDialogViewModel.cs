using System.Windows.Input;

namespace TOTP.Avalonia.Desktop.Presentation.Dialogs;

public sealed class ChoiceDialogViewModel
{
    public ChoiceDialogViewModel(ChoiceDialogRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (new[] { request.Title, request.Message, request.PrimaryText, request.SecondaryText, request.CancelText }
            .Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("Choice dialog text must be explicit and non-empty.", nameof(request));
        }

        Title = request.Title;
        Message = request.Message;
        Severity = request.Severity;
        PrimaryText = request.PrimaryText;
        SecondaryText = request.SecondaryText;
        CancelText = request.CancelText;
        PrimaryCommand = Command(ChoiceDialogResult.Primary);
        SecondaryCommand = Command(ChoiceDialogResult.Secondary);
        CancelCommand = Command(ChoiceDialogResult.Cancel);
    }

    public event Action<ChoiceDialogResult>? CloseRequested;
    public string Title { get; }
    public string Message { get; }
    public TOTP.Core.Services.Interfaces.NotificationSeverity Severity { get; }
    public string PrimaryText { get; }
    public string SecondaryText { get; }
    public string CancelText { get; }
    public ICommand PrimaryCommand { get; }
    public ICommand SecondaryCommand { get; }
    public ICommand CancelCommand { get; }

    private AsyncCommand Command(ChoiceDialogResult result) =>
        new(() =>
        {
            CloseRequested?.Invoke(result);
            return Task.CompletedTask;
        }, static () => true);
}
