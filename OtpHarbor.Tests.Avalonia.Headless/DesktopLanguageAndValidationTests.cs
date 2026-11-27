using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentResults;
using Moq;
using TOTP.Avalonia.Desktop;
using TOTP.Avalonia.Desktop.Localization;
using TOTP.Avalonia.Desktop.Presentation;
using TOTP.Avalonia.Desktop.Platform;
using TOTP.Avalonia.Shared.Controls;
using TOTP.Core.Models;
using TOTP.Core.Security.Interfaces;
using TOTP.Core.Services.Interfaces;

namespace TOTP.Tests.Avalonia.Headless;

public sealed class DesktopLanguageAndValidationTests
{
    [AvaloniaFact]
    public void LanguageChange_PublishesOneCompleteResourceUpdateAndSkipsEquivalentCulture()
    {
        var host = new Border();
        host.Resources["UnrelatedResource"] = "preserved";
        var catalog = new AvaloniaStringCatalog();
        var localization = new AvaloniaLocalizationService(host.Resources, catalog);
        localization.ApplyCulture("en");
        var updates = 0;
        var cultures = 0;
        host.ResourcesChanged += (_, _) =>
        {
            updates++;
            foreach (var key in AvaloniaStringKeys.All)
                Assert.Equal(catalog.Get(key, System.Globalization.CultureInfo.GetCultureInfo("de")), host.Resources[key]);
        };
        localization.CultureChanged += (_, _) => cultures++;

        localization.ApplyCulture("de");
        localization.ApplyCulture("de-DE");

        Assert.Equal(1, updates);
        Assert.Equal(1, cultures);
        Assert.Equal("preserved", host.Resources["UnrelatedResource"]);
    }

