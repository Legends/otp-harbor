using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentResults;
using Moq;

namespace TOTP.Tests.Avalonia.Headless;

public sealed partial class NativeStartupSplashTests
{
    [AvaloniaTheory]
    [InlineData(360, "de")]
    [InlineData(430, "es")]
    public async Task MobileSecuritySwitch_WrapsLabelAndKeepsTwoWaySettingsBinding(int width, string culture)
    {
        var context = CreateContext(culture: culture);
        context.Settings.Setup(service => service.SaveAsync()).ReturnsAsync(Result.Ok());
        await context.Model.InitializeAsync();
        await context.Model.ShowSettingsAsync();
        context.Model.ShowSecuritySettingsCommand.Execute(null);
        var view = CreateView(context.Model);
        var window = new Window { Width = width, Height = 640, Content = view };
        window.Styles.Add(new StyleInclude(new Uri("avares://TOTP.Tests.Avalonia.Headless/"))
        {
            Source = new Uri("avares://TOTP.UI.Avalonia.Mobile/Styles/MobileStyles.axaml")
        });
        try
        {
            window.Show();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            var toggle = Assert.Single(view.GetVisualDescendants().OfType<ToggleSwitch>(),
                control => AutomationProperties.GetName(control) == context.Model.AllowScreenshotsText);
            Assert.True(toggle.Bounds.Width >= 44 && toggle.Bounds.Height >= 44);
            toggle.IsChecked = false;
            Assert.False(context.Model.AllowScreenshots);
            context.Model.AllowScreenshots = true;
            Assert.True(toggle.IsChecked);
            var label = Assert.Single(view.GetVisualDescendants().OfType<TextBlock>(),
                control => control.Text == context.Model.AllowScreenshotsText);
            Assert.All(label.TextLayout.TextLines, line => Assert.False(line.HasCollapsed));
            var position = label.TranslatePoint(default, window)!.Value;
            Assert.True(position.X >= 0 && position.X + label.Bounds.Width <= width + 1);
            Assert.DoesNotContain(view.GetVisualDescendants(), control => control is CheckBox);
        }
        finally { window.Close(); }
    }
}
