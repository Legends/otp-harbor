using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Input;
using Avalonia.Input.Raw;
using TOTP.Avalonia.Desktop.Localization;
using TOTP.Avalonia.Shared.Controls;
using TOTP.Avalonia.Shared.Import;
using TOTP.Core.Models;
using TOTP.Core.Services.Models;

namespace TOTP.Tests.Avalonia.Headless;

public sealed class ImportReviewPanelTests
{
    [AvaloniaFact]
    public void NativeBody_ReplacesFallbackScrollerAndKeepsSharedFooterAndReviewState()
    {
        var catalog = new AvaloniaStringCatalog();
        var model = new ImportReviewViewModel(new(1, 0, ImportConflictStrategy.SkipExisting)
            { NewCount = 1, Entries = [new("Demo", "alice", "SHA1", 6, 30)] }, null, "2FAS", "export.2fas",
            key => catalog.Get(key, CultureInfo.GetCultureInfo("en")));
        var page = new ImportReviewMobilePage { DataContext = model };
        var nativeBody = new Border();
        page.UseNativeList(nativeBody);
        var window = new Window { Width = 360, Height = 640, Content = page };
        try
        {
            window.Show();
            window.UpdateLayout();
            Assert.False(page.FindControl<ScrollViewer>("ReviewPageScroll")!.IsVisible);
            Assert.Null(page.FindControl<ItemsControl>("ReviewItems")!.ItemsSource);
            Assert.Same(nativeBody, page.FindControl<ContentControl>("NativeReviewPresenter")!.Content);
            Assert.True(nativeBody.Bounds.Height >= 480);
            Assert.Same(model, nativeBody.DataContext);
            var footerButtons = page.GetVisualDescendants().OfType<Button>().Where(button => button.IsEffectivelyVisible).ToArray();
            Assert.Equal(2, footerButtons.Length);
            Assert.Contains(footerButtons, button => ReferenceEquals(button.Command, model.BackCommand));
            Assert.Contains(footerButtons, button => ReferenceEquals(button.Command, model.ImportCommand));
            Assert.DoesNotContain(page.GetVisualDescendants(), control => control is ImportReviewAccountCard);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false, "en")]
    [InlineData(true, "de")]
    [InlineData(false, "fr")]
    [InlineData(true, "es")]
    public void DesktopButtons_AreCompactAndCenterTheirLabels(bool dark, string language)
    {
        var catalog = new AvaloniaStringCatalog();
        var model = new ImportReviewViewModel(new(1, 0, ImportConflictStrategy.SkipExisting)
        { NewCount = 1, Entries = [new("Demo", "alice", "SHA1", 6, 30)] }, null, "2FAS", "export.2fas",
            key => catalog.Get(key, CultureInfo.GetCultureInfo(language)));
        var panel = new ImportReviewPanel { DataContext = model };
        var window = new Window { Width = 1240, Height = 800, Content = panel,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        try
        {
            window.Show();
            window.UpdateLayout();
            var buttons = panel.GetVisualDescendants().OfType<Button>().Where(button =>
                button.Classes.Contains("review-category") ||
                ReferenceEquals(button.Command, model.BackCommand) || ReferenceEquals(button.Command, model.CancelCommand) ||
                ReferenceEquals(button.Command, model.ImportCommand)).ToArray();
            Assert.Equal(9, buttons.Length);
            foreach (var button in buttons)
            {
                Assert.InRange(button.Bounds.Height, 44, 80);
                Assert.InRange(button.Bounds.Width, 40, button.Classes.Contains("review-category") ? 150 : 220);
                Assert.Equal(global::Avalonia.Layout.HorizontalAlignment.Center, button.HorizontalContentAlignment);
                Assert.Equal(global::Avalonia.Layout.VerticalAlignment.Center, button.VerticalContentAlignment);
                var presenter = button.GetVisualDescendants().OfType<ContentPresenter>()
                    .Single(item => ReferenceEquals(item.TemplatedParent, button) && item.Name == "PART_ContentPresenter");
                Assert.Equal(global::Avalonia.Layout.HorizontalAlignment.Center, presenter.HorizontalContentAlignment);
                Assert.Equal(global::Avalonia.Layout.VerticalAlignment.Center, presenter.VerticalContentAlignment);
            }
            var actions = panel.GetVisualDescendants().OfType<StackPanel>().Single(stack => stack.Classes.Contains("review-footer-actions"));
            Assert.True(actions.Bounds.Width < 500);
            var position = actions.TranslatePoint(default, panel)!.Value;
            Assert.InRange(Math.Abs(position.X + actions.Bounds.Width - (panel.Bounds.Width - 16)), 0, 1);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void MobileCards_UseEqualHeaderWidthsAndNoSelectableOrPressedRowBackground(bool dark)
    {
        var catalog = new AvaloniaStringCatalog();
        var entries = new[] { "GitHub", "A much longer issuer name", "OTP Harbor" }
            .Select((issuer, index) => new AccountImportEntryPreview(issuer, index == 0 ? string.Empty : "account@example.test", "SHA1", 6, 30)
            { ImportIndex = index, Existing = new(issuer, index == 0 ? string.Empty : "account@example.test", "SHA1", 6, 30) }).ToArray();
        var model = new ImportReviewViewModel(new(3, 3, ImportConflictStrategy.SkipExisting)
        { Entries = entries, UnchangedCount = 3 }, null, "2FAS", "export.2fas", key => catalog.Get(key, CultureInfo.GetCultureInfo("en")));
        var page = new ImportReviewMobilePage { DataContext = model };
        var window = new Window { Width = 430, Height = 1400, Content = page,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        try
        {
            window.Show();
            window.UpdateLayout();
            var cards = page.GetVisualDescendants().OfType<ImportReviewAccountCard>().ToArray();
            Assert.Equal(3, cards.Length);
            var headers = cards.Select(card => card.GetVisualDescendants().OfType<ToggleButton>().Single(button => button.Name == "ExpanderHeader")).ToArray();
            Assert.All(headers, header => Assert.True(header.Bounds.Width > 300));
            Assert.All(headers, header => Assert.InRange(Math.Abs(header.Bounds.Width - headers[0].Bounds.Width), 0, 1));
            Assert.DoesNotContain(page.GetVisualDescendants(), control => control is ListBoxItem);
            var card = cards[0];
            var container = card.FindAncestorOfType<ContentPresenter>()!;
            var background = container.Background;
            var location = card.TranslatePoint(new Point(4, 4), window)!.Value;
            using var finger = new Pointer(Pointer.GetNextFreeId(), PointerType.Touch, true);
            card.RaiseEvent(new PointerPressedEventArgs(card, finger, window, location, 1,
                new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed), KeyModifiers.None));
            window.UpdateLayout();
            Assert.Same(background, container.Background);
            Assert.True(container.Background is null || container.Background is ISolidColorBrush { Color.A: 0 });
            card.RaiseEvent(new PointerReleasedEventArgs(card, finger, window, location, 2,
                new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased), KeyModifiers.None, MouseButton.Left));
            finger.Capture(null);
            var header = headers[0];
            var headerBorder = header.GetVisualDescendants().OfType<Border>().Single(border => border.Name == "ToggleButtonBackground");
            var headerColor = Assert.IsAssignableFrom<ISolidColorBrush>(headerBorder.Background).Color;
            location = header.TranslatePoint(new Point(10, 10), window)!.Value;
            header.RaiseEvent(new PointerPressedEventArgs(header, finger, window, location, 3,
                new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed), KeyModifiers.None));
            window.UpdateLayout();
            Assert.True(header.IsPressed);
            Assert.Equal(headerColor, Assert.IsAssignableFrom<ISolidColorBrush>(headerBorder.Background).Color);
            header.RaiseEvent(new PointerReleasedEventArgs(header, finger, window, location, 4,
                new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased), KeyModifiers.None, MouseButton.Left));
            finger.Capture(null);
            Assert.False(header.IsPressed);
            window.MouseMove(location);
            window.MouseDown(location, MouseButton.Left);
            window.MouseUp(location, MouseButton.Left);
            window.UpdateLayout();
            Assert.True(model.Rows[0].IsExpanded);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(360, false)]
    [InlineData(430, true)]
    public void MobilePage_LongMixedHeightList_ScrollsIncrementallyWithBoundedRealizationAndKeepsDecisions(int width, bool dark)
    {
        var catalog = new AvaloniaStringCatalog();
        var entries = Enumerable.Range(0, 300).Select(index =>
            new AccountImportEntryPreview($"Issuer {index}", index % 2 == 0 ? "account@example.test" : "an-account-with-a-long-name@example.test", "SHA1", 6, 30)
            { ImportIndex = index, Existing = index % 3 == 0 ? new($"Issuer {index}", "account@example.test", "SHA1", 6, 30) : null }).ToArray();
        var model = new ImportReviewViewModel(new(300, 100, ImportConflictStrategy.SkipExisting)
        {
            Entries = entries,
            ChangedConflicts = [new(0, "Issuer 0", "account@example.test", "Issuer 0", "account@example.test", false, false, true, true)]
        }, null, "Aegis", "export.json", key => catalog.Get(key, CultureInfo.GetCultureInfo("en")));
        model.Rows[0].IsKeepBoth = true;
        foreach (var row in model.Rows.Where(row => row.ImportIndex % 11 == 0)) row.IsExpanded = true;
        var collectionChanges = 0;
        model.MobileItems.CollectionChanged += (_, _) => collectionChanges++;
        var page = new ImportReviewMobilePage { DataContext = model };
        var window = new Window { Width = width, Height = 760, Content = page,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        try
        {
            window.Show();
            window.UpdateLayout();
            var scroll = page.FindControl<ScrollViewer>("ReviewPageScroll")!;
            var items = page.FindControl<ItemsControl>("ReviewItems")!;
            Assert.True(Assert.IsType<VirtualizingStackPanel>(items.ItemsPanelRoot).CacheLength > 0);
            foreach (var target in Enumerable.Range(0, 126).Select(step => step * 24)
                         .Concat(Enumerable.Range(0, 126).Reverse().Select(step => step * 24)))
            {
                var anchor = page.GetVisualDescendants().OfType<ImportReviewAccountCard>().FirstOrDefault(card =>
                    card.IsEffectivelyVisible && card.TranslatePoint(new Point(0, card.Bounds.Height / 2), scroll) is { Y: >= 24 } point
                    && point.Y < scroll.Viewport.Height - 24);
                var anchorRow = anchor?.DataContext;
                var before = anchor?.TranslatePoint(default, scroll);
                var delta = target - scroll.Offset.Y;
                scroll.Offset = new Vector(0, target);
                window.UpdateLayout();
                Assert.InRange(Math.Abs(scroll.Offset.Y - target), 0, 1);
                if (anchor is not null && before is { } position)
                {
                    Assert.True(anchor.IsEffectivelyVisible);
                    Assert.Same(anchorRow, anchor.DataContext);
                    var after = anchor.TranslatePoint(default, scroll)!.Value;
                    Assert.InRange(Math.Abs(after.Y - (position.Y - delta)), 0, 1);
                }
                var realizedCards = page.GetVisualDescendants().OfType<ImportReviewAccountCard>().Count(card => card.IsEffectivelyVisible);
                Assert.InRange(realizedCards, 1, 35);
            }
            Assert.Equal(0, collectionChanges);
            Assert.True(model.Rows[0].IsKeepBoth);
            Assert.True(model.Rows[0].IsExpanded);
            Assert.False(model.Rows[1].IsExpanded);
            Assert.DoesNotContain(page.GetVisualDescendants(), control => control is ListBoxItem);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(360, 640, false, "en")]
    [InlineData(430, 760, true, "de")]
    [InlineData(360, 480, false, "fr")]
    public void MobilePage_ScrollsSummaryWithAccountsAndKeepsActionsAndSearchStable(int width, int height, bool dark, string language)
    {
        var catalog = new AvaloniaStringCatalog();
        var model = new ImportReviewViewModel(new(2, 1, ImportConflictStrategy.SkipExisting)
        {
            NewCount = 1,
            Entries = [new("New", "alice", "SHA1", 6, 30) { ImportIndex = 0 },
                new("Conflict", "bob", "SHA512", 8, 60) { ImportIndex = 1, Existing = new("Conflict", "bob", "SHA1", 6, 30) }],
            ChangedConflicts = [new(1, "Conflict", "bob", "Conflict", "bob", false, false, true, true)]
        }, null, "Bitwarden", "external.json", key => catalog.Get(key, CultureInfo.GetCultureInfo(language)));
        var page = new ImportReviewMobilePage { DataContext = model };
        var window = new Window { Width = width, Height = height, Content = page, RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        try
        {
            window.Show();
            window.UpdateLayout();
            var scroll = page.FindControl<ScrollViewer>("ReviewPageScroll")!;
            Assert.True(scroll.Bounds.Height >= height - 150);
            var search = page.GetVisualDescendants().OfType<TextBox>().Single(box => box.PlaceholderText == model.SearchText);
            search.Text = "bob";
            window.UpdateLayout();
            Assert.Single(model.VisibleRows);
            Assert.Same(search, page.GetVisualDescendants().OfType<TextBox>().Single(box => box.PlaceholderText == model.SearchText));
            scroll.Offset = new Vector(0, 200);
            window.UpdateLayout();
            Assert.True(scroll.Offset.Y >= 100);
            foreach (var command in new[] { model.BackCommand, model.ImportCommand })
            {
                var button = page.GetVisualDescendants().OfType<Button>().Single(button => ReferenceEquals(button.Command, command));
                var position = button.TranslatePoint(default, page)!.Value;
                Assert.True(button.Bounds.Height >= 44);
                Assert.True(position.Y >= 0 && position.Y + button.Bounds.Height <= height + 1);
                Assert.True(position.X >= 0 && position.X + button.Bounds.Width <= width + 1);
            }
            Assert.False(model.CanImport);
            model.Rows[1].IsReplace = true;
            Assert.True(model.CanImport);
            Assert.Equal(2, model.ImportCount);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(360, 740, false, "de")]
    [InlineData(430, 700, true, "fr")]
    [InlineData(760, 580, true, "es")]
    [InlineData(1240, 760, false, "en")]
    [InlineData(360, 540, true, "de")]
    [InlineData(1240, 480, true, "en")]
    public void Review_ReflowsToCardsAndKeepsImportAndBackVisible(int width, int height, bool dark, string language)
    {
        var catalog = new AvaloniaStringCatalog();
        var model = new ImportReviewViewModel(new(1, 1, ImportConflictStrategy.SkipExisting)
        {
            Entries = [new("An issuer with a long name", "alice@example.test", "SHA512", 8, 60)
                { ImportIndex = 0, Existing = new("An issuer with a long name", "alice@example.test", "SHA1", 6, 30) }],
            ChangedConflicts = [new(0, "Issuer", "alice", "Issuer", "alice", false, false, true, true)]
        }, null, "Bitwarden Authenticator", "external-export.json", key => catalog.Get(key, CultureInfo.GetCultureInfo(language)));
        var panel = new ImportReviewPanel { DataContext = model };
        var window = new Window { Width = width, Height = height, Content = panel,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        try
        {
            window.Show();
            window.UpdateLayout();
            var table = panel.GetVisualDescendants().OfType<ListBox>().Single(list => list.Classes.Contains("review-table"));
            var cards = panel.GetVisualDescendants().OfType<ListBox>().Single(list => list.Classes.Contains("review-cards"));
            Assert.Equal(width >= 900, table.IsVisible);
            Assert.Equal(width < 900, cards.IsVisible);
            Assert.True((width < 900 ? cards : table).Bounds.Height >= 80);
            foreach (var command in new[] { model.ImportCommand, model.BackCommand })
            {
                var button = panel.GetVisualDescendants().OfType<Button>().First(button => ReferenceEquals(button.Command, command) && button.IsVisible);
                var bounds = button.Bounds;
                Assert.True(bounds.Height >= 44);
                var point = button.TranslatePoint(new Point(0, 0), panel)!.Value;
                Assert.True(point.X >= 0 && point.X + bounds.Width <= width + 1);
                Assert.True(point.Y >= 0 && point.Y + bounds.Height <= height + 1);
            }
            Assert.DoesNotContain(panel.GetVisualDescendants().OfType<ScrollViewer>(), scroll =>
                scroll.FindAncestorOfType<ListBox>() is not null && scroll.Extent.Width > scroll.Viewport.Width + 1 && scroll.IsEffectivelyVisible);
            Assert.False(model.CanImport);
            model.Rows[0].IsKeepBoth = true;
            window.UpdateLayout();
            Assert.True(model.CanImport);
        }
        finally { window.Close(); }
    }
}
