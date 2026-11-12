using System.Globalization;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Avalonia.LogicalTree;
using TOTP.Avalonia.Desktop.Localization;
using TOTP.Avalonia.Shared.Controls;
using TOTP.Avalonia.Shared.Import;

namespace TOTP.Tests.Avalonia.Headless;

public sealed class AuthenticatorImportPanelTests
{
    [AvaloniaTheory]
    [InlineData(360, true, "de")]
    [InlineData(720, false, "fr")]
    public async Task DetectedFileSummary_ShowsCountsAndFormatOnlyInModalContent(int width, bool dark, string language)
    {
        var catalog = new AvaloniaStringCatalog();
        var model = new AuthenticatorImportViewModel(key => catalog.Get(key, CultureInfo.GetCultureInfo(language)), new Command(), new Command());
        var pending = model.WaitForFileReviewAsync(new TOTP.Core.Services.Models.AccountImportReadSummary(23, nonOtpCount: 1)
            { Format = TOTP.Core.Services.Models.AccountImportFormat.AegisJson }, "renamed.csv", TestContext.Current.CancellationToken);
        var landing = new AuthenticatorImportPanel { DataContext = model };
        var panel = new ImportFileSummaryPanel { DataContext = model };
        var scroll = new ScrollViewer { Content = panel, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var window = new Window { Width = width, Height = 600, Content = scroll, RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        try
        {
            window.Show();
            window.UpdateLayout();
            Assert.Null(model.SelectedSource);
            Assert.DoesNotContain(landing.GetLogicalDescendants().OfType<Button>(), item => ReferenceEquals(item.Command, model.ReviewFileCommand));
            Assert.Contains(panel.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == model.DetectedFormatText && text.IsEffectivelyVisible);
            Assert.Equal(24, model.TotalEntries);
            var button = panel.GetVisualDescendants().OfType<Button>().Single(button => ReferenceEquals(button.Command, model.ReviewFileCommand));
            button.BringIntoView();
            window.UpdateLayout();
            Assert.True(button.Bounds.Height >= 44);
            Assert.False(pending.IsCompleted);
            Assert.True(scroll.Extent.Width <= scroll.Viewport.Width + 1);
            button.Command!.Execute(null);
            Assert.True(await pending);
        }
        finally { model.CancelPendingFile(); window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(360, false, "de")]
    [InlineData(430, true, "fr")]
    [InlineData(520, true, "es")]
    [InlineData(720, false, "en")]
    public void OptionalHelp_DoesNotGateFileOrQrAndFitsViewport(int width, bool dark, string language)
    {
        var catalog = new AvaloniaStringCatalog();
        var model = new AuthenticatorImportViewModel(key => catalog.Get(key, CultureInfo.GetCultureInfo(language)), new Command(), new Command());
        var panel = new AuthenticatorImportPanel { DataContext = model, Margin = new Thickness(32) };
        var scroll = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = panel };
        var window = new Window { Width = width, Height = 420, Content = scroll, RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        try
        {
            window.Show();
            foreach (var source in model.Sources)
            {
                model.SelectedSource = source;
                window.UpdateLayout();
                Assert.True(scroll.Extent.Width <= scroll.Viewport.Width + 1);
                Assert.Single(panel.GetVisualDescendants().OfType<ComboBox>(), combo => ReferenceEquals(combo.ItemsSource, model.Sources));
                Assert.Contains(panel.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == model.Instructions);
                var actions = panel.GetVisualDescendants().OfType<Button>()
                    .Where(button => ReferenceEquals(button.Command, model.FileCommand) || ReferenceEquals(button.Command, model.QrCommand)).ToArray();
                Assert.Equal(2, actions.Length);
                Assert.Equal(model.CanImportFile, actions.Single(button => ReferenceEquals(button.Command, model.FileCommand)).IsVisible);
                Assert.Equal(model.CanImportQr, actions.Single(button => ReferenceEquals(button.Command, model.QrCommand)).IsVisible);
                foreach (var action in actions.Where(action => action.IsVisible)) Assert.True(action.Bounds.Height >= 44);
            }
        }
        finally { window.Close(); }
    }

    private sealed class Command : ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) { }
    }
}
