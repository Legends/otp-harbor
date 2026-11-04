using Avalonia.Controls;
using Avalonia.LogicalTree;

namespace TOTP.Avalonia.Desktop.Dialogs;

internal static class DialogWindowSizing
{
    private const double PreferredWidth = 520;
    private const double MinimumWidth = 320;
    private const double OwnerMargin = 16;

    public static void FitToOwner(Window dialog, Window owner)
    {
        ArgumentNullException.ThrowIfNull(dialog);
        ArgumentNullException.ThrowIfNull(owner);

        var availableWidth = Math.Max(0, owner.ClientSize.Width - (OwnerMargin * 2));
        if (availableWidth <= 0) return;

        var width = Math.Min(PreferredWidth, availableWidth);
        dialog.MinWidth = Math.Min(MinimumWidth, width);
        dialog.Width = width;
        dialog.MaxWidth = availableWidth;
        if (dialog is ChoiceDialogWindow or ConfirmationDialogWindow)
        {
            dialog.MaxHeight = Math.Max(280, owner.ClientSize.Height - (OwnerMargin * 2));
            var contentScroll = dialog.FindControl<ScrollViewer>("DialogContentScroll");
            if (contentScroll is not null)
                contentScroll.MaxHeight = Math.Min(460,
                    Math.Max(120, owner.ClientSize.Height - 200));
        }
        foreach (var messageCard in dialog.GetLogicalDescendants().OfType<Border>())
        {
            if (messageCard.Classes.Contains("dialog-message-card")
                && messageCard.Child is TextBlock message)
            {
                message.Width = Math.Max(0, width - 100);
            }
        }
    }
}
