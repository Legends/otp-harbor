using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentResults;
using Moq;
using SkiaSharp;
using TOTP.Avalonia.Mobile.Platform;
using TOTP.Core.Models;
using TOTP.Core.Services.Interfaces;
using TOTP.Core.Services.Models;

namespace TOTP.Tests.Avalonia.Headless;

public sealed partial class NativeStartupSplashTests
{
    [AvaloniaTheory]
    [InlineData(360, 640, "en", false, 1200)]
    [InlineData(430, 800, "de", true, 1200)]
    [InlineData(360, 580, "fr", true, 1200)]
    [InlineData(430, 720, "es", false, 1200)]
    [InlineData(360, 320, "de", false, 1234567)]
    [InlineData(360, 320, "fr", true, 1234567)]
    public async Task FileImportResult_RendersStructuredLocalizedCountsAndReachableDone(
        int width, int height, string culture, bool dark, int added)
    {
        var documents = new Mock<IMobileDocumentService>();
        documents.Setup(service => service.OpenAccountImportAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MobileReadableDocument(new MemoryStream([1]), "synthetic.txt"));
        var exports = new Mock<IExportService>();
        exports.Setup(service => service.ImportFromStreamWithFormatSelectionAsync(It.IsAny<Stream>(), It.IsAny<string>(),
                It.IsAny<Func<IReadOnlyList<AccountImportFormat>, CancellationToken, Task<AccountImportFormat?>>>(),
                It.IsAny<Func<AccountImportPasswordRequest, CancellationToken, Task<Result<List<Account>>>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok(new List<Account> { new(Guid.NewGuid(), "Synthetic", "JBSWY3DPEHPK3PXP", "test") }));
        var imports = new Mock<IAccountImportService>();
        imports.Setup(service => service.ImportWithConflictResolutionAsync(It.IsAny<IReadOnlyList<Account>>(),
                It.IsAny<Func<AccountImportPreview, CancellationToken, Task<AccountImportResolution?>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Ok(new AccountImportOutcome(AccountImportStatus.Completed, added, 35, 7)));
        var context = CreateContext(culture: culture, documents: documents, exports: exports, imports: imports);
        context.Settings.Setup(service => service.SaveAsync()).ReturnsAsync(Result.Ok());
        await context.Model.InitializeAsync();
        await context.Model.ShowSettingsAsync();
        await context.Model.ImportAccountFileAsync();
        Assert.True(context.Model.IsFileImportResult);
        var view = CreateView(context.Model);
        var window = new Window { Width = width, Height = height, Content = view,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        window.Styles.Add(new StyleInclude(new Uri("avares://TOTP.Tests.Avalonia.Headless/"))
        { Source = new Uri("avares://TOTP.UI.Avalonia.Mobile/Styles/MobileStyles.axaml") });
        try
        {
            window.Show();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            var surface = Assert.Single(view.GetVisualDescendants().OfType<Border>(), control => control.Name == "ImportResultDialogSurface");
            var summary = Assert.Single(surface.GetVisualDescendants().OfType<Border>(), control => control.Name == "FileImportResultSummary");
            var done = Assert.Single(surface.GetVisualDescendants().OfType<Button>());
            var doneOrigin = done.TranslatePoint(default, window)!.Value;
            Assert.InRange(doneOrigin.Y, 0, height - done.Bounds.Height);
            Assert.True(done.Bounds.Height >= 48);
            Assert.True(done.Bounds.Width >= summary.Bounds.Width - 1);
            Assert.Equal(VerticalAlignment.Center, done.VerticalContentAlignment);
            var doneLabel = Assert.Single(done.GetVisualDescendants().OfType<TextBlock>());
            Assert.Equal(context.Model.ImportResultDismissText, doneLabel.Text);
            var rowGrid = Assert.IsType<Grid>(summary.Child);
            foreach (var (row, label, count) in new[]
            {
                (0, context.Model.ImportAccountsAddedText, added.ToString("N0", CultureInfo.GetCultureInfo(culture))),
                (2, context.Model.ImportAccountsOverwrittenText, "35"),
                (4, context.Model.ImportAccountsUnchangedText, "7")
            })
            {
                var title = Assert.Single(rowGrid.Children.OfType<TextBlock>(), value => Grid.GetRow(value) == row && Grid.GetColumn(value) == 0);
                var number = Assert.Single(rowGrid.Children.OfType<TextBlock>(), value => Grid.GetRow(value) == row && Grid.GetColumn(value) == 1);
                Assert.Equal(label, title.Text);
                Assert.Equal(count, number.Text);
                Assert.True(title.Bounds.Right < number.Bounds.Left);
                Assert.True(title.Bounds.Height >= title.TextLayout.Height - 1);
                Assert.True(number.Bounds.Right <= rowGrid.Bounds.Width + 1);
            }
            var body = Assert.Single(surface.GetVisualDescendants().OfType<ScrollViewer>());
            body.ScrollToEnd();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            var lastCount = rowGrid.Children.OfType<TextBlock>().Single(value => value.Name == "ImportAccountsUnchangedCount");
            var lastOrigin = lastCount.TranslatePoint(default, body)!.Value;
            Assert.True(lastOrigin.Y >= 0 && lastOrigin.Y + lastCount.Bounds.Height <= body.Bounds.Height + 1);
            body.ScrollToHome();
            window.UpdateLayout();
            using var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            using var encoded = new MemoryStream();
            frame.Save(encoded, new PngBitmapEncoderOptions());
            using var pixels = SKBitmap.Decode(encoded.ToArray());
            AssertRenderedInk(doneLabel, window, pixels);
            if (height >= 580)
            {
                foreach (var text in summary.GetVisualDescendants().OfType<TextBlock>())
                    AssertRenderedInk(text, window, pixels);
            }
            if (Environment.GetEnvironmentVariable("OTP_HARBOR_TEST_PREVIEW_DIR") is { Length: > 0 } preview)
            {
                Directory.CreateDirectory(preview);
                frame.Save(Path.Combine(preview, $"import-result-{culture}-{height}.png"), new PngBitmapEncoderOptions());
            }
            var nextCulture = culture == "en" ? "de" : "en";
            await context.Model.SelectLanguageAsync(nextCulture);
            window.UpdateLayout();
            Assert.Equal(nextCulture, context.Preferences.CultureName);
            Assert.Equal(added.ToString("N0", CultureInfo.GetCultureInfo(nextCulture)), context.Model.ImportAccountsAddedCount);
            Assert.Equal(context.Model.ImportAccountsAddedCount, rowGrid.Children.OfType<TextBlock>().Single(value => value.Name == "ImportAccountsAddedCount").Text);
            Assert.Equal(context.Model.ImportAccountsAddedText, rowGrid.Children.OfType<TextBlock>().Single(value => Grid.GetRow(value) == 0 && Grid.GetColumn(value) == 0).Text);
            Assert.Equal(context.Model.ImportResultDismissText, doneLabel.Text);
            done.Command!.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.False(context.Model.IsQrImportResultVisible);
            Assert.False(context.Model.IsFileImportResult);
            Assert.Equal("0", context.Model.ImportAccountsAddedCount);
        }
        finally { window.Close(); }
    }
}