    [AvaloniaTheory]
    [InlineData("en")]
    [InlineData("de")]
    [InlineData("fr")]
    [InlineData("es")]
    public async Task DuplicateGroupName_IsInlineVisibleAndKeepsDraftAndSelectionsAcrossLanguageChange(string culture)
    {
        var work = new AccountGroup(Guid.NewGuid(), "Work", "#4F6BED");
        var personal = new AccountGroup(Guid.NewGuid(), "Personal", "#E45757");
        IReadOnlyList<Account> accounts = Enumerable.Range(0, 1200).Select(index =>
            new Account(Guid.NewGuid(), $"Synthetic {index:D4}", "JBSWY3DPEHPK3PXP", "test")
                .WithGroupMembership(index % 2 == 0 ? work : personal, true)).ToArray();
        var manager = new Mock<IAccountManager>();
        manager.Setup(service => service.GetAllOtpEntriesSortedAsync()).ReturnsAsync(Result.Ok(accounts));
        var localization = new AvaloniaLocalizationService(new ResourceDictionary(), new AvaloniaStringCatalog());
        localization.ApplyCulture(culture);
        using var model = new AccountListViewModel(manager.Object, Mock.Of<IAccountTotpService>(), Mock.Of<IAsyncClipboardService>(),
            Mock.Of<IAccountQrCodeService>(), Mock.Of<IAvaloniaQrImageFactory>(), Mock.Of<IAvaloniaDialogService>(), localization);
        await model.LoadAsync();
        model.Groups.Single(group => group.Id == work.Id).EditCommand.Execute(null);
        model.GroupEditorName = "personal";
        var groups = model.Groups;
        var selections = model.GroupEditorAccounts;
        var iconRefreshes = 0;
        model.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(model.EditorBrandIconOptions)) iconRefreshes++; };
        var main = new MainWindow();
        var window = new Window { Width = 380, Height = 420 };
        try
        {
            main.Show();
            var page = main.FindControl<ContentControl>("AccountListPage")!;
            var content = page.ContentTemplate!.Build(model)!;
            content.DataContext = model;
            window.Content = content;
            window.Show();
            window.UpdateLayout();
            var scroller = window.GetVisualDescendants().OfType<ScrollViewer>().Single(control => control.Name == "GroupEditorScrollViewer");
            var search = window.GetVisualDescendants().OfType<TextBox>().Single(control => control.Name == "GroupAccountSearchBox");
            search.Focus();
            scroller.ScrollToEnd();
            window.UpdateLayout();
            Assert.True(scroller.Offset.Y > 0);

            await model.SaveGroupAsync();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var error = window.GetVisualDescendants().OfType<ValidationMessage>().Single(control => control.Name == "GroupEditorNameValidationMessage");
            Assert.Equal(localization.GetString(AvaloniaStringKeys.GroupNameDuplicate), error.Text);
            AssertVisibleInside(error, scroller);
            Assert.True(search.IsFocused);
            Assert.Empty(model.GroupEditorGeneralMessage);
            manager.Verify(service => service.SaveGroupAsync(It.IsAny<AccountGroup>(), It.IsAny<IReadOnlyCollection<Guid>>()), Times.Never);

            localization.ApplyCulture(culture == "de" ? "fr" : "de");
            Assert.Same(groups, model.Groups);
            Assert.Same(selections, model.GroupEditorAccounts);
            Assert.Equal(600, selections.Count(account => account.IsSelected));
            Assert.Equal("personal", model.GroupEditorName);
            Assert.Equal(localization.GetString(AvaloniaStringKeys.GroupNameDuplicate), error.Text);
            Assert.Equal(0, iconRefreshes);

            model.GroupEditorName = "Unique";
            Assert.Empty(model.GroupEditorNameMessage);
            foreach (var account in model.GroupEditorAccounts) account.IsSelected = false;
            scroller.ScrollToEnd();
            await model.SaveGroupAsync();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            var general = window.GetVisualDescendants().OfType<ValidationMessage>().Single(control => control.Name == "GroupEditorGeneralValidationMessage");
            Assert.Equal(localization.GetString(AvaloniaStringKeys.GroupAccountRequired), general.Text);
            Assert.Empty(model.GroupEditorNameMessage);
            AssertVisibleInside(general, window);
            Assert.DoesNotContain(general.GetVisualAncestors(), ancestor => ancestor == scroller);
            manager.Setup(service => service.SaveGroupAsync(It.IsAny<AccountGroup>(), It.IsAny<IReadOnlyCollection<Guid>>()))
                .ReturnsAsync(Result.Fail("Synthetic storage failure"));
            model.GroupEditorAccounts[0].IsSelected = true;
            await model.SaveGroupAsync();
            window.UpdateLayout();
            Assert.Equal(localization.GetString(AvaloniaStringKeys.GroupSaveFailed), general.Text);
            AssertVisibleInside(general, window);
            Assert.True(model.IsGroupEditorVisible);
            Assert.Equal("Unique", model.GroupEditorName);
        }
        finally { window.Close(); main.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void DialogError_RevealsFeedbackWithoutMovingFocus_AndSuccessDoesNotScroll(bool notification)
    {
        var field = new TextBox();
        Control feedback = notification ? new NotificationBanner() : new ValidationMessage();
        var panel = new StackPanel();
        panel.Children.Add(field);
        panel.Children.Add(new Border { Height = 700, Background = Brushes.Transparent });
        panel.Children.Add(feedback);
        panel.Children.Add(new Border { Height = 500 });
        var scroller = new ScrollViewer { Content = panel };
        var window = new Window { Width = 400, Height = 300, Content = scroller };
        try
        {
            window.Show();
            field.Focus();
            if (feedback is NotificationBanner banner) { banner.Severity = NotificationSeverity.Error; banner.Text = "Synthetic error"; }
            else { var message = (ValidationMessage)feedback; message.Severity = ValidationSeverity.Error; message.Text = "Synthetic error"; }
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            AssertVisibleInside(feedback, scroller);
            Assert.True(field.IsFocused);
            scroller.ScrollToHome();
            if (feedback is NotificationBanner success) { success.Severity = NotificationSeverity.Success; success.Text = "Synthetic success"; }
            else { var info = (ValidationMessage)feedback; info.Severity = ValidationSeverity.Information; info.Text = "Synthetic information"; }
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Assert.Equal(0, scroller.Offset.Y);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void MultipleDialogErrors_RevealFirstField_AndClearedErrorsDoNotScroll()
    {
        var first = new ValidationMessage { Severity = ValidationSeverity.Error };
        var second = new ValidationMessage { Severity = ValidationSeverity.Error };
        var panel = new StackPanel();
        panel.Children.Add(new Border { Height = 500 });
        panel.Children.Add(first);
        panel.Children.Add(new Border { Height = 700 });
        panel.Children.Add(second);
        panel.Children.Add(new Border { Height = 500 });
        var scroller = new ScrollViewer { Content = panel };
        var window = new Window { Width = 400, Height = 300, Content = scroller };
        try
        {
            window.Show();
            first.Text = "Synthetic first error";
            second.Text = "Synthetic second error";
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            AssertVisibleInside(first, scroller);
            Assert.True(second.TranslatePoint(default, scroller)!.Value.Y > scroller.Bounds.Height);
            first.Text = second.Text = string.Empty;
            scroller.ScrollToHome();
            window.UpdateLayout();
            first.Text = "Synthetic error already corrected";
            first.Text = string.Empty;
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Assert.Equal(0, scroller.Offset.Y);
        }
        finally { window.Close(); }
    }

    private static void AssertVisibleInside(Control control, Control viewport)
    {
        var origin = control.TranslatePoint(default, viewport)!.Value;
        Assert.True(origin.Y >= -1 && origin.Y + control.Bounds.Height <= viewport.Bounds.Height + 1,
            $"{control.Name ?? control.GetType().Name}: {origin.Y}+{control.Bounds.Height} outside {viewport.Bounds.Height}.");
    }
}
