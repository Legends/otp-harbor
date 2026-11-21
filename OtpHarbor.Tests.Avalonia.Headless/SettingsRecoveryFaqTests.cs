using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using TOTP.Avalonia.Desktop;
using TOTP.Avalonia.Desktop.Localization;

namespace TOTP.Tests.Avalonia.Headless;

public sealed class SettingsRecoveryFaqTests
{
    [AvaloniaTheory]
    [InlineData(520, 480)]
    [InlineData(520, 620)]
    [InlineData(800, 620)]
    public void RecoveryAnswers_UseActiveLocaleAndRemainScrollableWithoutHorizontalOverflow(int width, int height)
    {
        var window = new SettingsWindow { Width = width, Height = height };
        var catalog = new AvaloniaStringCatalog();
        var localization = new AvaloniaLocalizationService(window.Resources, catalog);
        (string Name, string Question, string Answer)[] entries =
        [
            ("FaqMasterPasswordRecoveryExpander", AvaloniaStringKeys.FaqMasterPasswordRecoveryQuestion, AvaloniaStringKeys.FaqMasterPasswordRecoveryAnswer),
            ("FaqOlderVaultsExpander", AvaloniaStringKeys.FaqOlderVaultsQuestion, AvaloniaStringKeys.FaqOlderVaultsAnswer),
            ("FaqCreateBackupExpander", AvaloniaStringKeys.FaqCreateBackupQuestion, AvaloniaStringKeys.FaqCreateBackupAnswer),
            ("FaqBackupLocationsExpander", AvaloniaStringKeys.FaqBackupLocationsQuestion, AvaloniaStringKeys.FaqBackupLocationsAnswer),
            ("FaqForgottenPasswordExpander", AvaloniaStringKeys.FaqForgottenPasswordQuestion, AvaloniaStringKeys.FaqForgottenPasswordAnswer)
        ];
        try
        {
            window.Show();
            var tabs = Assert.Single(window.GetVisualDescendants().OfType<TabControl>(),
                control => control.Classes.Contains("settings-tabs"));
            tabs.SelectedIndex = 5;
            Assert.Null(window.FindControl<Expander>("FaqAuthorizationChangesExpander"));
            foreach (var locale in new[] { "en", "de", "fr", "es" })
            {
                localization.ApplyCulture(locale);
                var culture = CultureInfo.GetCultureInfo(locale);
                foreach (var entry in entries)
                {
                    var section = Assert.IsType<Expander>(window.FindControl<Expander>(entry.Name));
                    section.IsExpanded = true;
                    section.RaiseEvent(new RoutedEventArgs(Expander.ExpandedEvent));
                    window.UpdateLayout();
                    var question = Assert.Single(Assert.IsType<Grid>(section.Header).Children.OfType<TextBlock>());
                    var answer = Assert.IsType<TextBlock>(Assert.IsType<Border>(section.Content).Child);
                    Assert.Equal(catalog.Get(entry.Question, culture), question.Text);
                    Assert.Equal(catalog.Get(entry.Answer, culture), answer.Text);
                    var scroller = Assert.Single(section.GetVisualAncestors().OfType<ScrollViewer>());
                    Assert.True(scroller.Viewport.Width > 0);
                    Assert.True(scroller.Extent.Width <= scroller.Viewport.Width + 1);
                    foreach (var text in new[] { question, answer })
                    {
                        var left = text.TranslatePoint(default, scroller)!.Value.X;
                        Assert.True(left >= -1 && left + text.Bounds.Width <= scroller.Viewport.Width + 1,
                            $"FAQ text exceeds the {locale} viewport at {width} x {height}.");
                    }

                    var answerBottom = answer.TranslatePoint(new Point(0, answer.Bounds.Height), scroller)!.Value.Y;
                    scroller.Offset = new Vector(0, scroller.Offset.Y + answerBottom - scroller.Viewport.Height);
                    window.UpdateLayout();
                    var visibleBottom = answer.TranslatePoint(new Point(0, answer.Bounds.Height), scroller)!.Value.Y;
                    Assert.InRange(visibleBottom, 1, scroller.Viewport.Height + 1);
                }
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void RecoveryAndExistingFaqSections_ShareOneAccordion()
    {
        var window = new SettingsWindow();
        try
        {
            window.Show();
            var sections = new[] { "FaqIconPacksExpander", "FaqMasterPasswordRecoveryExpander",
                "FaqOlderVaultsExpander", "FaqImportFormatsExpander", "FaqCreateBackupExpander", "FaqBackupLocationsExpander", "FaqForgottenPasswordExpander" }
                .Select(name => Assert.IsType<Expander>(window.FindControl<Expander>(name))).ToArray();
            foreach (var expanded in sections)
            {
                expanded.IsExpanded = true;
                expanded.RaiseEvent(new RoutedEventArgs(Expander.ExpandedEvent));
                Assert.Same(expanded, Assert.Single(sections, section => section.IsExpanded));
            }
        }
        finally { window.Close(); }
    }
}
