using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaPath = Avalonia.Controls.Shapes.Path;
using System.Windows.Input;
using FluentResults;
using TOTP.Avalonia.Desktop;
using TOTP.Avalonia.Shared.Controls;
using TOTP.Avalonia.Shared.Styles;
using TOTP.Avalonia.Desktop.Dialogs;
using TOTP.Avalonia.Desktop.Localization;
using TOTP.Avalonia.Desktop.Controls;
using TOTP.Avalonia.Desktop.Presentation;
using TOTP.Core.Models;
using TOTP.Core.Security.Interfaces;
using TOTP.Core.Services.Interfaces;
using TOTP.Core.Validation;

namespace TOTP.Tests.Avalonia.Headless;

public sealed class MainWindowSmokeTests
{
    [AvaloniaFact]
    public void PeriodClearButton_IsCenteredBeforeSpinnerWithoutOverlap()
    {
        var clear = new Button { Content = "×" };
        clear.Classes.Add("numeric-clear");
        var input = new NumericUpDown
        {
            Width = 320,
            Value = 30,
            InnerRightContent = clear
        };
        var window = new Window { Content = input };

        try
        {
            window.Show();
            input.ApplyTemplate();
            window.UpdateLayout();

            var spinner = Assert.Single(
                input.GetVisualDescendants().OfType<StackPanel>(),
                panel => panel.Name == "PART_SpinnerPanel");
            var clearRight = clear.TranslatePoint(
                new Point(clear.Bounds.Width, clear.Bounds.Height / 2),
                input);
            var spinnerLeft = spinner.TranslatePoint(
                new Point(0, spinner.Bounds.Height / 2),
                input);

            Assert.NotNull(clearRight);
            Assert.NotNull(spinnerLeft);
            Assert.True(clearRight.Value.X <= spinnerLeft.Value.X);
            Assert.Equal(input.Bounds.Height / 2, clearRight.Value.Y, 3);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void RevealableSecretInput_FocusInputFocusesPasswordTextBox()
    {
        var input = new RevealableSecretInput();
        var window = new Window { Content = input };

        try
        {
            window.Show();
            window.UpdateLayout();

            input.FocusInput();

            Assert.True(Assert.Single(input.GetVisualDescendants().OfType<TextBox>()).IsFocused);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task PasswordDialog_AutomaticallyFocusesPrimaryPasswordInput()
    {
        var window = new PasswordDialogWindow();

        try
        {
            window.Show();
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Input);

            var passwordInputs = window.GetVisualDescendants()
                .OfType<RevealableSecretInput>()
                .ToArray();
            Assert.Equal(2, passwordInputs.Length);
            Assert.True(Assert.Single(
                passwordInputs[0].GetVisualDescendants().OfType<TextBox>()).IsFocused);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void RevealableSecretInput_ClickTogglesPersistentDisclosure()
    {
        var input = new RevealableSecretInput { Text = "test-secret", Width = 320 };
        var window = new Window { Content = input };

        try
        {
            window.Show();
            window.UpdateLayout();
            var textBox = Assert.Single(input.GetVisualDescendants().OfType<TextBox>());
            var revealButton = Assert.Single(input.GetVisualDescendants().OfType<Button>());
            var revealIcon = Assert.Single(revealButton.GetVisualDescendants().OfType<SymbolIcon>());

            Assert.Equal(SymbolIconKind.Reveal, revealIcon.Kind);
            Assert.Equal(3, Assert.IsType<TranslateTransform>(revealIcon.RenderTransform).Y);
            Assert.Equal(44, revealButton.Bounds.Width);
            Assert.Equal(textBox.Bounds.Right, revealButton.Bounds.Right);
            Assert.Equal(default, revealButton.BorderThickness);
            Assert.Equal(VerticalAlignment.Center, revealButton.VerticalContentAlignment);

            var inputCenter = input.Bounds.Height / 2;
            var buttonCenter = revealButton.TranslatePoint(
                new Point(revealButton.Bounds.Width / 2, revealButton.Bounds.Height / 2),
                input);
            Assert.NotNull(buttonCenter);
            Assert.Equal(inputCenter, buttonCenter.Value.Y, 3);

            revealButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.UpdateLayout();

            Assert.True(input.IsRevealed);
            Assert.Equal('\0', textBox.PasswordChar);
            Assert.True(textBox.IsFocused);
            Assert.Equal(SymbolIconKind.Conceal, revealIcon.Kind);

            revealButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.False(input.IsRevealed);
            Assert.NotEqual('\0', textBox.PasswordChar);
            Assert.Equal(SymbolIconKind.Reveal, revealIcon.Kind);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void BrandIconComboBox_QuickTypedPrefixSelectsMatchingEntry()
    {
        var options = new[]
        {
            new BrandIconOption(null, "Automatic"),
            new BrandIconOption("paypal", "PayPal"),
            new BrandIconOption("pr-co", "pr.co"),
            new BrandIconOption("proton", "Proton")
        };
        var comboBox = new TypeAheadComboBox
        {
            Width = 240,
            DisplayMemberBinding = new Binding(nameof(BrandIconOption.DisplayName)),
            ItemsSource = options,
            SelectedIndex = 0
        };
        var window = new Window { Content = comboBox };

        try
        {
            window.Show();
            window.UpdateLayout();
            comboBox.Focus();
            comboBox.IsDropDownOpen = true;
            window.UpdateLayout();

            Assert.True(comboBox.Bounds.Height > 0);

            window.KeyPress(Key.P, RawInputModifiers.None, PhysicalKey.P, "p");
            Assert.Same(options[1], comboBox.SelectedItem);

            window.KeyPress(Key.R, RawInputModifiers.None, PhysicalKey.R, "r");
            Assert.Same(options[2], comboBox.SelectedItem);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void TextBoxStyles_KeepSingleLineTextCenteredAndMultilineTextTopAligned()
    {
        var singleLine = new TextBox { Text = "Letters with descenders: gypq" };
        var multiline = new TextBox
        {
            AcceptsReturn = true,
            MinHeight = 100,
            Text = "First line\nSecond line"
        };
        var window = new Window
        {
            Content = new StackPanel { Children = { singleLine, multiline } }
        };

        try
        {
            window.Show();
            window.UpdateLayout();

            Assert.Equal(new Thickness(12, 6), singleLine.Padding);
            Assert.Equal(
                global::Avalonia.Layout.VerticalAlignment.Center,
                singleLine.VerticalContentAlignment);
            Assert.True(singleLine.Bounds.Height >= singleLine.MinHeight);
            Assert.Equal(
                global::Avalonia.Layout.VerticalAlignment.Top,
                multiline.VerticalContentAlignment);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void PasswordSetup_EnterFromEitherSecretInputInvokesDefaultAction()
    {
        var executionCount = 0;
        var inputs = new[] { new RevealableSecretInput(), new RevealableSecretInput() };
        var submit = new Button
        {
            IsDefault = true,
            Command = new TestCommand(() => executionCount++)
        };
        var window = new Window
        {
            Content = new StackPanel { Children = { inputs[0], inputs[1], submit } }
        };

        try
        {
            window.Show();
            window.UpdateLayout();

            foreach (var input in inputs)
            {
                input.ApplyTemplate();
                var textBox = Assert.Single(input.GetVisualDescendants().OfType<TextBox>());
                textBox.Focus();
                window.KeyPress(
                    Key.Enter,
                    RawInputModifiers.None,
                    PhysicalKey.Enter,
                    null);
            }

            Assert.Equal(2, executionCount);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void AccountList_RightClickOpensContextWithoutChangingSelection()
    {
        var first = new AccountListItemViewModel(Guid.NewGuid(), "First", "selected");
        var second = new AccountListItemViewModel(Guid.NewGuid(), "Second", "context");
        var menu = new ContextMenu { ItemsSource = new[] { new MenuItem { Header = "Edit" } } };
        var list = new ContextPreservingAccountListBox
        {
            Width = 200,
            Height = 100,
            ItemsSource = new[] { first, second },
            SelectedItem = first,
            ContextMenu = menu
        };
        var window = new Window { Width = 240, Height = 140, Content = list };

        try
        {
            window.Show();
            list.ApplyTemplate();
            window.UpdateLayout();
            var secondContainer = Assert.Single(
                list.GetVisualDescendants().OfType<ListBoxItem>(),
                item => ReferenceEquals(item.DataContext, second));
            var clickPoint = secondContainer.TranslatePoint(new Point(8, 8), window);
            Assert.NotNull(clickPoint);

            window.MouseDown(clickPoint.Value, MouseButton.Right, RawInputModifiers.None);
            window.MouseUp(clickPoint.Value, MouseButton.Right, RawInputModifiers.None);

            Assert.Same(first, list.SelectedItem);
            Assert.Same(second, list.ContextAccount);
            Assert.True(menu.IsOpen);
            menu.Close();
            Assert.Null(list.ContextAccount);
        }
        finally
        {
            menu.Close();
            window.Close();
        }
    }

    [AvaloniaFact]
    public void AccountContextMenu_UsesProductSurfaceAndDangerTreatment()
    {
        var edit = new MenuItem
        {
            Header = "Edit",
            Icon = new SymbolIcon { Kind = SymbolIconKind.Edit, IconSize = 15 }
        };
        var showQr = new MenuItem
        {
            Header = "Show QR code",
            Icon = new SymbolIcon { Kind = SymbolIconKind.QrCode, IconSize = 15 }
        };
        var delete = new MenuItem
        {
            Header = "Delete",
            Icon = new SymbolIcon { Kind = SymbolIconKind.Delete, IconSize = 15 }
        };
        delete.Classes.Add("danger-context-item");
        var menu = new ContextMenu
        {
            ItemsSource = new Control[] { edit, showQr, new Separator(), delete }
        };
        menu.Classes.Add("account-context-menu");
        var host = new Button { Width = 120, Height = 40, ContextMenu = menu };
        var window = new Window { Content = host };

        try
        {
            window.Show();
            menu.Open(host);
            window.UpdateLayout();

            Assert.Equal(0, menu.MinWidth);
            Assert.True(double.IsNaN(menu.Width));
            Assert.Equal(new Thickness(6), menu.Padding);
            Assert.Equal(12, menu.FontSize);
            Assert.Equal(12, edit.FontSize);
            Assert.Equal(new CornerRadius(6), menu.CornerRadius);
            Assert.Equal(new Thickness(1), menu.BorderThickness);
            Assert.Equal(34, edit.MinHeight);
            Assert.Equal(new Thickness(10, 6), edit.Padding);
            var normalForeground = Assert.IsType<SolidColorBrush>(edit.Foreground);
            var dangerForeground = Assert.IsType<SolidColorBrush>(delete.Foreground);
            Assert.NotEqual(normalForeground.Color, dangerForeground.Color);
            Assert.Collection(
                new[] { edit, showQr, delete },
                item => Assert.Equal(SymbolIconKind.Edit, Assert.IsType<SymbolIcon>(item.Icon).Kind),
                item => Assert.Equal(SymbolIconKind.QrCode, Assert.IsType<SymbolIcon>(item.Icon).Kind),
                item => Assert.Equal(SymbolIconKind.Delete, Assert.IsType<SymbolIcon>(item.Icon).Kind));
        }
        finally
        {
            menu.Close();
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task AccountList_ProgrammaticSelectionScrollsImportedRowIntoView()
    {
        var accounts = Enumerable.Range(0, 50)
            .Select(index => new AccountListItemViewModel(
                Guid.NewGuid(),
                $"Issuer {index:00}",
                $"account-{index:00}"))
            .ToArray();
        var list = new ContextPreservingAccountListBox
        {
            Width = 220,
            Height = 120,
            ItemsSource = accounts
        };
        var window = new Window { Width = 260, Height = 160, Content = list };

        try
        {
            window.Show();
            list.ApplyTemplate();
            window.UpdateLayout();

            list.SelectedItem = accounts[^1];
            await Dispatcher.UIThread.InvokeAsync(
                static () => { },
                DispatcherPriority.Loaded);
            window.UpdateLayout();

            Assert.Contains(
                list.GetVisualDescendants().OfType<ListBoxItem>(),
                item => ReferenceEquals(item.DataContext, accounts[^1]));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void AccountRowHighlightContainer_PadsContentInsideHighlight()
    {
        var text = new TextBlock { Text = "new account" };
        var highlight = new Border { Child = text };
        highlight.Classes.Add("account-row-container");
        highlight.Classes.Add("recently-added");
        var window = new Window { Content = highlight };

        try
        {
            window.Show();
            window.UpdateLayout();

            Assert.Equal(new Thickness(8, 5), highlight.Padding);
            Assert.Equal(new Thickness(0), highlight.BorderThickness);
            Assert.True(text.Bounds.X >= 8);
            Assert.True(text.Bounds.Y >= 5);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void AccountRow_RendersAccountNameBelowIssuerWithAccessibleContext()
    {
        var application = Assert.IsType<App>(Application.Current);
        application.RequestedThemeVariant = ThemeVariant.Light;
        var row = new AccountRow { Issuer = "Issuer", AccountName = "account@example.test" };
        var window = new Window { Content = row };

        try
        {
            window.Show();
            row.ApplyTemplate();
            window.UpdateLayout();

            var text = row.GetVisualDescendants().OfType<TextBlock>().ToArray();
            Assert.Equal(2, text.Length);
            Assert.Equal("Issuer", text[0].Text);
            Assert.Equal("account@example.test", text[1].Text);
            Assert.Equal(11, text[1].FontSize);
            Assert.Equal(
                Color.Parse("#7E7E84"),
                Assert.IsType<SolidColorBrush>(text[1].Foreground).Color);
            Assert.Equal("Issuer, account@example.test", row.AccessibleName);
        }
        finally
        {
            window.Close();
            application.RequestedThemeVariant = ThemeVariant.Dark;
        }
    }

    [AvaloniaFact]
    public void DesktopAccountList_SelectedRowUsesSubtleSurfaceWithoutTextRecoloring()
    {
        var item = new Border
        {
            Child = new AccountRow { Issuer = "Issuer", AccountName = "account" }
        };
        item.Classes.Add("account-row-container");
        var list = new ListBox
        {
            ItemsSource = new[] { item },
            SelectedItem = item
        };
        list.Classes.Add("accounts");
        list.Classes.Add("desktop-accounts");
        var window = new Window { Content = list };

        try
        {
            window.Show();
            window.UpdateLayout();

            var container = Assert.Single(
                list.GetVisualDescendants().OfType<ListBoxItem>());
            var presenter = Assert.Single(
                container.GetVisualDescendants().OfType<ContentPresenter>(),
                candidate => candidate.Name == "PART_ContentPresenter");
            Assert.Equal(
                Colors.Transparent,
                Assert.IsAssignableFrom<ISolidColorBrush>(presenter.Background).Color);
            Assert.Equal(
                Color.Parse("#1A2E4E"),
                Assert.IsType<SolidColorBrush>(item.Background).Color);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task AccountEditorFlyout_MatchesWpfFullWidthCollapsedSlideLifecycle()
    {
        var transform = new TranslateTransform { X = 380 };
        var flyout = new Border
        {
            Width = 380,
            Height = 300,
            IsVisible = false,
            RenderTransform = transform
        };
        flyout.Classes.Add("flyout");
        var window = new Window { Content = flyout };

        try
        {
            window.Show();
            Assert.False(flyout.IsVisible);
            Assert.Equal(default, flyout.BorderThickness);
            Assert.Equal(380, transform.X, precision: 2);

            flyout.IsVisible = true;
            window.UpdateLayout();
            Assert.Equal(380, transform.X, precision: 2);
            flyout.Classes.Add("open");
            await WaitUntilAsync(() => Math.Abs(transform.X) < 0.005);

            Assert.Equal(0, transform.X, precision: 2);

            flyout.IsVisible = false;
            flyout.Classes.Remove("open");
            transform.X = 380;
            Assert.False(flyout.IsVisible);
            Assert.Equal(380, transform.X, precision: 2);

            flyout.IsVisible = true;
            window.UpdateLayout();
            Assert.Equal(380, transform.X, precision: 2);
            flyout.Classes.Add("open");
            await WaitUntilAsync(() => Math.Abs(transform.X) < 0.005);
            Assert.Equal(0, transform.X, precision: 2);
        }
        finally
        {
            window.Close();
        }
    }

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    public void AccountPageHeightFit_DoesNotResizeAnOpenEditor(
        bool isAccountListVisible,
        bool isEditorVisible,
        bool expected)
    {
        var policy = typeof(MainWindow).GetMethod(
            "ShouldFitAccountPage",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        Assert.NotNull(policy);
        Assert.Equal(expected, policy.Invoke(null, [isAccountListVisible, isEditorVisible]));
    }

    [Fact]
    public void AccountPageHeightFit_UsesCompactWindowMinimum()
    {
        var policy = typeof(MainWindow).GetMethod(
            "GetDesiredMinimumHeight",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        Assert.NotNull(policy);
        Assert.Equal(200, Assert.IsType<double>(policy.Invoke(null, null)));
    }

    [AvaloniaFact]
    public void ContentHeightFit_PreservesWindowTopEdge()
    {
        var window = new MainWindow();

        try
        {
            window.Show();
            window.Position = new PixelPoint(120, 130);
            var resize = typeof(MainWindow).GetMethod(
                "SetHeightImmediately",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            Assert.NotNull(resize);
            resize.Invoke(window, [250d]);

            Assert.Equal(new PixelPoint(120, 130), window.Position);
            Assert.Equal(250, window.Height);
        }
        finally
        {
            window.Close();
        }
    }

    [Theory]
    [InlineData(70, 350, 32, 200, 540, 454)]
    [InlineData(70, 600, 32, 200, 540, 540)]
    public void AccountEditorHeight_FitsContentAndHonorsScreenCap(
        double chromeHeight,
        double contentHeight,
        double verticalPadding,
        double minimumHeight,
        double maximumHeight,
        double expectedHeight)
    {
        var policy = typeof(MainWindow).GetMethod(
            "CalculateAccountEditorWindowHeight",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        Assert.NotNull(policy);
        Assert.Equal(
            expectedHeight,
            Assert.IsType<double>(policy.Invoke(
                null,
                [chromeHeight, contentHeight, verticalPadding, minimumHeight, maximumHeight])),
            precision: 2);
    }

    [Theory]
    [InlineData(0, 42, 0, 0)]
    [InlineData(2, 42, 0, 44)]
    [InlineData(2, 42, 60, 60)]
    [InlineData(2, 42, 500, 86)]
    public void AccountListHeight_PopulatedListNeverCollapsesToZero(
        int accountCount,
        double rowHeight,
        double availableHeight,
        double expectedHeight)
    {
        var policy = typeof(MainWindow).GetMethod(
            "CalculateAccountListHeight",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        Assert.NotNull(policy);
        Assert.Equal(
            expectedHeight,
            Assert.IsType<double>(policy.Invoke(
                null,
                [accountCount, rowHeight, availableHeight])),
            precision: 2);
    }

    [Theory]
    [InlineData(500, 300, 200)]
    [InlineData(80, 120, 0)]
    public void AccountPageFixedHeight_ExcludesTheEntireOverlayListRegion(
        double contentHeight,
        double listRegionHeight,
        double expectedHeight)
    {
        var policy = typeof(MainWindow).GetMethod(
            "CalculateFixedAccountPageHeight",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        Assert.NotNull(policy);
        Assert.Equal(
            expectedHeight,
            Assert.IsType<double>(policy.Invoke(
                null,
                [contentHeight, listRegionHeight])),
            precision: 2);
    }

    [AvaloniaFact]
    public void DesktopAccountRow_UsesRequestedLightThemeBackground()
    {
        var application = Assert.IsType<App>(Application.Current);
        var previousTheme = application.RequestedThemeVariant;
        application.RequestedThemeVariant = ThemeVariant.Light;
        var row = new Border { Child = new TextBlock { Text = "account" } };
        row.Classes.Add("account-row-container");
        var item = new ListBoxItem { Content = row };
        var list = new ListBox { ItemsSource = new[] { item } };
        list.Classes.Add("accounts");
        list.Classes.Add("desktop-accounts");
        var window = new Window { Content = list };

        try
        {
            window.Show();
            window.UpdateLayout();

            Assert.Equal(
                Color.Parse("#F4F6FB"),
                Assert.IsType<SolidColorBrush>(row.Background).Color);
        }
        finally
        {
            window.Close();
            application.RequestedThemeVariant = previousTheme;
        }
    }

    [AvaloniaFact]
    public void MobileAccountList_SelectedRowKeepsPrimaryTextColor()
    {
        var application = Assert.IsType<App>(Application.Current);
        application.RequestedThemeVariant = ThemeVariant.Light;
        var item = new TextBlock { Text = "Issuer" };
        var list = new ListBox
        {
            ItemsSource = new[] { item },
            SelectedItem = item
        };
        list.Classes.Add("accounts");
        list.Classes.Add("mobile-accounts");
        var window = new Window { Content = list };

        try
        {
            window.Show();
            window.UpdateLayout();

            var container = Assert.Single(
                list.GetVisualDescendants().OfType<ListBoxItem>());
            var presenter = Assert.Single(
                container.GetVisualDescendants().OfType<ContentPresenter>(),
                candidate => candidate.Name == "PART_ContentPresenter");
            Assert.Equal(
                Color.Parse("#172033"),
                Assert.IsAssignableFrom<ISolidColorBrush>(presenter.Foreground).Color);
        }
        finally
        {
            window.Close();
            application.RequestedThemeVariant = ThemeVariant.Dark;
        }
    }

    [AvaloniaFact]
    public void AccountPageHeightRefit_RestoresTheAccountListScrollOffset()
    {
        var content = new Border { Width = 200, Height = 2000 };
        var scrollViewer = new ScrollViewer
        {
            Width = 220,
            Height = 120,
            Content = content
        };
        var window = new Window { Width = 260, Height = 160, Content = scrollViewer };

        try
        {
            window.Show();
            window.UpdateLayout();
            scrollViewer.Offset = new Vector(0, 900);
            window.UpdateLayout();
            var offsetBeforeRefit = scrollViewer.Offset;
            Assert.True(offsetBeforeRefit.Y > 0);

            scrollViewer.Offset = default;
            var restore = typeof(MainWindow).GetMethod(
                "RestoreAccountListScrollOffset",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

            Assert.NotNull(restore);
            restore.Invoke(null, [scrollViewer, offsetBeforeRefit]);

            Assert.Equal(offsetBeforeRefit.X, scrollViewer.Offset.X, precision: 2);
            Assert.Equal(offsetBeforeRefit.Y, scrollViewer.Offset.Y, precision: 2);
        }
        finally
        {
            window.Close();
        }
    }

    [Theory]
    [InlineData(Key.Delete, KeyModifiers.None, true, false, true)]
    [InlineData(Key.D, KeyModifiers.Control, true, false, true)]
    [InlineData(Key.D, KeyModifiers.Control, true, true, false)]
    [InlineData(Key.D, KeyModifiers.Control | KeyModifiers.Shift, true, false, false)]
    [InlineData(Key.Delete, KeyModifiers.None, true, true, false)]
    [InlineData(Key.Delete, KeyModifiers.None, false, false, false)]
    [InlineData(Key.Delete, KeyModifiers.Control, true, false, false)]
    [InlineData(Key.Back, KeyModifiers.None, true, false, false)]
    public void AccountDeleteShortcut_RequiresPlainDeleteOutsideTextEditing(
        Key key,
        KeyModifiers modifiers,
        bool canDelete,
        bool isTextEditing,
        bool expected)
    {
        var policy = typeof(MainWindow).GetMethod(
            "ShouldHandleAccountDeleteKey",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        Assert.NotNull(policy);
        Assert.Equal(expected, policy.Invoke(null, [key, modifiers, canDelete, isTextEditing]));
    }

    [Theory]
    [InlineData(Key.A, Key.A, KeyModifiers.Control, true, false, true)]
    [InlineData(Key.E, Key.E, KeyModifiers.Control, true, false, true)]
    [InlineData(Key.A, Key.A, KeyModifiers.Control, true, true, false)]
    [InlineData(Key.E, Key.E, KeyModifiers.Control, false, false, false)]
    [InlineData(Key.A, Key.E, KeyModifiers.Control, true, false, false)]
    [InlineData(Key.A, Key.A, KeyModifiers.Control | KeyModifiers.Shift, true, false, false)]
    public void AccountCommandShortcuts_RequireExactControlKeyOutsideTextEditors(
        Key key,
        Key expectedKey,
        KeyModifiers modifiers,
        bool canExecute,
        bool isTextEditing,
        bool expected)
    {
        var policy = typeof(MainWindow).GetMethod(
            "ShouldHandleAccountCommandShortcut",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        Assert.NotNull(policy);
        Assert.Equal(
            expected,
            policy.Invoke(null, [key, expectedKey, modifiers, canExecute, isTextEditing]));
    }

    [Theory]
    [InlineData(Key.L, KeyModifiers.Control, true, true)]
    [InlineData(Key.L, KeyModifiers.Control, false, false)]
    [InlineData(Key.L, KeyModifiers.Control | KeyModifiers.Shift, true, false)]
    [InlineData(Key.K, KeyModifiers.Control, true, false)]
    public void LockShortcut_RequiresExactControlLAndAvailableLock(
        Key key,
        KeyModifiers modifiers,
        bool canLock,
        bool expected)
    {
        var policy = typeof(MainWindow).GetMethod(
            "ShouldHandleLockShortcut",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        Assert.NotNull(policy);
        Assert.Equal(expected, policy.Invoke(null, [key, modifiers, canLock]));
    }

    [Theory]
    [InlineData(Key.C, KeyModifiers.Control, true, false, true)]
    [InlineData(Key.C, KeyModifiers.Control, true, true, false)]
    [InlineData(Key.C, KeyModifiers.Control, false, false, false)]
    [InlineData(Key.C, KeyModifiers.Control | KeyModifiers.Shift, true, false, false)]
    [InlineData(Key.C, KeyModifiers.None, true, false, false)]
    [InlineData(Key.V, KeyModifiers.Control, true, false, false)]
    public void AccountCopyShortcut_RequiresControlCWithoutSelectedText(
        Key key,
        KeyModifiers modifiers,
        bool canCopy,
        bool preserveTextCopy,
        bool expected)
    {
        var policy = typeof(MainWindow).GetMethod(
            "ShouldHandleAccountCopyKey",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        Assert.NotNull(policy);
        Assert.Equal(expected, policy.Invoke(null, [key, modifiers, canCopy, preserveTextCopy]));
    }

    [Theory]
    [InlineData(Key.Down, KeyModifiers.None, true, true, true)]
    [InlineData(Key.Down, KeyModifiers.None, false, true, false)]
    [InlineData(Key.Down, KeyModifiers.None, true, false, false)]
    [InlineData(Key.Down, KeyModifiers.Control, true, true, false)]
    [InlineData(Key.Up, KeyModifiers.None, true, true, false)]
    public void SearchDownArrow_MovesFocusOnlyToAnAvailableAccountList(
        Key key,
        KeyModifiers modifiers,
        bool canNavigate,
        bool isSearchSource,
        bool expected)
    {
        var policy = typeof(MainWindow).GetMethod(
            "ShouldMoveFocusFromSearch",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        Assert.NotNull(policy);
        Assert.Equal(expected, policy.Invoke(null, [key, modifiers, canNavigate, isSearchSource]));
    }

    [AvaloniaFact]
    public async Task SearchDownArrow_FocusesTheSelectedAccountContainer()
    {
        var account = new AccountListItemViewModel(Guid.NewGuid(), "Issuer", "account");
        var list = new ContextPreservingAccountListBox
        {
            Width = 220,
            Height = 120,
            ItemsSource = new[] { account },
            SelectedItem = account
        };
        var search = new TextBox();
        var window = new Window
        {
            Width = 260,
            Height = 200,
            Content = new StackPanel { Children = { search, list } }
        };

        try
        {
            window.Show();
            window.UpdateLayout();
            search.Focus();
            list.FocusAccount(account);
            await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Loaded);

            var container = Assert.Single(
                list.GetVisualDescendants().OfType<ListBoxItem>(),
                item => ReferenceEquals(item.DataContext, account));
            Assert.True(container.IsFocused);
            Assert.False(search.IsFocused);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void AccountList_ArrowNavigationMovesSelectionAndKeyboardFocus()
    {
        var first = new AccountListItemViewModel(Guid.NewGuid(), "First", "account");
        var second = new AccountListItemViewModel(Guid.NewGuid(), "Second", "account");
        var list = new ContextPreservingAccountListBox
        {
            Width = 220,
            Height = 120,
            ItemsSource = new[] { first, second },
            SelectedItem = first
        };
        var window = new Window { Width = 260, Height = 160, Content = list };

        try
        {
            window.Show();
            window.UpdateLayout();
            list.FocusAccount(first);

            window.KeyPress(
                Key.Down,
                RawInputModifiers.None,
                PhysicalKey.ArrowDown,
                null);

            Assert.Same(second, list.SelectedItem);
            Assert.True(Assert.Single(
                list.GetVisualDescendants().OfType<ListBoxItem>(),
                item => ReferenceEquals(item.DataContext, second)).IsFocused);
        }
        finally
        {
            window.Close();
        }
    }

    [Theory]
    [InlineData(Key.Up, KeyModifiers.None, true, true, true, true)]
    [InlineData(Key.Up, KeyModifiers.None, true, true, false, false)]
    [InlineData(Key.Up, KeyModifiers.None, true, false, true, false)]
    [InlineData(Key.Up, KeyModifiers.None, false, true, true, false)]
    [InlineData(Key.Up, KeyModifiers.Control, true, true, true, false)]
    [InlineData(Key.Down, KeyModifiers.None, true, true, true, false)]
    public void FirstSearchResultUpArrow_ReturnsFocusOnlyToVisibleSearch(
        Key key,
        KeyModifiers modifiers,
        bool canNavigate,
        bool isListSource,
        bool isAtFirstResult,
        bool expected)
    {
        var policy = typeof(MainWindow).GetMethod(
            "ShouldMoveFocusToSearch",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        Assert.NotNull(policy);
        Assert.Equal(
            expected,
            policy.Invoke(null, [key, modifiers, canNavigate, isListSource, isAtFirstResult]));
    }

    [Theory]
    [InlineData(Key.F, KeyModifiers.Control, true, true)]
    [InlineData(Key.F, KeyModifiers.Control, false, false)]
    [InlineData(Key.F, KeyModifiers.Control | KeyModifiers.Shift, true, false)]
    [InlineData(Key.G, KeyModifiers.Control, true, false)]
    public void SearchShortcut_RequiresExactControlFAndAvailableSearch(
        Key key,
        KeyModifiers modifiers,
        bool canFocus,
        bool expected)
    {
        var policy = typeof(MainWindow).GetMethod(
            "ShouldFocusAccountSearch",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        Assert.NotNull(policy);
        Assert.Equal(expected, policy.Invoke(null, [key, modifiers, canFocus]));
    }

    [AvaloniaFact]
    public void AccountCopyShortcut_PreservesCopyingSelectedSearchText()
    {
        var policy = typeof(MainWindow).GetMethod(
            "HasSelectedText",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var searchBox = new TextBox { Text = "github" };

        searchBox.SelectionStart = searchBox.SelectionEnd = searchBox.Text.Length;
        Assert.False((bool)policy!.Invoke(null, [searchBox])!);

        searchBox.SelectionStart = 0;
        Assert.True((bool)policy.Invoke(null, [searchBox])!);
    }

    [AvaloniaFact]
    public void WindowNotificationBanner_UsesSharedBottomNonInteractiveOverlay()
    {
        var notification = new NotificationBanner
        {
            Width = 240,
            HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Bottom,
            IsHitTestVisible = false,
            Severity = NotificationSeverity.Success,
            Text = "Synthetic account saved"
        };
        var content = new Border { Height = 300 };
        var host = new Grid { Children = { content, notification } };
        var window = new Window { Content = host };

        try
        {
            window.Show();
            window.UpdateLayout();

            Assert.Equal(300, content.Bounds.Height);
            Assert.True(notification.IsVisible);
            Assert.False(notification.IsHitTestVisible);
            Assert.Equal(VerticalAlignment.Bottom, notification.VerticalAlignment);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ToolbarIconStyle_LeavesFullHeightForSearchSymbol()
    {
        var symbol = new SymbolIcon
        {
            Kind = SymbolIconKind.Search,
            IconSize = 16
        };
        var button = new Button { Content = symbol };
        button.Classes.Add("icon");
        var window = new Window { Content = button };

        try
        {
            window.Show();
            button.ApplyTemplate();
            symbol.ApplyTemplate();
            window.UpdateLayout();

            Assert.Equal(16, symbol.Bounds.Width);
            Assert.Equal(16, symbol.Bounds.Height);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void LanguageFlagBinding_DecodesAndProducesImageSource()
    {
        using var flags = new AvaloniaLanguageFlagProvider();
        var localization = new AvaloniaLocalizationService(
            new ResourceDictionary(),
            new AvaloniaStringCatalog(),
            flags);
        var images = localization.SupportedLanguages.Select(option =>
        {
            var image = new Image { DataContext = option };
            image.Bind(Image.SourceProperty, new Binding(nameof(LanguageOption.Icon)));
            return image;
        }).ToArray();
        var window = new Window
        {
            Content = new StackPanel { Children = { images[0], images[1] } }
        };

        try
        {
            window.Show();

            Assert.All(images, image =>
            {
                Assert.NotNull(image.Source);
                Assert.True(image.Source.Size.Width > 0);
                Assert.True(image.Source.Size.Height > 0);
            });
            Assert.NotSame(images[0].Source, images[1].Source);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void CameraScannerDialog_LoadsRealXaml()
    {
        var window = new CameraScannerDialogWindow();

        try
        {
            window.Show();

            Assert.Equal(2, window.GetVisualDescendants().OfType<Image>().Count());
            Assert.Single(window.GetVisualDescendants().OfType<ProgressBar>());
            Assert.Equal(
                4,
                window.GetVisualDescendants().OfType<Button>().Count(button => button.IsVisible));
            var titleBar = Assert.Single(window.GetVisualDescendants().OfType<ProductTitleBar>());
            Assert.False(titleBar.ShowMinimizeButton);
            Assert.Equal(WindowDecorations.None, window.WindowDecorations);
            Assert.Equal(560, window.Width);
            Assert.Equal(420, window.Height);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void AccountRow_WithoutAccountNameOmitsSecondaryLine()
    {
        var row = new AccountRow { Issuer = "Issuer" };
        var window = new Window { Content = row };

        try
        {
            window.Show();
            row.ApplyTemplate();
            window.UpdateLayout();

            var text = row.GetVisualDescendants().OfType<TextBlock>().ToArray();
            Assert.Equal(2, text.Length);
            Assert.Equal("Issuer", text[0].Text);
            Assert.False(text[1].IsVisible);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void CameraScannerDialog_CancelButtonSizesToLocalizedContent()
    {
        var window = new CameraScannerDialogWindow();

        try
        {
            window.Show();
            var cancel = Assert.Single(
                window.GetVisualDescendants().OfType<Button>(),
                button => button.IsCancel);
            cancel.Content = "Scan abbrechen";
            window.UpdateLayout();

            Assert.True(double.IsNaN(cancel.Width));
            Assert.True(cancel.Bounds.Width > 100);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ConfirmationDialog_UsesChromelessDistinctDialogSurface()
    {
        var application = Assert.IsType<App>(Application.Current);
        application.RequestedThemeVariant = ThemeVariant.Dark;
        var window = new ConfirmationDialogWindow();

        try
        {
            window.Show();

            Assert.Equal(WindowDecorations.None, window.WindowDecorations);
            Assert.Equal(
                Color.Parse("#192B52"),
                Assert.IsType<SolidColorBrush>(window.Background).Color);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ActionDialogs_MatchTitlelessWpfPromptChrome()
    {
        Window[] windows =
        [
            new ConfirmationDialogWindow(),
            new PasswordDialogWindow(),
            new ChoiceDialogWindow()
        ];

        try
        {
            foreach (var window in windows)
            {
                window.Show();

                Assert.Equal(WindowDecorations.None, window.WindowDecorations);
                Assert.Empty(window.GetVisualDescendants().OfType<ProductTitleBar>());
            }
        }
        finally
        {
            foreach (var window in windows)
                window.Close();
        }
    }

    [AvaloniaFact]
    public void QrPreviewDialog_TitleHasLeftPaddingWhenIconIsHidden()
    {
        var window = new QrPreviewDialogWindow();

        try
        {
            window.Show();
            var titleBar = Assert.Single(
                window.GetVisualDescendants().OfType<ProductTitleBar>());

            Assert.False(titleBar.ShowIcon);
            Assert.Equal(new Thickness(8, 0, 0, 0), titleBar.TitlePadding);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ProductTitleBar_CloseGlyphIsCenteredInItsHoverTarget()
    {
        var titleBar = new ProductTitleBar();
        var window = new Window { Content = titleBar };

        try
        {
            window.Show();
            window.UpdateLayout();

            var closeButton = Assert.Single(
                titleBar.GetVisualDescendants().OfType<Button>(),
                button => button.Classes.Contains("titlebar-close"));

            Assert.Equal(HorizontalAlignment.Center, closeButton.HorizontalContentAlignment);
            Assert.Equal(VerticalAlignment.Center, closeButton.VerticalContentAlignment);
            Assert.Equal(34, closeButton.Bounds.Width);
            Assert.Equal(33, closeButton.Bounds.Height);

            var glyph = Assert.Single(closeButton.GetVisualDescendants().OfType<AvaloniaPath>());
            var glyphCenter = glyph.TranslatePoint(
                new Point(glyph.Bounds.Width / 2, glyph.Bounds.Height / 2),
                closeButton);
            Assert.NotNull(glyphCenter);
            Assert.Equal(closeButton.Bounds.Width / 2, glyphCenter.Value.X, 3);
            Assert.Equal(closeButton.Bounds.Height / 2, glyphCenter.Value.Y, 3);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ProductTitleBar_MinimizeButtonMinimizesOwningWindow()
    {
        var titleBar = new ProductTitleBar { ShowMinimizeButton = true };
        var window = new Window { Content = titleBar };

        try
        {
            window.Show();
            window.UpdateLayout();

            var minimizeButton = Assert.Single(
                titleBar.GetVisualDescendants().OfType<Button>(),
                button => button.Classes.Contains("titlebar-minimize"));

            Assert.True(minimizeButton.IsVisible);
            minimizeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.Equal(WindowState.Minimized, window.WindowState);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task ProductTitleBar_MaximizeToggleRestoresOriginalWindowSize()
    {
        var titleBar = new ProductTitleBar();
        var window = new Window
        {
            Width = 420,
            Height = 300,
            MinWidth = 200,
            MinHeight = 150,
            Content = titleBar
        };

        try
        {
            window.Show();
            window.UpdateLayout();
            var originalSize = window.Bounds.Size;
            var toggle = typeof(ProductTitleBar).GetMethod(
                "ToggleMaximizedState",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.NotNull(toggle);

            toggle.Invoke(titleBar, [window]);
            Assert.Equal(WindowState.Maximized, window.WindowState);
            window.Width = 900;
            window.Height = 700;

            toggle.Invoke(titleBar, [window]);
            await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Background);

            Assert.Equal(WindowState.Normal, window.WindowState);
            Assert.Equal(originalSize.Width, window.Width, precision: 2);
            Assert.Equal(originalSize.Height, window.Height, precision: 2);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void QrPreviewDialog_EscapeClosesWindow()
    {
        var window = new QrPreviewDialogWindow();

        window.Show();
        window.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = Key.Escape
        });

        Assert.False(window.IsVisible);
    }

    [AvaloniaFact]
    public void MainWindow_LoadsRealXamlAndEssentialSurfaces()
    {
        var window = new MainWindow();

        try
        {
            window.Show();

            Assert.NotNull(window.Icon);
            Assert.Equal(WindowDecorations.None, window.WindowDecorations);
            Assert.Empty(window.KeyBindings);
            var titleBar = Assert.Single(window.GetVisualDescendants().OfType<ProductTitleBar>());
            Assert.Equal(window.Title, titleBar.Title);
            Assert.True(titleBar.ShowMinimizeButton);
            Assert.Single(window.GetVisualDescendants().OfType<BusyOverlay>());
            Assert.True(window.GetVisualDescendants().OfType<Button>().Count() >= 5);
            Assert.True(window.GetVisualDescendants().OfType<Border>().Count() >= 5);
            Assert.NotEmpty(window.GetVisualDescendants().OfType<ScrollViewer>());
            Assert.Empty(window.GetVisualDescendants().OfType<TabControl>());
            Assert.True(AssetLoader.Exists(new Uri(
                "avares://TOTP.UI.Avalonia.Desktop/Assets/flags/en.png")));
            Assert.True(AssetLoader.Exists(new Uri(
                "avares://TOTP.UI.Avalonia.Desktop/Assets/flags/de.png")));
            var languageSelector = Assert.Single(
                window.GetVisualDescendants().OfType<ComboBox>(),
                combo => combo.Width == 64
                    && combo.HorizontalContentAlignment == global::Avalonia.Layout.HorizontalAlignment.Center);
            Assert.Same(languageSelector, window.FindControl<ComboBox>("LanguageSelector"));
            AssertToolbarAutomationName(window, "AddAccountButton", AvaloniaStringKeys.AddAccount);
            AssertToolbarAutomationName(window, "ToggleSearchButton", AvaloniaStringKeys.SearchAccounts);
            AssertToolbarAutomationName(window, "ManageGroupsButton", AvaloniaStringKeys.CreateGroup);
            var manageGroupsButton = window.FindControl<Button>("ManageGroupsButton")!;
            var toggleSearchButton = window.FindControl<Button>("ToggleSearchButton")!;
            Assert.Equal(2, Grid.GetColumn(manageGroupsButton));
            Assert.Equal(3, Grid.GetColumn(toggleSearchButton));
            Assert.Equal(
                SymbolIconKind.FolderAdd,
                Assert.IsType<SymbolIcon>(manageGroupsButton.Content).Kind);
            AssertToolbarAutomationName(window, "ClearSearchButton", AvaloniaStringKeys.ClearSearch);
            AssertToolbarAutomationName(window, "OpenSettingsButton", AvaloniaStringKeys.Settings);
            AssertToolbarAutomationName(window, "LockButton", AvaloniaStringKeys.Lock);
            Assert.Equal(
                Application.Current!.Resources[AvaloniaStringKeys.Language],
                AutomationProperties.GetName(languageSelector));
            languageSelector.ItemsSource = new[] { new LanguageOption("en", "English") };
            languageSelector.SelectedIndex = 0;
            window.UpdateLayout();

            var languageFlag = Assert.Single(languageSelector.GetVisualDescendants().OfType<Image>());
            Assert.Equal(new Thickness(8, 0, 0, 0), languageFlag.Margin);
            Assert.Equal(380, window.Width);
            Assert.Equal(540, window.Height);
            Assert.Equal(360, window.MinWidth);
            Assert.Equal(200, window.MinHeight);
            var screen = window.Screens.ScreenFromWindow(window);
            Assert.NotNull(screen);
            Assert.InRange(
                window.MaxWidth,
                0,
                (screen.WorkingArea.Width / screen.Scaling) * 0.92);
            Assert.InRange(
                window.MaxHeight,
                0,
                (screen.WorkingArea.Height / screen.Scaling) * 0.60);
            Assert.Equal(WindowStartupLocation.CenterScreen, window.WindowStartupLocation);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void MainToolbar_TabAndShiftTabFollowVisualWorkflowOrder()
    {
        var window = new MainWindow();

        try
        {
            window.Show();
            window.FindControl<Grid>("AuthorizedShell")!.IsVisible = true;
            window.FindControl<Border>("MainToolbar")!.IsEnabled = true;
            window.FindControl<Grid>("AccountSearchHost")!.IsVisible = true;
            window.FindControl<Button>("ClearSearchButton")!.IsVisible = true;
            window.UpdateLayout();

            Control[] expectedOrder =
            [
                window.FindControl<Button>("ScanQrButton")!,
                window.FindControl<Button>("AddAccountButton")!,
                window.FindControl<Button>("ManageGroupsButton")!,
                window.FindControl<Button>("ToggleSearchButton")!,
                window.FindControl<TextBox>("AccountSearchBox")!,
                window.FindControl<Button>("ClearSearchButton")!,
                window.FindControl<ComboBox>("LanguageSelector")!,
                window.FindControl<Button>("OpenSettingsButton")!,
                window.FindControl<Button>("LockButton")!
            ];
            foreach (var button in expectedOrder.OfType<Button>())
                button.Command = new TestCommand(static () => { });
            window.UpdateLayout();

            Assert.True(
                expectedOrder[0].IsEffectivelyVisible,
                "The scanner button must be effectively visible for focus traversal.");
            Assert.True(
                expectedOrder[0].IsEffectivelyEnabled,
                "The scanner button must be effectively enabled for focus traversal.");
            Assert.True(expectedOrder[0].Focusable);
            Assert.True(expectedOrder[0].Focus());
            Assert.True(expectedOrder[0].IsFocused);
            for (var index = 1; index < expectedOrder.Length; index++)
            {
                window.KeyPress(
                    Key.Tab,
                    RawInputModifiers.None,
                    PhysicalKey.Tab,
                    null);
                Assert.True(
                    expectedOrder[index].IsFocused,
                    $"Expected forward focus on {expectedOrder[index].Name}.");
            }

            for (var index = expectedOrder.Length - 2; index >= 0; index--)
            {
                window.KeyPress(
                    Key.Tab,
                    RawInputModifiers.Shift,
                    PhysicalKey.Tab,
                    null);
                Assert.True(
                    expectedOrder[index].IsFocused,
                    $"Expected reverse focus on {expectedOrder[index].Name}.");
            }
        }
        finally
        {
            window.Close();
        }
    }

    private static void AssertToolbarAutomationName(
        MainWindow window,
        string controlName,
        string resourceKey)
    {
        var button = window.FindControl<Button>(controlName);

        Assert.NotNull(button);
        Assert.Equal(
            Application.Current!.Resources[resourceKey],
            AutomationProperties.GetName(button));
    }

    [AvaloniaFact]
    public void AccountEditorContent_KeepsNarrowClearanceFromOverlayScrollBar()
    {
        var mainWindow = new MainWindow();
        var templateHost = new Window { Width = 380, Height = 540 };

        try
        {
            mainWindow.Show();
            var accountPage = mainWindow.FindControl<ContentControl>("AccountListPage");
            Assert.NotNull(accountPage);
            Assert.NotNull(accountPage.ContentTemplate);
            templateHost.Content = accountPage.ContentTemplate.Build(null);
            templateHost.Show();
            var flyout = Assert.Single(
                templateHost.GetLogicalDescendants().OfType<Border>(),
                border => border.Name == "AccountEditorFlyout");
            var advancedOptions = Assert.Single(
                templateHost.GetLogicalDescendants().OfType<Expander>(),
                expander => expander.Name == "AccountAdvancedOptions");
            var brandIconPicker = Assert.Single(
                templateHost.GetLogicalDescendants().OfType<TypeAheadComboBox>(),
                comboBox => comboBox.Name == "AccountBrandIconComboBox");
            var scrollViewer = Assert.Single(
                templateHost.GetVisualDescendants().OfType<ScrollViewer>(),
                viewer => viewer.Name == "AccountEditorScrollViewer");
            flyout.IsVisible = true;
            advancedOptions.IsExpanded = true;
            scrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Visible;
            templateHost.UpdateLayout();

            Assert.True(brandIconPicker.IsVisible);
            Assert.True(brandIconPicker.Bounds.Height > 0);

            var accountEditorContent = Assert.Single(
                templateHost.GetLogicalDescendants().OfType<StackPanel>(),
                panel => panel.Name == "AccountEditorContent");
            Assert.Equal(new Thickness(0, 10, 20, 10), accountEditorContent.Margin);

            var verticalScrollBar = Assert.Single(
                scrollViewer.GetVisualDescendants().OfType<ScrollBar>(),
                scrollBar => scrollBar.Orientation == Orientation.Vertical && scrollBar.IsVisible);
            var issuer = Assert.Single(
                templateHost.GetVisualDescendants().OfType<TextBox>(),
                textBox => textBox.Name == "AccountIssuerBox");
            var issuerRight = issuer.TranslatePoint(new Point(issuer.Bounds.Width, 0), scrollViewer);
            var scrollBarLeft = verticalScrollBar.TranslatePoint(new Point(0, 0), scrollViewer);
            Assert.NotNull(issuerRight);
            Assert.NotNull(scrollBarLeft);
            Assert.InRange(scrollBarLeft.Value.X - issuerRight.Value.X, 4, 12);
        }
        finally
        {
            templateHost.Close();
            mainWindow.Close();
        }
    }

    [AvaloniaFact]
    public void GroupEditorFlyout_ProvidesBoundedNameColorAndAccountSelection()
    {
        var mainWindow = new MainWindow();
        var templateHost = new Window { Width = 380, Height = 540 };

        try
        {
            mainWindow.Show();
            var accountPage = mainWindow.FindControl<ContentControl>("AccountListPage");
            Assert.NotNull(accountPage?.ContentTemplate);
            templateHost.Content = accountPage.ContentTemplate.Build(null);
            templateHost.Show();
            var groupScroller = Assert.Single(
                templateHost.GetLogicalDescendants().OfType<ScrollViewer>(),
                viewer => viewer.Name == "GroupCardsScrollViewer");
            Assert.Equal(ScrollBarVisibility.Hidden, groupScroller.HorizontalScrollBarVisibility);
            var groupBackButton = Assert.Single(
                templateHost.GetLogicalDescendants().OfType<Button>(),
                button => button.Name == "GroupBackButton");
            Assert.Equal(
                Application.Current!.Resources[AvaloniaStringKeys.AllAccounts],
                AutomationProperties.GetName(groupBackButton));
            Assert.Same(groupScroller.Parent, groupBackButton.Parent);
            Assert.Equal(0, Grid.GetColumn(groupBackButton));
            Assert.Equal(1, Grid.GetColumn(groupScroller));
            var groupNavigationGrid = Assert.IsType<Grid>(groupScroller.Parent);
            Assert.Equal(new GridLength(10), groupNavigationGrid.ColumnDefinitions[0].Width);
            Assert.Equal(new Thickness(-10, 0, 0, 0), groupBackButton.Margin);
            Assert.Equal(54, groupBackButton.Height);
            Assert.Equal(20, groupBackButton.Width);
            Assert.Empty(groupBackButton.GetLogicalDescendants().OfType<TextBlock>());
            var flyout = Assert.Single(
                templateHost.GetLogicalDescendants().OfType<Border>(),
                border => border.Name == "GroupEditorFlyout");
            flyout.IsVisible = true;
            templateHost.UpdateLayout();

            var name = Assert.Single(
                templateHost.GetLogicalDescendants().OfType<TextBox>(),
                textBox => textBox.Name == "GroupNameBox");
            Assert.Equal(AccountGroupPolicy.MaximumNameLength, name.MaxLength);
            Assert.Single(
                flyout.GetLogicalDescendants().OfType<TextBox>(),
                textBox => textBox.Name == "GroupAccountSearchBox");
            var colorList = Assert.Single(
                flyout.GetLogicalDescendants().OfType<ListBox>());
            Assert.Equal(SelectionMode.Single, colorList.SelectionMode);
            Assert.Equal(ScrollBarVisibility.Disabled, ScrollViewer.GetHorizontalScrollBarVisibility(colorList));
            Assert.Equal(ScrollBarVisibility.Disabled, ScrollViewer.GetVerticalScrollBarVisibility(colorList));
            var accountPickerScroller = Assert.Single(
                flyout.GetLogicalDescendants().OfType<ScrollViewer>(),
                viewer => viewer.MaxHeight == 240);
            Assert.Equal(ScrollBarVisibility.Hidden, accountPickerScroller.VerticalScrollBarVisibility);
            Assert.True(flyout.Bounds.Height > 0);
        }
        finally
        {
            templateHost.Close();
            mainWindow.Close();
        }
    }

    [AvaloniaFact]
    public void SettingsWindow_IsOwnedWindowWidthWithSingleRowTabs()
    {
        var window = new SettingsWindow();

        try
        {
            window.Show();
            window.UpdateLayout();

            Assert.Equal(520, window.Width);
            Assert.Equal(520, window.MinWidth);
            Assert.True(window.Topmost);
            Assert.Equal(WindowDecorations.None, window.WindowDecorations);
            Assert.Empty(window.GetVisualDescendants().OfType<ProductTitleBar>());
            var settingsTabs = Assert.Single(
                window.GetVisualDescendants().OfType<TabControl>(),
                tabControl => tabControl.Classes.Contains("settings-tabs"));
            var tabs = settingsTabs.GetVisualDescendants().OfType<TabItem>().ToArray();
            Assert.Equal(4, tabs.Length);
            Assert.True(tabs.Sum(tab => tab.MinWidth) <= window.Width - 32);
            Assert.All(
                tabs,
                tab => Assert.Equal(tabs[0].Bounds.Width, tab.Bounds.Width, precision: 2));
            for (var index = 1; index < tabs.Length; index++)
            {
                Assert.Equal(
                    tabs[index - 1].Bounds.Right,
                    tabs[index].Bounds.Left,
                    precision: 2);
            }
            Assert.All(
                tabs,
                tabItem =>
                {
                    Assert.Equal(104, tabItem.MinWidth);
                    Assert.Equal(12, tabItem.FontSize);
                    Assert.Equal(FontWeight.SemiBold, tabItem.FontWeight);
                    Assert.Equal(
                        global::Avalonia.Layout.HorizontalAlignment.Center,
                        tabItem.HorizontalContentAlignment);
                });
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void MasterPasswordChange_UsesNeutralActionStyle()
    {
        var window = new SettingsWindow();

        try
        {
            window.Show();
            window.UpdateLayout();

            var authorizationHost = Assert.Single(
                window.GetVisualDescendants().OfType<ContentControl>(),
                control => control.Name == "AuthorizationSettingsHost");
            var contentTemplate = authorizationHost.ContentTemplate;
            Assert.NotNull(contentTemplate);
            var content = Assert.IsAssignableFrom<Control>(contentTemplate.Build(null));
            var changeButton = Assert.Single(
                content.GetLogicalDescendants().OfType<Button>(),
                control => control.Name == "MasterPasswordChangeButton");

            Assert.DoesNotContain("primary", changeButton.Classes);
            Assert.DoesNotContain("danger", changeButton.Classes);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void SettingsTabs_UseOneBottomOverlayOutsideTabContent()
    {
        var window = new SettingsWindow();

        try
        {
            window.Show();
            var settingsTabs = Assert.Single(
                window.GetVisualDescendants().OfType<TabControl>(),
                tabControl => tabControl.Classes.Contains("settings-tabs"));
            NotificationBanner? overlay = null;
            for (var tabIndex = 0; tabIndex < 4; tabIndex++)
            {
                settingsTabs.SelectedIndex = tabIndex;
                window.UpdateLayout();

                var currentOverlay = Assert.Single(
                    window.GetVisualDescendants().OfType<NotificationBanner>(),
                    control => control.Name == "SettingsNotificationToast");
                overlay ??= currentOverlay;
                Assert.Same(overlay, currentOverlay);
                Assert.Equal(VerticalAlignment.Bottom, currentOverlay.VerticalAlignment);
                Assert.False(currentOverlay.IsHitTestVisible);
                Assert.DoesNotContain(
                    currentOverlay,
                    settingsTabs.GetVisualDescendants());
            }
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void SecurityBehaviorSettings_AreInsideSecurityTab()
    {
        var window = new SettingsWindow();
        using var settingsPage = new SettingsPageViewModel(new TestSettingsService());

        try
        {
            window.Show();
            var settingsTabs = Assert.Single(
                window.GetVisualDescendants().OfType<TabControl>(),
                tabControl => tabControl.Classes.Contains("settings-tabs"));
            settingsTabs.SelectedIndex = 0;
            window.UpdateLayout();
            var settingsHost = Assert.Single(
                window.GetVisualDescendants().OfType<ContentControl>(),
                control => control.Name == "SecurityBehaviorSettingsHost");
            settingsHost.Content = settingsPage;
            window.UpdateLayout();

            var scrollViewer = Assert.Single(
                window.GetVisualDescendants().OfType<ScrollViewer>(),
                control => control.Name == "SecuritySettingsScroll");
            var behaviorSettings = Assert.Single(
                window.GetVisualDescendants().OfType<Border>(),
                control => control.Name == "SecurityBehaviorSettings");
            Assert.Contains(behaviorSettings, scrollViewer.GetVisualDescendants());
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void DarkVariant_UsesOriginalNavyAndPurplePalette()
    {
        var application = Assert.IsType<App>(Application.Current);
        application.RequestedThemeVariant = ThemeVariant.Dark;
        var window = new MainWindow();

        try
        {
            window.Show();

            Assert.True(window.TryFindResource(
                "BrushWindowBackground",
                ThemeVariant.Dark,
                out var background));
            Assert.Equal(
                Color.Parse("#0C1C33"),
                Assert.IsType<SolidColorBrush>(background).Color);
            Assert.True(window.TryFindResource(
                "BrushAccent",
                ThemeVariant.Dark,
                out var accent));
            Assert.Equal(
                Color.Parse("#7D7FF4"),
                Assert.IsType<SolidColorBrush>(accent).Color);
        }
        finally
        {
            window.Close();
            application.RequestedThemeVariant = ThemeVariant.Dark;
        }
    }

    [AvaloniaFact]
    public void LightVariant_UsesBrightSurfaceAndMatchingBlueAccent()
    {
        var application = Assert.IsType<App>(Application.Current);
        application.RequestedThemeVariant = ThemeVariant.Light;
        var window = new MainWindow();

        try
        {
            window.Show();

            Assert.True(window.TryFindResource(
                "BrushWindowBackground",
                ThemeVariant.Light,
                out var background));
            Assert.Equal(
                Colors.White,
                Assert.IsType<SolidColorBrush>(background).Color);
            Assert.True(window.TryFindResource(
                "BrushAccountItemBackground",
                ThemeVariant.Light,
                out var accountBackground));
            Assert.Equal(
                Color.Parse("#F2F4FB"),
                Assert.IsType<SolidColorBrush>(accountBackground).Color);
            Assert.True(window.TryFindResource(
                "BrushAccent",
                ThemeVariant.Light,
                out var accent));
            Assert.Equal(
                Color.Parse("#168AE0"),
                Assert.IsType<SolidColorBrush>(accent).Color);
        }
        finally
        {
            window.Close();
            application.RequestedThemeVariant = ThemeVariant.Dark;
        }
    }

    [AvaloniaFact]
    public void ExpiringAccountProgress_UsesCountdownWarningRed()
    {
        var application = Assert.IsType<App>(Application.Current);
        application.RequestedThemeVariant = ThemeVariant.Light;
        var progress = new ProgressBar { Maximum = 30, Value = 10 };
        progress.Classes.Add("account-countdown");
        progress.Classes.Add("expiring");
        var window = new Window { Content = progress };

        try
        {
            window.Show();
            window.UpdateLayout();

            Assert.Equal(
                Color.Parse("#F44336"),
                Assert.IsType<SolidColorBrush>(progress.Foreground).Color);
        }
        finally
        {
            window.Close();
            application.RequestedThemeVariant = ThemeVariant.Dark;
        }
    }

    [AvaloniaFact]
    public void AccountCountdownProgress_RendersAsOneDipHairline()
    {
        var progress = new ProgressBar
        {
            Width = 200,
            Maximum = 30,
            Value = 15
        };
        progress.Classes.Add("account-countdown");
        var window = new Window { Content = progress };

        try
        {
            window.Show();
            window.UpdateLayout();

            Assert.Equal(1, progress.Bounds.Height, precision: 2);
            Assert.Equal(0, progress.MinHeight);
            Assert.Equal(1, progress.MaxHeight);
            Assert.True(progress.ClipToBounds);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void HighContrastVariant_ResolvesDedicatedSemanticPalette()
    {
        var application = Assert.IsType<App>(Application.Current);
        application.RequestedThemeVariant = AvaloniaThemeVariants.HighContrast;
        var window = new MainWindow();

        try
        {
            window.Show();

            Assert.True(window.TryFindResource(
                "BrushWindowBackground",
                AvaloniaThemeVariants.HighContrast,
                out var background));
            Assert.Equal(Colors.Black, Assert.IsType<SolidColorBrush>(background).Color);
            Assert.True(window.TryFindResource(
                "BrushFocus",
                AvaloniaThemeVariants.HighContrast,
                out var focus));
            Assert.Equal(Color.Parse("#00FFFF"), Assert.IsType<SolidColorBrush>(focus).Color);
        }
        finally
        {
            window.Close();
            application.RequestedThemeVariant = ThemeVariant.Dark;
        }
    }

    private sealed class TestCommand(Action execute) : ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter) => execute();
    }

    private sealed class TestSettingsService : ISettingsService
    {
        public IAppSettings Current { get; } = new AppSettings();

        public Task<Result<IAppSettings>> LoadAsync() =>
            Task.FromResult(Result.Ok(Current));

        public Task<Result> SaveAsync() => Task.FromResult(Result.Ok());
    }

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));

        while (!predicate())
            await Task.Delay(10, timeout.Token);
    }
}
