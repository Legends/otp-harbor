using Avalonia.Threading;
using TOTP.Avalonia.Desktop.Dialogs;
using TOTP.Avalonia.Desktop.Presentation.Dialogs;

namespace TOTP.Avalonia.Desktop.Platform;

public sealed class AvaloniaDialogService(
    AvaloniaWindowCoordinator windows,
    AvaloniaActivityMonitor activityMonitor) : IAvaloniaDialogService
{
    private readonly SemaphoreSlim _dialogGate = new(1, 1);

    public async Task<bool> ConfirmImportFileAsync(TOTP.Avalonia.Shared.Import.AuthenticatorImportViewModel model,
        TOTP.Core.Services.Models.AccountImportReadSummary summary, string fileName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model);
        await _dialogGate.WaitAsync(cancellationToken);
        try
        {
            var owner = windows.GetRequiredDialogOwner();
            var decision = model.WaitForFileReviewAsync(summary, fileName, cancellationToken);
            var dialog = new ImportFileSummaryWindow { DataContext = model };
            dialog.ConfigureForOwner(owner);
            model.PropertyChanged += StateChanged;
            dialog.Opened += Opened;
            using var ownership = windows.RegisterOwnedDialog(dialog);
            using var activity = activityMonitor.Attach(dialog);
            using var cancellation = cancellationToken.Register(() => Dispatcher.UIThread.Post(() => dialog.Close()));
            try
            {
                if (!decision.IsCompleted) await dialog.ShowDialog(owner);
                model.CancelPendingFile();
                return await decision;
            }
            finally
            {
                model.CancelPendingFile();
                model.PropertyChanged -= StateChanged;
                dialog.Opened -= Opened;
                dialog.DataContext = null;
                try { await decision; }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
            }
            void StateChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
            {
                if (!model.HasSelectedFile && dialog.IsVisible) dialog.Close();
            }
            void Opened(object? sender, EventArgs args)
            {
                if (decision.IsCompleted) dialog.Close();
            }
        }
        finally { _dialogGate.Release(); }
    }

    public async Task<TOTP.Core.Services.Models.AccountImportResolution?> ReviewImportAsync(
        TOTP.Avalonia.Shared.Import.ImportReviewViewModel review,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(review);
        await _dialogGate.WaitAsync(cancellationToken);
        try
        {
            var owner = windows.GetRequiredDialogOwner();
            var dialog = new ImportReviewWindow { DataContext = review };
            dialog.FitToScreen(owner);
            TOTP.Core.Services.Models.AccountImportResolution? resolution = null;
            review.CloseRequested += Close;
            using var ownership = windows.RegisterOwnedDialog(dialog);
            using var activity = activityMonitor.Attach(dialog);
            using var cancellation = cancellationToken.Register(
                () => Dispatcher.UIThread.Post(() => dialog.Close()));
            try
            {
                await dialog.ShowDialog(owner);
                cancellationToken.ThrowIfCancellationRequested();
                return resolution;
            }
            finally
            {
                review.CloseRequested -= Close;
                dialog.DataContext = null;
            }
            void Close(TOTP.Core.Services.Models.AccountImportResolution? result)
            {
                resolution = result;
                dialog.Close();
            }
        }
        finally { _dialogGate.Release(); }
    }

    public async Task<bool> ConfirmAsync(
        ConfirmationDialogRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await _dialogGate.WaitAsync(cancellationToken);
        try
        {
            var owner = windows.GetRequiredDialogOwner();
            var viewModel = new ConfirmationDialogViewModel(request);
            var dialog = new ConfirmationDialogWindow { DataContext = viewModel };
            dialog.ConfigureForOwner(owner, request.IsDestructive);
            var requestedResult = false;
            viewModel.CloseRequested += Close;
            using var ownership = windows.RegisterOwnedDialog(dialog);
            using var activity = activityMonitor.Attach(dialog);
            using var cancellation = cancellationToken.Register(
                () => Dispatcher.UIThread.Post(() => dialog.Close(false)));

            try
            {
                var result = await dialog.ShowDialog<bool>(owner);
                return result && requestedResult;
            }
            finally
            {
                viewModel.CloseRequested -= Close;
                dialog.DataContext = null;
            }

            void Close(bool result)
            {
                requestedResult = result;
                dialog.Close(result);
            }
        }
        finally
        {
            _dialogGate.Release();
        }
    }

    public async Task<string?> PromptForPasswordAsync(
        PasswordDialogRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await _dialogGate.WaitAsync(cancellationToken);
        try
        {
            var owner = windows.GetRequiredDialogOwner();
            using var viewModel = new PasswordDialogViewModel(request, cancellationToken);
            var dialog = new PasswordDialogWindow { DataContext = viewModel };
            DialogWindowSizing.FitToOwner(dialog, owner);
            string? confirmedPassword = null;
            viewModel.CloseRequested += Close;
            using var ownership = windows.RegisterOwnedDialog(dialog);
            using var activity = activityMonitor.Attach(dialog);
            using var cancellation = cancellationToken.Register(
                () => Dispatcher.UIThread.Post(() => dialog.Close(false)));

            try
            {
                var result = await dialog.ShowDialog<bool>(owner);
                return result ? confirmedPassword : null;
            }
            finally
            {
                viewModel.CloseRequested -= Close;
                viewModel.ClearSensitiveData();
                dialog.DataContext = null;
            }

            void Close(string? password)
            {
                confirmedPassword = password;
                dialog.Close(password is not null);
            }
        }
        finally
        {
            _dialogGate.Release();
        }
    }

    public async Task<ChoiceDialogResult> ChooseAsync(
        ChoiceDialogRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await _dialogGate.WaitAsync(cancellationToken);
        try
        {
            var owner = windows.GetRequiredDialogOwner();
            var viewModel = new ChoiceDialogViewModel(request);
            var dialog = new ChoiceDialogWindow { DataContext = viewModel };
            DialogWindowSizing.FitToOwner(dialog, owner);
            var requestedResult = ChoiceDialogResult.Cancel;
            viewModel.CloseRequested += Close;
            using var ownership = windows.RegisterOwnedDialog(dialog);
            using var activity = activityMonitor.Attach(dialog);
            using var cancellation = cancellationToken.Register(
                () => Dispatcher.UIThread.Post(() => dialog.Close(ChoiceDialogResult.Cancel)));
            try
            {
                await dialog.ShowDialog<ChoiceDialogResult>(owner);
                return requestedResult;
            }
            finally
            {
                viewModel.CloseRequested -= Close;
                dialog.DataContext = null;
            }

            void Close(ChoiceDialogResult result)
            {
                requestedResult = result;
                dialog.Close(result);
            }
        }
        finally
        {
            _dialogGate.Release();
        }
    }

    public async Task ShowMessageAsync(
        MessageDialogRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await ConfirmAsync(new ConfirmationDialogRequest(
            request.Title,
            request.Message,
            request.Severity,
            request.CloseText,
            request.CloseText,
            ShowCancel: false), cancellationToken);
    }
}
