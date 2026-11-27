using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using TOTP.Avalonia.Shared.Controls;

namespace TOTP.Avalonia.Mobile.Views;

public partial class MainView
{
    private CancellationTokenSource? _secondaryPreloadLifetime;
    private Control? _nativeImportReview;
    private Control? _nativeGroupAccountPicker;

    private VerticalReveal MasterPasswordFieldsReveal => DeferredControl<VerticalReveal>(DeferredMasterPasswordChangePanel, "MasterPasswordFieldsReveal");
    private Border DisableAppLockConfirmationPanel => DeferredControl<Border>(DeferredAppLockPanel, "DisableAppLockConfirmationPanel");
    private RevealableSecretInput DisableAppLockPasswordBox => DeferredControl<RevealableSecretInput>(DeferredAppLockPanel, "DisableAppLockPasswordBox");
    private StackPanel UnlockMethodConfirmationPanel => DeferredControl<StackPanel>(DeferredUnlockMethodPanel, "UnlockMethodConfirmationPanel");
    private RevealableSecretInput UnlockMethodPasswordBox => DeferredControl<RevealableSecretInput>(DeferredUnlockMethodPanel, "UnlockMethodPasswordBox");
    private Expander FaqMasterPasswordRecoveryExpander => DeferredControl<Expander>(DeferredFaqMasterPasswordRecoveryExpander, "FaqMasterPasswordRecoveryExpander");
    private Expander FaqOlderVaultsExpander => DeferredControl<Expander>(DeferredFaqOlderVaultsExpander, "FaqOlderVaultsExpander");
    private Expander FaqCreateBackupExpander => DeferredControl<Expander>(DeferredFaqCreateBackupExpander, "FaqCreateBackupExpander");
    private Expander FaqIconPacksExpander => DeferredControl<Expander>(DeferredFaqIconPacksExpander, "FaqIconPacksExpander");
    private Expander FaqImportFormatsExpander => DeferredControl<Expander>(DeferredFaqImportFormatsExpander, "FaqImportFormatsExpander");
    private Expander FaqImportFormatsAegisExpander => DeferredControl<Expander>(DeferredFaqImportFormatsExpander, "FaqImportFormatsAegisExpander");
    private Expander FaqImportFormatsTwoFasExpander => DeferredControl<Expander>(DeferredFaqImportFormatsExpander, "FaqImportFormatsTwoFasExpander");
    private Expander FaqImportFormatsOtpAuthExpander => DeferredControl<Expander>(DeferredFaqImportFormatsExpander, "FaqImportFormatsOtpAuthExpander");
    private TextBox GroupAccountSearchBox => DeferredControl<TextBox>(DeferredGroupEditor, "GroupAccountSearchBox");
    private ListBox GroupAccountPicker => DeferredControl<ListBox>(DeferredGroupEditor, "GroupAccountPicker");
    private ContentControl NativeGroupAccountPickerPresenter => DeferredControl<ContentControl>(DeferredGroupEditor, "NativeGroupAccountPickerPresenter");
    private ImportReviewMobilePage ExternalImportReviewPage => DeferredControl<ImportReviewMobilePage>(DeferredImportReview, "ExternalImportReviewPage");
    private Grid AccountBrandIconPickerModalOverlay => DeferredControl<Grid>(DeferredAccountBrandIconPickerModalOverlay, "AccountBrandIconPickerModalOverlay");

    private static T DeferredControl<T>(DeferredContent host, string name) where T : Control
    {
        var root = host.EnsureContentCreated();
        return root.GetLogicalDescendants().OfType<T>().Prepend(root as T)
            .FirstOrDefault(control => control?.Name == name)
            ?? throw new InvalidOperationException($"The deferred control {name} is unavailable.");
    }

    private void InitializeDeferredContent()
    {
        DeferredImportReview.ContentCreated += (_, _) => AttachNativeImportReview();
        DeferredGroupEditor.ContentCreated += (_, _) => AttachNativeGroupAccountPicker();
        DeferredAccountBrandIconPickerModalOverlay.ContentCreated += (_, _) =>
            UpdateAccountBrandIconPickerInputPane(_inputPaneState, _inputPaneOccludedRect);
        DeferredEncryptedAccountImportPasswordDialog.ContentCreated += (_, _) =>
            Dispatcher.UIThread.Post(UpdateImportPasswordDialogInputPane, DispatcherPriority.Loaded);
        DeferredEncryptedAccountImportPasswordDialog.SizeChanged += (_, _) =>
            UpdateImportPasswordDialogInputPane();
    }

    private void AttachNativeImportReview()
    {
        if (_nativeImportReview is not null && DeferredImportReview.IsContentCreated)
            ExternalImportReviewPage.UseNativeList(_nativeImportReview);
    }

    private void AttachNativeGroupAccountPicker()
    {
        if (_nativeGroupAccountPicker is null || !DeferredGroupEditor.IsContentCreated) return;
        GroupAccountPicker.IsVisible = false;
        GroupAccountPicker.ItemsSource = null;
        NativeGroupAccountPickerPresenter.Content = _nativeGroupAccountPicker;
        NativeGroupAccountPickerPresenter.IsHitTestVisible = true;
    }

    private void BeginSecondaryPreload()
    {
        if (TopLevel.GetTopLevel(this) is null || IsNativeStartupSplashPending) return;
        _secondaryPreloadLifetime?.Cancel();
        _secondaryPreloadLifetime?.Dispose();
        var lifetime = _secondaryPreloadLifetime = new CancellationTokenSource();
        _ = PreloadSecondaryViewsAsync(lifetime.Token);
    }

    private async Task PreloadSecondaryViewsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!cancellationToken.IsCancellationRequested)
                    _startupViewModel?.PreloadSecondaryResources();
            }, DispatcherPriority.ApplicationIdle);
            DeferredContent[] views =
            [
                DeferredInfoSettings,
                DeferredAppearanceSettings,
                DeferredDiagnosticLogSettingsPanel,
                DeferredProblemReportSettingsPanel,
                DeferredSupportStorageLocationsPanel,
                DeferredMasterPasswordChangePanel,
                DeferredAppLockPanel,
                DeferredUnlockMethodPanel,
                DeferredScreenCapturePanel,
                DeferredBrandIconSettings,
                DeferredBackupImport,
                DeferredAutomaticBackupPanel,
                DeferredBackupExportPanel,
                DeferredFaqMasterPasswordRecoveryExpander,
                DeferredFaqOlderVaultsExpander,
                DeferredFaqCreateBackupExpander,
                DeferredFaqIconPacksExpander,
                DeferredFaqImportFormatsExpander,
                DeferredGroupEditor,
                DeferredAccountEditor,
                DeferredMobileOnboardingOverlay,
                DeferredImportReview,
                DeferredNotification,
                DeferredImportProgressOverlay,
                DeferredImportFileSummary,
                DeferredEncryptedAccountImportPasswordDialog,
                DeferredImportConfirmation,
                DeferredQrImportResolution,
                DeferredQrImportResult,
                DeferredBackupConflictResolution,
                DeferredAccountBrandIconPickerModalOverlay,
                DeferredAccountEditorExitModalOverlay,
                DeferredGroupDeleteModalOverlay,
                DeferredDeleteConfirmation,
                DeferredQuickUnlockOffer
            ];
            foreach (var view in views)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (!cancellationToken.IsCancellationRequested)
                        view.Preload(Math.Max(0, MobileMainContent.Bounds.Width));
                }, DispatcherPriority.ApplicationIdle);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _startupViewModel?.ReportSecondaryViewPreloadFailure(exception);
        }
    }
}
