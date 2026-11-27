using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Platform;
using Avalonia.Headless.XUnit;
using Avalonia.Headless;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentResults;
using Moq;
using TOTP.Avalonia.Mobile.Platform;
using TOTP.Avalonia.Mobile.Views;
using TOTP.Core.Models;
using TOTP.Core.Services.Interfaces;
using TOTP.Core.Services.Models;

namespace TOTP.Tests.Avalonia.Headless;

public sealed partial class NativeStartupSplashTests
{
    [AvaloniaTheory]
    [InlineData(360, 640, 300, "en")]
    [InlineData(390, 800, 360, "de")]
    [InlineData(430, 720, 350, "fr")]
    [InlineData(360, 580, 300, "es")]
    public async Task BackupPasswordDialog_KeepsActionsAboveKeyboardAndRestoresAfterClosing(
        int width, int height, int keyboardHeight, string culture)
    {
        var documents = new Mock<IMobileDocumentService>();
        documents.Setup(service => service.OpenAccountImportAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MobileReadableDocument(new MemoryStream([1]), "synthetic.otphbp"));
        var exports = new Mock<IExportService>();
        exports.Setup(service => service.ImportFromStreamWithFormatSelectionAsync(It.IsAny<Stream>(), It.IsAny<string>(),
                It.IsAny<Func<IReadOnlyList<AccountImportFormat>, CancellationToken, Task<AccountImportFormat?>>>(),
                It.IsAny<Func<AccountImportPasswordRequest, CancellationToken, Task<Result<List<Account>>>>>(), It.IsAny<CancellationToken>()))
            .Returns((Stream _, string _, Func<IReadOnlyList<AccountImportFormat>, CancellationToken, Task<AccountImportFormat?>> _,
                Func<AccountImportPasswordRequest, CancellationToken, Task<Result<List<Account>>>> unlock, CancellationToken token) =>
                unlock(new(false, (_, _) => Task.FromResult(Result.Fail<List<Account>>("Synthetic wrong password"))), token));
        var context = CreateContext(culture: culture, documents: documents, exports: exports);
        await context.Model.InitializeAsync();
        await context.Model.ShowSettingsAsync();
        var importing = context.Model.ImportAccountFileAsync();
        Assert.True(context.Model.IsImportBackupPasswordVisible);
        var view = CreateView(context.Model);
        var window = new Window { Width = width, Height = height, Content = view };
        window.Styles.Add(new StyleInclude(new Uri("avares://TOTP.Tests.Avalonia.Headless/"))
        {
            Source = new Uri("avares://TOTP.UI.Avalonia.Mobile/Styles/MobileStyles.axaml")
        });
        try
        {
            window.Show();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            var inputPane = typeof(MainView).GetMethod("UpdatePasswordConfirmationInputPane", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var keyboardTop = height - keyboardHeight;
            inputPane.Invoke(view, [InputPaneState.Open, new Rect(0, keyboardTop, width, keyboardHeight)]);
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            var actions = view.GetVisualDescendants().OfType<Button>().Where(button =>
                button.Command == context.Model.CancelImportBackupUnlockCommand || button.Command == context.Model.UnlockImportBackupCommand).ToArray();
            Assert.Equal(2, actions.Length);
            var unlock = Assert.Single(actions, button => button.Command == context.Model.UnlockImportBackupCommand);
            Assert.Contains(unlock.GetVisualDescendants().OfType<TextBlock>(), label => label.Text == context.Model.UnlockText);
            foreach (var action in actions)
            {
                var origin = action.TranslatePoint(default, window)!.Value;
                Assert.True(origin.Y >= 0 && origin.Y + action.Bounds.Height <= keyboardTop + 1,
                    $"Password action extends below the keyboard at {width}x{height} ({culture}).");
                Assert.True(action.Bounds.Height >= 44);
                window.MouseMove(action.TranslatePoint(new Point(4, 4), window)!.Value);
                action.Focus();
                window.UpdateLayout();
                Assert.Equal(global::Avalonia.Layout.VerticalAlignment.Center, action.VerticalContentAlignment);
                action.IsEnabled = false;
                window.UpdateLayout();
                Assert.Equal(global::Avalonia.Layout.VerticalAlignment.Center, action.VerticalContentAlignment);
                action.IsEnabled = true;
                Assert.All(action.GetVisualDescendants().OfType<TextBlock>(), label =>
                    Assert.True(label.TextLayout.Width <= label.Bounds.Width + 1, $"Password action is clipped in {culture}."));
            }
            view.Height = keyboardTop;
            view.VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Top;
            window.UpdateLayout();
            inputPane.Invoke(view, [InputPaneState.Open, new Rect(0, keyboardTop, width, keyboardHeight)]);
            window.UpdateLayout();
            var resizedOverlay = view.GetVisualDescendants().OfType<Grid>().Single(control => control.Name == "EncryptedAccountImportPasswordDialog");
            Assert.Equal(0, resizedOverlay.Margin.Bottom);
            inputPane.Invoke(view, [InputPaneState.Closed, default(Rect)]);
            window.UpdateLayout();
            var overlay = view.GetVisualDescendants().OfType<Grid>().Single(control => control.Name == "EncryptedAccountImportPasswordDialog");
            Assert.Equal(default, overlay.Margin);
            context.Model.ImportPassword = "Synthetic pending password";
            context.Model.CancelImportBackupUnlockCommand.Execute(null);
            await importing;
            Assert.False(context.Model.IsImportBackupPasswordVisible);
            Assert.Empty(context.Model.ImportPassword);
        }
        finally { window.Close(); }
    }
}
