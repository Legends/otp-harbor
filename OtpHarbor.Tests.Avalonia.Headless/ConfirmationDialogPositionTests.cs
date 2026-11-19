using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using TOTP.Avalonia.Desktop.Dialogs;
using TOTP.Avalonia.Desktop.Presentation.Dialogs;
using TOTP.Core.Services.Interfaces;

namespace TOTP.Tests.Avalonia.Headless;

public sealed class ConfirmationDialogPositionTests
{
    [AvaloniaTheory]
    [InlineData(380, 540)]
    [InlineData(720, 540)]
    public async Task ContentHeightChanges_KeepOwnedDialogCenteredAndClearOfBottomEdge(int width, int height)
    {
        var owner = new Window { Width = width, Height = height, Position = new PixelPoint(200, 100) };
        var dialog = new ConfirmationDialogWindow { DataContext = Model("Synthetic short message.") };
        try
        {
            owner.Show();
            owner.UpdateLayout();
            typeof(ConfirmationDialogWindow).GetMethod("ConfigureForOwner", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(dialog, [owner, false]);
            _ = dialog.ShowDialog<bool>(owner);
            dialog.UpdateLayout();
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Loaded);
            var originalHeight = dialog.ClientSize.Height;

            dialog.DataContext = Model(string.Join(" ", Enumerable.Repeat("Synthetic text with multiple lines after layout.", 8)));
            dialog.UpdateLayout();
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Loaded);
            Assert.True(dialog.ClientSize.Height > originalHeight);
            var ownerOrigin = owner.PointToScreen(default);
            var expectedY = ownerOrigin.Y + (int)Math.Round(
                (owner.ClientSize.Height * owner.RenderScaling - dialog.ClientSize.Height * dialog.RenderScaling) / 2);
            Assert.Equal(expectedY, dialog.Position.Y);
            var bottomInset = ownerOrigin.Y + owner.ClientSize.Height * owner.RenderScaling
                - dialog.Position.Y - dialog.ClientSize.Height * dialog.RenderScaling;
            Assert.True(bottomInset >= 16 * owner.RenderScaling - 1,
                $"The dialog has only {bottomInset} physical pixels below it.");
        }
        finally { dialog.Close(); owner.Close(); }
    }

    private static ConfirmationDialogViewModel Model(string message) => new(new ConfirmationDialogRequest(
        "Synthetic quick-unlock offer", message, NotificationSeverity.Information,
        "Synthetic enable", "Synthetic skip", StackActions: true));
}
